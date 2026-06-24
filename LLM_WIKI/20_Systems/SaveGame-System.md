---
type: system
project: AutoCrawler
system: SaveGame
updated: 2026-06-18
tags: [system, save-game, section]
---

# SaveGame System

재사용 가능한 SaveGame core. 저장 가능한 최소 단위(`SaveSection`)를 상속해 저장 대상을 늘린다.
SaveGame core는 WorldState/DialogueTool 등 domain-specific system을 직접 참조하지 않는다(ADR-013).

**SG-001 Step 1~5 완료**: in-memory core(Step 1) + 파일 slot store(Step 2) + WorldState adapter(Step 3) +
backup/recovery(Step 4) + 패키징/User Guide 문서·completion review(Step 5,
[[SG-001-SaveGame-Core-Section-System-Review]]).

**SG-002 Step 1~3 완료**: `SaveFlow` facade + metadata provider + save gate provider(아래 SaveFlow 절,
[[SG-002-SaveFlow-Facade-Metadata-Provider-Review]] 판정 완료).

**SG-003 Step 1~3 완료**: host save slot UI integration contract(문서 + test-only fake host flow). SaveGame은
production save/load UI scene/theme를 제공하지 않고, host가 `SaveFlow` raw report를 직접 소비하는 규칙만
[[SaveGame-User-Guide]] §12에 문서화한다(아래 SaveFlow 절 끝, [[SG-003-SaveSlot-UI-Host-Integration-Review]]
판정 완료).

## 위치

- `addons/world_core/save_game/save_section.gd` — `class_name SaveSection`(domain-free core)
- `addons/world_core/save_game/save_game_manager.gd` — `class_name SaveGameManager`(domain-free core)
- `addons/world_core/save_game/save_flow.gd` — `class_name SaveFlow`(domain-free thin facade, SG-002 Step 1)
- `addons/world_core/save_game/tests/` — core + facade 헤드리스 테스트
- `addons/world_core/save_game_world_state/world_state_save_section.gd` — `class_name WorldStateSaveSection`
  (SaveGame ↔ WorldState 통합 adapter)
- `addons/world_core/save_game_world_state/tests/` — 통합 헤드리스 테스트

ADR-013에 따라 `addons/world_core/save_game/`와 `addons/world_core/save_game_world_state/`로 이동되었다. core(`world_core/save_game/`)는
domain-free를 유지하고, WorldState 결합은 별도 디렉터리(`save_game_world_state/`)의 adapter에만 격리된다.

## SaveSection

`Node` 기반 base contract. 게임은 이 노드를 상속해 자기 system을 저장 대상으로 노출한다.

```gdscript
@export var section_id: StringName       # 빈 값/중복 금지(manager 검증)
@export var section_version: int = 1     # adapter contract version, >= 1
@export var restore_order: int = 0       # 작은 값 먼저, tie는 section_id lexical
@export var required: bool = true        # save에 없을 때 load 실패 여부

func capture_save() -> Dictionary        # { ok, payload, reason }
func validate_save(payload) -> Dictionary # { ok, reason } (read-only)
func restore_save(payload) -> Dictionary  # { ok, reason }
```

기본 구현은 안전한 no-op이며 상속 노드가 override 한다. payload는 JSON 호환 Dictionary여야 하고
core는 내부를 해석하지 않는다.

## SaveGameManager

`Node` 기반 in-memory orchestrator. 모든 public API는 보고형 Dictionary를 반환한다.

### 등록 / 발견

- `register_section(section) -> Dictionary` — 1순위 명시 등록. null/빈 id/`section_version<1`/중복 id 거부.
- `unregister_section(section_or_id) -> Dictionary`
- `discover_sections(root := self, include_groups := false) -> Dictionary` — 보조 helper. root 하위
  트리(+선택 group)에서 `SaveSection`을 찾아 등록. 전체 SceneTree 자동 검색은 기본 동작이 아니다.
- `get_ordered_sections()` / `get_section_ids()` — deterministic 순서(restore_order, 그다음 id lexical).

### Envelope

`capture_all()`이 만드는 in-memory envelope(JSON 호환):

```json
{ "save_version": 1, "sections": { "<id>": { "section_version": 1, "payload": {} } } }
```

version 3계층 분리:
- `save_version` — manager(core) envelope version. mismatch 시 load 실패(`save_version_mismatch`).
- `section_version` — `SaveSection` adapter contract version. required mismatch 시 load 실패
  (`section_version_mismatch`), 비-required mismatch는 `skipped_sections` report.
- payload `schema_version` — payload/domain 소유. core 미해석, section `validate_save()`가 판단.

### Transaction

- `capture_all() -> Dictionary` — deterministic 순서로 모든 section capture. 한 section이라도 capture
  실패면 envelope를 만들지 않고 `capture_failed`+failed id 반환.
- `validate_envelope(envelope) -> Dictionary` — restore 없이 비파괴 검증. `{ ok, reason, errors,
  ignored_sections, missing_required, skipped_sections, plan }`.
- `restore_all(envelope) -> Dictionary` — validate 통과해야 restore 시작(validate 실패 시 restore 0회).
  restore 중간 실패 시 즉시 중단하고 `partial_restore` report(이미 복원된 id + 실패 id/reason). 이미 복원된
  section은 manager가 되돌리지 않는다.

### 파일 slot store (Step 2)

`user://saves/<slot>.json`에 envelope를 저장/로드한다. 파일 API는 `{ ok, slot_id, error, ... }` 형태.

- `save_slot(slot_id, metadata := {}) -> Dictionary` — `capture_all()`로 envelope를 만든 뒤 slot 메타
  (`slot_id`/`created_at_unix`/`updated_at_unix`/`metadata`)를 더해 atomic write. capture 실패 시 파일 미작성.
  기존 slot의 `created_at_unix` 보존, `updated_at_unix`만 갱신.
- `load_slot(slot_id) -> Dictionary` — 파일 읽기→JSON parse→`restore_all`. 누락=`slot_not_found`,
  파싱 실패=`parse_error`, 비-Dictionary=`corrupt`(게임 상태 불변). primary가 없거나 손상이면 `.bak` 복구를
  시도하고 성공 시 `recovered_from_backup=true`/`source=&"backup"`로 보고(Step 4).
- `list_slots() -> Array[Dictionary]` — `.json` 메타(정렬). 손상 slot은 `ok:false`로 보고하되 나머지 나열을
  막지 않는다(per-slot isolation). raw `error`는 JSON 파싱 실패면 `parse_error`(`_read_envelope`), 파싱은 됐지만
  `save_version`이 정수형 number가 아니거나 timestamp가 number가 아니거나 `metadata`가 Dictionary가 아닌 구조
  손상이면 `corrupt`(`_extract_slot_meta`). `.json.tmp`/`.bak` 제외.
- `delete_slot(slot_id)` / `has_slot(slot_id)`.

정책:
- **slot_id**: `^[a-zA-Z0-9_-]{1,64}$`만 허용(`invalid_slot_id`).
- **atomic write + backup(Step 4)**: 순서 = tmp write → (기존 primary가 **유효할 때만**)
  `primary→<slot>.json.bak` rename → `tmp→primary` rename. 한 세대 백업으로 직전 good 상태를 보존하고, 백업
  rename 실패는 `backup_failed`로 save를 막아 primary를 보존한다. 첫 save에는 bak이 없다. **손상된 primary는
  회전하지 않아** good `.bak`을 덮어쓰지 않는다(데이터 손실 방지). partial write가 실제 slot을 덮지 않는다.
- **corrupt 읽기**: `_read_envelope`는 인스턴스 `JSON.new().parse()`로 error code를 받아 손상 slot을 조용히
  `parse_error`/`corrupt`로 보고한다(정적 `JSON.parse_string`의 엔진 ERROR 로그 오염 회피).
- **JSON number**: Godot `JSON.parse_string`은 모든 number를 float로 읽는다(`7`→`7.0`). JSON은 int/float를
  구분하지 않으므로 core는 JSON number를 그대로 돌려준다(추측 정규화 금지). int 의미가 필요한 section은 자기
  `restore_save`에서 정규화한다(WorldState adapter=Step 3). `save_version`/`section_version`은 `_is_integral_number`로
  INT/정수형 FLOAT만 허용한다(비정수 float `1.5`/string/null은 malformed로 거부 — `int()` 강제 변환으로 version
  contract가 우회되지 않게).

정책(공통):
- unknown saved section(등록 안 됨)은 `ignored_sections`로 report하고 실패하지 않는다.
- required section이 save에 없으면 `missing_required` + 실패. 비-required는 없어도 통과.
- `capture_all`/`restore_all` 재진입은 `busy` 실패 report로 거부한다.
- **payload JSON 호환 강제**: `capture_all`/`validate_envelope`는 payload를 재귀 검증한다(`_is_json_compatible`).
  허용 = null/bool/int(±(2^53-1))/float/String, Array(원소 호환), Dictionary(String key + 값 호환). StringName/
  Vector*/Object/Resource/Node·non-String key는 거부(`payload_not_json_compatible`). capture는 비호환 시
  envelope 미생성, validate는 required=error·비-required=skip. JSON 왕복 손실/변형을 core가 막는다.
- **등록 후 식별자 재검증**: `section_id`/`section_version`이 export var라 등록 후 바뀔 수 있으므로,
  `capture_all`/`validate_envelope`는 op 전에 `_revalidate_sections()`로 live section 필드를 재검증한다.
  `_sections`는 등록 당시 id로 keyed인데 plan/restore는 live id로 `_sections[id]`를 조회하므로, 등록 후 id가
  빈/중복뿐 아니라 **다른 고유 값으로 바뀌어도**(`section_id_changed`) lookup miss/null 접근이 나기 전에 거부한다
  (freed/`section_id_empty`/`section_id_changed`/`section_version_invalid` 검출). 등록 key는 항상 고유하므로
  live 중복은 곧 id 변경의 결과이고 `section_id_changed` 한 검사가 이를 포함한다. 깨졌으면 `sections_invalid`로
  거부한다(envelope key 덮어쓰기 + restore lookup miss/SCRIPT ERROR 방지).

## 검증

헤드리스 테스트(Godot 4.6.3):
- `sg001_step1_core_test`(A~Z, 26 시나리오): 등록/중복/빈 id/invalid version/discovery/ordering/capture/
  JSON 왕복/capture 실패/required missing/save_version·section_version mismatch/unknown ignored/
  validate 실패 시 restore 0회/restore order/partial restore/optional missing/busy guard/non-JSON payload
  거부(StringName·non-String key·중첩·int overflow)/유효 중첩 JSON 통과/등록 후 id 중복·빈 id·고유 rename
  재검증(restore lookup miss/SCRIPT ERROR 방지)/section_version 정수형 강제(1.5·"1" → malformed).
- `sg001_step1_static_guard_test`: core 2파일에 domain 금지 토큰 0건(코드 기준, 주석 제외).
- `sg001_step2_slot_store_test`(A~L, 12 시나리오): slot_id validation/save·has_slot/save→load 왕복/overwrite
  atomic replace(Windows rename-over)/created 보존·updated 갱신/missing·corrupt 실패/list_slots 메타+corrupt
  isolation/delete·has_slot/capture 실패 시 파일 미작성/디스크 파일 유효 JSON/구조 손상 slot list 격리.
- `sg001_step3_world_state_section_test`(A~F, 6 시나리오): new game→mutate→save→mutate→load 파일 왕복
  SAVE 복원+타입 보존(int/float/StringName)+SESSION default / load validation 실패 시 기존 WorldState 보존 /
  store not-ready·session not-ready capture 실패 시 파일 미작성 / 후속 section 실패 시 partial restore(world_state
  복원 후 중단) / duck-type runtime 반환 shape 위반 → `runtime_contract_invalid`(SCRIPT ERROR 없음).
- `sg001_step4_backup_test`(A~H, 8 시나리오): overwrite 시 bak 생성+직전 내용 보존 / 첫 save bak 없음 /
  primary 손상→bak 복구 / primary 없음→bak 복구(크래시 시뮬) / primary+bak 둘 다 손상→실패(restore 0) /
  delete가 primary+bak 모두 제거 / 손상 primary가 good bak을 덮지 않음(P1) / primary 없음+bak 손상 시 실제
  원인(parse_error) 보고(P2).

## WorldStateSaveSection (Step 3)

SaveGame ↔ WorldState 통합 adapter(`class_name WorldStateSaveSection extends SaveSection`).

- `section_id = &"world_state"`, `section_version = 1`, `restore_order = -100`(다른 gameplay section보다 이른 복원).
- `WorldStateRuntime`은 class_name이 없으므로(ADR-007 D2) NodePath(`world_state_runtime_path`, 기본
  `/root/WorldStateRuntime`) 또는 `set_runtime()` 주입으로 해석하고 duck-type 호출만 한다(preload/class_name
  미참조 → parse-safe). 계약 메서드 미충족/미해석이면 `runtime_unavailable`로 fail-closed. 메서드는 있지만
  반환 shape가 잘못된 runtime(capture/peek/restore가 Dictionary 아님)은 typed 대입 SCRIPT ERROR 대신
  `runtime_contract_invalid`로 닫는다. ready 검사는 `== true`로만 통과시킨다.
- `capture_save()`: `is_store_ready()` + `is_session_ready()`를 먼저 확인하고, 준비 안 됐으면 빈 payload 없이
  실패(`store_not_ready`/`session_not_ready`) → manager가 save 전체를 쓰지 않는다. 준비됐으면
  `capture_world_state()` snapshot을 payload로 반환.
- `validate_save(payload)`: `peek_world_state_compatibility(payload)`(비파괴).
- `restore_save(payload)`: `restore_world_state(payload)`(transactional restore: SAVE import + SESSION default,
  실패 시 기존 상태 보존). JSON round-trip의 int/float·String↔StringName 복원은 Store가 처리하므로 adapter는
  별도 정규화를 하지 않는다.
- WorldStateRuntime은 SaveGame을 모른다(역의존 없음). SG-001 Step 3에서 `peek_world_state_compatibility()`만
  추가됐다(ADR-007 D5).

## SaveFlow (SG-002 Step 1)

`SaveGameManager` 위의 thin facade(`class_name SaveFlow extends Node`). UI를 제공하지 않고, 게임 UI/메뉴/
이벤트 레이어가 envelope/backup 내부를 몰라도 호출할 수 있는 의도 중심 API만 둔다(ADR-014). core와 동일하게
domain-free이고, `save_flow.gd`도 정적 가드(`sg002_step1_static_guard_test`)로 WorldState/DialogTool 직접
참조 0을 보존한다. **SG-002 Step 1~3 완료**([[SG-002-SaveFlow-Facade-Metadata-Provider-Review]], 판정: 완료).

- `@export var manager_path: NodePath = ^"/root/SaveGame"`. manager를 소유하지 않고 호출마다 lazy resolve한다:
  1순위 `set_manager()` 주입 manager(valid일 때), 그다음 `manager_path`. 매번 `is_instance_valid`+
  `is SaveGameManager`를 재확인하므로 freed/재생성에 안전하다. 미해석 시 일반 report
  `{ ok:false, error:&"manager_unavailable" }`, `list_slots()`만 단일 실패 entry
  `{ ok:false, slot_id:&"", error:&"manager_unavailable" }`(per-slot failure entry와 shape 통일), `has_slot()`은 false.
- metadata: `set_metadata_provider(Object)`(duck-type `make_save_metadata(slot_id) -> Dictionary`). 없으면 base
  `{}`. freed/non-Object/메서드 없음=`metadata_provider_unavailable`, 반환 non-Dictionary=
  `metadata_provider_contract_invalid`(둘 다 fail-closed, `save_slot` 미호출). shallow merge(provider base →
  caller override). 최종 JSON 호환 검증은 `SaveGameManager.save_slot()` 위임(caller non-JSON이면 manager의
  `metadata_not_json_compatible`가 passthrough).
- save gate: `set_save_gate_provider(Object)`(duck-type `query_save_gate(slot_id) -> { ok:bool, reason }`).
  `can_save(slot_id)`는 gate만 질의(manager 가용성 무관). 없으면 allow, freed/non-Object/메서드 없음=
  `save_gate_unavailable`, 반환 non-Dictionary 또는 `ok` 비-bool=`save_gate_contract_invalid`(fail-closed),
  provider deny는 reason 보존. `save_manual()`은 저장 전 `can_save()` 호출, `ok:false`면 `save_slot` 미호출.
  정책 금지=`save_not_allowed`, gate 설치/계약 오류=`save_gate_unavailable`/`save_gate_contract_invalid`로 구분.
- `save_manual(slot_id, metadata := {})` report는 성공/실패 모두 `ok/slot_id/error/metadata/manager_report/gate`
  6키를 유지하고(미호출 단계 `{}`), manager report를 숨기지 않는다(원본 error 노출 + `manager_report` 보존).
- `load_manual()`은 gate 미확인, `recovered_from_backup`/`source`/`restore` 손실 없이 manager.load_slot 래핑.
  `delete_slot()`/`list_slots()`(display formatting 없음)/`has_slot()`은 manager 위임.
- 검증: `sg002_step1_save_flow_test`(A~T 20 시나리오, non-Object provider fail-closed 포함)·
  `sg002_step1_static_guard_test` ALL PASS. **Step 2(통합 usage test, 제품 코드 변경 없음)**:
  `addons/world_core/save_game_world_state/tests/sg002_step2_save_flow_world_state_test`(A~D)로 `SaveFlow`가
  `SaveGameManager + WorldStateSaveSection` 조합에서 실제 slot 왕복(타입 보존/SESSION default/metadata
  merge/manager report passthrough), store·session not-ready capture 실패 of 원본 manager report 전달,
  backup recovery report(`recovered_from_backup`/`source`/`restore`) 보존을 검증한다. 회귀 SG-001 step3/4,
  DT-006 step3/4 ALL PASS, `--import` 0 에러. User Guide/README는 Step 3.

## Host save slot UI integration (SG-003)

SaveGame core는 production save/load **UI scene/theme/layout을 제공하지 않는다**(ADR-014). save slot UI/UX는
게임마다 다르므로 host가 자기 menu를 만들고 `SaveFlow` raw report를 직접 소비한다. SG-003 산출물은 flow
contract 문서 + test-only fake host flow다(제품 helper/scene 없음). **SG-003 Step 1~3 완료**
([[SG-003-SaveSlot-UI-Host-Integration-Review]] 판정: 완료).

- 소비 규칙·report consumption matrix(list/save/load/delete)·metadata fallback 정책은 [[SaveGame-User-Guide]]
  §12에 있다. 핵심: whole-list `manager_unavailable`(단일 `slot_id:&""`) vs non-empty `slot_id` per-slot
  failure(`parse_error`/`corrupt` raw 보존) 구분, save 6키 shape + provider/gate fail-closed, load
  `recovered_from_backup`/`source`/`restore` 보존, metadata `{}`/unknown/wrong-type fallback + raw 보존.
- load/delete report는 실패 종류에 따라 키(`slot_id`/`recovered_from_backup`/`source`/`restore`)가 빠질 수
  있으므로 host는 `report.get(key, default)`로 소비한다.
- 검증: `addons/world_core/save_game/tests/sg003_step2_host_flow_test`(테스트 내부 `FakeSaveSlotHostController`,
  제품 코드/helper 추가 0)가 실제 `SaveFlow + SaveGameManager` 위에서 list 분류·per-slot 격리·metadata
  fallback·gate fail-closed·save 6키·load recovery·delete refresh를 ALL PASS로 검증한다.

## 미구현 (범위 밖 후속)

- production save menu UI scene/theme/localization/input focus(SG-003은 host integration contract만 문서화,
  실제 위젯은 host 소유), autosave/quicksave, thumbnail/capture image, Dialogue SaveEffect, 다세대 백업
  history(현재 한 세대만), compression/encryption, schema/section version migration registry — SG-001~003 범위 밖.
- `world_core` umbrella packaging migration — ADR-013 trigger 충족 시 별도 Task.

## Related

- [[SG-001-SaveGame-Core-Section-System]]
- [[ADR-013-WorldCore-Umbrella-Packaging]]
- [[ADR-007-WorldState-Runtime-Lifecycle]]
- [[World-State-System]]
