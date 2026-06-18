---
id: SG-001
type: task
status: completed
system: SaveGame, WorldState
created: 2026-06-17
updated: 2026-06-17
tags: [task, save-game, slot, section, world-core, world-state]
---

# SaveGame Core and Section System

## Goal

재사용 가능한 SaveGame core를 만들고, 저장 가능한 최소 단위(`SaveSection`)를 상속·확장해서 필요한 저장 대상을
늘릴 수 있게 한다. 첫 소비자는 `WorldStateRuntime.capture_world_state()` /
`restore_world_state(snapshot)` adapter를 감싼 `WorldStateSaveSection`이다.

SaveGame은 DialogueTool의 하위 기능이 아니라 여러 gameplay system이 공유할 수 있는 core system이어야 한다.

## User Outcome

- 게임은 slot 단위로 save/load/list/delete를 수행할 수 있다.
- SaveGame core는 WorldState, DialogueTool, future inventory/party/map system을 직접 알지 않는다.
- 각 저장 대상은 `SaveSection`을 상속한 Node로 추가한다.
- `SaveGameManager`는 명시적으로 등록된 section을 1순위로 사용하고, subtree/group discovery는 보조 helper로만
  제공한다. 중복/순서/required 여부는 manager가 검증한다.
- load는 모든 section validation을 먼저 통과해야 restore를 시작한다. 실패 시 현재 게임 상태를 보존한다.
- 첫 MVP는 WorldState SAVE snapshot만 파일에 저장하지만, envelope와 section 구조는 후속 확장을 허용한다.

## Context

- [[DT-006-WorldState-Runtime-Integration]]은 외부 SaveGame 계층용 adapter를 이미 제공한다.
  - `WorldStateRuntime.capture_world_state() -> Dictionary`
  - `WorldStateRuntime.restore_world_state(snapshot) -> Dictionary`
- [[World-State-System]]의 명시적 비책임: 실제 save file, slot, backup, autosave 정책.
- DialogueTool은 WorldState condition/mutation provider를 소비하지만, SaveGame에는 직접 의존하지 않는 방향이
  좋다. Dialogue에서 저장을 트리거해야 한다면 SaveEffect보다 event/game layer 호출을 우선 검토한다.
- DT-011에서 `DialogueTool + WorldState`는 `addons/dialogtool/` 아래에 묶였지만, SaveGame까지
  `dialogtool` 하위에 넣으면 이름과 책임이 맞지 않는다.
- 사용자와 논의한 선호 구조:

```text
addons/world_core/
  save_game/
  dialogtool/
  world_state/
  save_game_world_state/
```

이 구조는 아직 구현하지 않고, [[ADR-013-WorldCore-Umbrella-Packaging]]에서 별도 설계 결정으로 검토한다.

## Scope

### Included

- SaveGame core의 책임과 addon/package 경계 설계.
- Node 기반 `SaveSection` base contract.
- 명시적 `register_section()` 기반 section ownership + 보조 subtree/group discovery helper.
- section id/version/required/restore_order 정책.
- save envelope 구조와 version policy.
- slot file naming, metadata, list/delete API.
- atomic write와 backup 정책의 Step 분해.
- load pre-validation과 restore transaction policy.
- `WorldStateSaveSection` integration adapter 설계.
- 관련 Task/ADR/System/User Guide 문서화 계획.

### Out of Scope

- 이번 설계 세션에서 제품 코드 구현.
- save slot UI.
- cloud sync, compression, encryption.
- schema migration registry와 key alias.
- Dialogue SaveEffect 노드.
- inventory/party/map/combat 실제 section 구현.
- `world_core` 대규모 경로 이동을 SaveGame Step 구현과 같은 Step에 섞는 것.

## Design Direction

### 1. Package Boundary

선호 후보는 umbrella root다.

```text
addons/world_core/save_game/              # 순수 SaveGame core
addons/world_core/world_state/            # WorldState core
addons/world_core/dialogtool/             # DialogueTool
addons/world_core/save_game_world_state/  # SaveGame + WorldState integration adapter
```

의존 방향:

```text
save_game                 -> domain-specific system을 모름
world_state               -> save_game을 모름
dialogtool                -> save_game을 모름
save_game_world_state     -> save_game + world_state를 앎
game-specific sections    -> save_game + 해당 game system을 앎
```

패키징 이동은 경로 churn이 크므로 별도 ADR/Step으로 다룬다. SG-001의 core 설계는 이 구조를 목표 형태로 삼되,
구현 첫 Step에서 대규모 이동을 강제하지 않는다.

### 2. SaveSection Contract

Godot스러운 Node 기반을 우선한다.

```gdscript
class_name SaveSection
extends Node

@export var section_id: StringName
@export var section_version: int = 1
@export var restore_order: int = 0
@export var required: bool = true

func capture_save() -> Dictionary
func validate_save(payload: Dictionary) -> Dictionary
func restore_save(payload: Dictionary) -> Dictionary
```

SaveGameManager의 1순위 경로는 명시적 등록이다.

```gdscript
func register_section(section: SaveSection) -> Dictionary
func unregister_section(section_or_id) -> Dictionary
func discover_sections(root: Node = self, include_groups := false) -> Dictionary
```

정책:

- `section_id` 빈 값 금지.
- 중복 `section_id` 금지.
- `section_version >= 1`.
- `restore_order`로 정렬, 같은 order는 `section_id` lexical order로 deterministic tie-break.
- `required == true` section이 save file에 없으면 load 실패.
- unknown section은 기본적으로 무시하거나 report한다. MVP에서는 `ignored_sections`로 보고하고 실패하지 않는다.
- subtree/group discovery는 host가 원할 때 호출하는 보조 기능이며, 전체 SceneTree group 자동 검색을 기본 동작으로
  삼지 않는다. 저장 참여 범위는 manager 또는 host가 명시적으로 소유한다.

### 3. Save Envelope

MVP JSON envelope 초안:

```json
{
  "save_version": 1,
  "slot_id": "slot_1",
  "created_at_unix": 0,
  "updated_at_unix": 0,
  "metadata": {
    "display_name": "",
    "play_time_seconds": 0
  },
  "sections": {
    "world_state": {
      "section_version": 1,
      "payload": {}
    }
  }
}
```

정책:

- SaveGame core의 `save_version`은 section version과 별도다.
- `section_version`은 `SaveSection` adapter contract version이다.
- `payload.schema_version`이 있다면 그것은 payload/domain이 소유한다. 예: WorldState snapshot schema version.
- manager는 `save_version`을 해석하고 지원하지 않는 envelope version이면 load를 실패시킨다.
- manager는 saved `section_version`과 현재 section의 `section_version`이 다르면 기본적으로 해당 required section
  load를 실패시킨다(`section_version_mismatch`). migration registry는 MVP 밖이다.
- section별 payload는 core가 해석하지 않는다. payload 내부 version mismatch는 section `validate_save()`가 판단한다.
- JSON 호환 Dictionary만 허용한다.
- unknown saved section은 `ignored_sections`로 보고하고 restore하지 않는다.

### 4. Slot Store

초기 저장 위치 후보:

```text
user://saves/<slot_id>.json
user://saves/<slot_id>.json.tmp
user://saves/<slot_id>.json.bak
```

API 초안:

```gdscript
SaveGame.save_slot(slot_id: StringName) -> Dictionary
SaveGame.load_slot(slot_id: StringName) -> Dictionary
SaveGame.list_slots() -> Array[Dictionary]
SaveGame.delete_slot(slot_id: StringName) -> Dictionary
SaveGame.has_slot(slot_id: StringName) -> bool
```

보고형 반환을 사용한다. 예:

```gdscript
{
  "ok": true,
  "slot_id": &"slot_1",
  "error": &"",
  "sections": {...}
}
```

### 5. Load Transaction

추천 정책은 전체 validate 후 restore다.

```text
read file
  -> parse JSON
  -> validate envelope
  -> discover current sections
  -> validate all required section payloads
  -> if all valid: restore sections in deterministic order
  -> if any validation fails: no restore
```

MVP에서는 restore 자체를 완전 rollback하지 않는다. 대신 restore 전에 가능한 실패를 모두 검출하도록
section `validate_save()`를 강제한다. restore 중간 실패가 발생하면 즉시 중단하고 `partial_restore` report에
이미 restore된 section id와 실패 section id/reason을 남긴다. 이미 restore된 section을 manager가 되돌리지는 않는다.
`WorldStateSaveSection`은 `WorldStateRuntime.restore_world_state()`의 transactional restore를 사용하므로 자체 보존성이 있다.

### 6. WorldStateSaveSection

adapter 위치 후보:

```text
addons/world_core/save_game_world_state/world_state_save_section.gd
```

역할:

- `section_id = &"world_state"`
- `section_version = 1`
- `restore_order`는 다른 gameplay section보다 이른 순서가 기본값.
- `capture_save()`는 Store ready와 session ready를 먼저 확인한다. 준비되지 않았으면 실패 report를 반환하고,
  빈 payload를 envelope에 넣지 않는다. manager는 section capture 실패 시 save 전체를 쓰지 않는다.
- 준비된 경우에만 `WorldStateRuntime.capture_world_state()`를 호출한다.
- `validate_save(payload)`는 `WorldStateRuntime.peek_world_state_compatibility(payload)`를 사용한다.
  이 Runtime adapter는 Store의 `peek_snapshot_compatibility()`를 감싸는 비파괴 검증 API이며 SaveGame을 알지 않는다.
- `restore_save(payload)`는 `WorldStateRuntime.restore_world_state(payload)`를 호출한다.

WorldStateRuntime은 SaveGame을 모른다.

### 7. Manager Guards

- `SaveGameManager`는 save/load 실행 중 재진입을 거부한다. MVP 정책은 두 번째 요청을 queue하지 않고
  `busy` 실패 report로 반환한다.
- `slot_id`는 파일 IO Step에서 `^[a-zA-Z0-9_-]{1,64}$`만 허용한다.

## Open Decisions

1. **Package migration timing**
   - A. SG-001 Step 1 전에 `addons/world_core/` umbrella 이동을 먼저 한다.
   - B. SaveGame core 설계/테스트를 먼저 만들고, 패키징 이동은 별도 Task로 한다.
   - 권장: **B**. SaveGame 설계와 대규모 path migration을 같은 Step에 섞지 않는다.

2. **SaveGame autoload ownership**
   - A. `SaveGame` manager를 host가 수동 autoload 등록.
   - B. plugin이 자동 등록.
   - 권장: **A**. runtime singleton 순서와 game-specific section 배치를 host가 명시하게 한다.

3. **Section discovery scope**
   - A. 전체 SceneTree group 검색을 기본으로 한다.
   - B. `register_section()` 명시 등록을 기본으로 하고 subtree/group discovery는 helper로 둔다.
   - 권장: **B**. 자동 발견은 좋지만 저장 참여 범위를 예측 가능하게 제한한다.

4. **Unknown saved section policy**
   - A. 무시하고 report.
   - B. load 실패.
   - 권장: MVP는 **A**, 이후 strict mode 옵션.

5. **Backup Step 범위**
   - A. Step 1부터 `.bak`까지 포함.
   - B. Step 1 atomic write, Step 2 `.bak`/recovery.
   - 권장: **B**.

## Proposed Steps

### Step 0: Design Review and ADR

목표:
- SaveGame core 책임, `world_core` umbrella packaging, section contract, slot/envelope/transaction 정책을
  구현 가능한 수준으로 확정한다.

작업 범위:
- 제품 코드 수정 없음.
- [[ADR-013-WorldCore-Umbrella-Packaging]] 후보 대조.
- 실제 WorldStateRuntime adapter 계약 확인.
- `SaveSection`/`SaveGameManager` API와 Step 분해 확정.

완료 조건:
- package boundary 결정이 Approved 또는 Approved after design fixes.
- Step 1 구현 범위가 path migration과 섞이지 않게 확정됨.
- Save envelope v1, section discovery, load failure policy가 미결정 없이 기록됨.

검증 방법:
- [[Design-Review-Prompt]] 기준 설계 리뷰.

결과:
- 2026-06-17 설계 리뷰 판정: **Approved after design fixes**.
- 확정 fix:
  - WorldState validation은 `WorldStateRuntime.peek_world_state_compatibility()`를 추가하는 방향으로 확정.
  - WorldState capture는 store/session 준비 전 실패해야 하며 빈 payload를 저장하지 않는다.
  - section discovery는 명시적 `register_section()` 1순위, subtree/group helper 보조로 확정.
  - `section_version`과 payload `schema_version`, manager-level `save_version` mismatch 정책을 분리했다.
  - `world_core` path migration은 두 번째 core 소비자가 등장할 때 별도 Task로 수행한다.

### Step 1: SaveGame Core In-Memory Orchestration

목표:
- 파일 IO 없이 `SaveSection` 수집, 중복 검증, capture envelope 생성, validate-all/restore-all 순서를 검증한다.

작업 범위:
- `SaveSection` base.
- `SaveGameManager` explicit registration, optional subtree helper, ordering.
- in-memory `capture_all()` / `validate_envelope()` / `restore_all(envelope)` API.
- save/load busy guard.
- fake sections로 성공/실패/중복/order 테스트.
- SaveGame core가 WorldState/DialogTool을 참조하지 않는 정적 가드 테스트.

제외 범위:
- FileAccess, slot, backup.
- WorldState adapter.
- `world_core` 경로 이동.

완료 조건:
- 중복 section id 실패.
- required missing 실패.
- validate 실패 시 restore 0회.
- restore order deterministic.
- envelope JSON 호환.
- capture 실패 시 envelope를 만들지 않음.
- core에 domain-specific preload/load/class reference 없음.

#### Step 1 구현 결과 (2026-06-17)

판정: 구현 완료 — 코드 리뷰 진행 중.

**배치 결정**: scope가 `addons/world_core` path migration을 제외하므로 in-memory core를 standalone
`addons/save_game/`에 신규 생성했다(기존 dialogtool/world_state 파일은 이동하지 않음). ADR-013은
SaveGame을 `dialogtool` 하위에 두지 않고 장기적으로 `addons/world_core/save_game/`을 목표로 하되,
migration은 두 번째 core 소비자 등장 시 별도 Task로 수행한다고 명시한다. 따라서 Step 1에서는 umbrella
root를 미리 만들지 않고 `addons/save_game/`에 두었으며, 후속 migration 시 단순 `git mv`로 옮길 수 있다.
설계 변경이 아니라 ADR이 미지정한 interim 위치를 채운 것이다.

변경 파일(전부 신규):
- `addons/save_game/save_section.gd`: `class_name SaveSection extends Node` base contract.
  `@export section_id/section_version/restore_order/required` + 보고형 `capture_save()`/
  `validate_save(payload)`/`restore_save(payload)` override 지점(기본 no-op).
- `addons/save_game/save_game_manager.gd`: `class_name SaveGameManager extends Node`.
  `register_section`/`unregister_section`/`discover_sections(root, include_groups)`(보조 subtree+group
  helper)/`get_ordered_sections`/`capture_all`/`validate_envelope`/`restore_all`. `SAVE_VERSION=1`.
- `addons/save_game/tests/sg001_step1_core_test.gd`(+`.tscn`): fake section 기반 24 시나리오.
- `addons/save_game/tests/sg001_step1_static_guard_test.gd`(+`.tscn`): core 소스 텍스트(주석 제외)에
  WorldState/DialogueTool/condition 등 금지 토큰 0건 정적 가드.

구현 내용:
- **section ownership**: 명시적 `register_section()` 1순위. `discover_sections()`는 host가 호출하는 보조
  helper로만 subtree(+선택 group) 검색. 전체 SceneTree 자동 검색은 기본 동작이 아니다.
- **검증 정책**: 빈 id(`section_id_empty`), 중복 id(`section_id_duplicate`), `section_version < 1`
  (`section_version_invalid`)을 등록 시 거부.
- **deterministic ordering**: `restore_order` 오름차순, tie는 `section_id` lexical.
- **version 분리**: manager `save_version`(envelope), section `section_version`(adapter contract),
  payload `schema_version`(domain 소유, core 미해석)을 분리. `validate_envelope`가 `save_version`
  mismatch(`save_version_mismatch`)와 required section의 `section_version` mismatch
  (`section_version_mismatch`)를 해석. 비-required mismatch는 `skipped_sections`로 report.
- **capture transaction**: 한 section이라도 `capture_save().ok==false`면 envelope를 만들지 않고
  `capture_failed`+failed section id 반환(`envelope` 키 없음).
- **load transaction**: `restore_all`은 `validate_envelope`를 먼저 통과해야 restore를 시작한다(validate
  실패 시 restore_save 0회 호출). restore 중간 실패 시 즉시 중단하고 `partial_restore` report에 이미 복원된
  section id + 실패 section id/reason을 남긴다(이미 복원된 section은 되돌리지 않음).
- **unknown saved section**: 등록되지 않은 saved section은 `ignored_sections`로 report하고 실패하지 않는다.
- **busy guard**: `capture_all`/`restore_all` 진행 중 재진입은 `busy` 실패 report(완전 실행 후 `_busy=false`
  복귀). fake section의 capture/restore 안에서 manager를 재호출해 검증.
- **JSON 호환**: capture envelope를 `JSON.stringify`→`JSON.parse_string` 왕복해 값 보존 확인.
- **domain-free**: core 2파일에 WorldState/DialogTool preload/load/class_name 0건(정적 가드 통과).
  WorldState adapter(`WorldStateSaveSection`)는 Step 3 범위.

검증(Godot 4.6.3 headless):
- `--import`: 0 parse error, `SaveSection`/`SaveGameManager` 전역 클래스 등록 확인.
- `sg001_step1_core_test`: 24 시나리오(A~X) ALL PASS.
- `sg001_step1_static_guard_test`: core 2파일 × 금지 토큰 ALL PASS(코드 0건, 주석은 제외 스캔).
- DT-006 adapter 회귀 `dt006_step4_adapter_test`: ALL PASS(WorldStateRuntime 무수정 확인).

#### Step 1 코드 리뷰 대응 (2026-06-17)

1차 코드 리뷰 판정 **미완료**(P1 JSON 호환성 강제 누락). 수정 완료:

- **[P1 수정] payload JSON 호환성 강제**: `SaveGameManager._is_json_compatible(value)` 재귀 검증 추가.
  허용 = null/bool/int(JSON-safe ±(2^53-1))/float/String, Array(원소 호환), Dictionary(String key + 값 호환).
  StringName/Vector*/Object/Resource/Node와 non-String Dictionary key는 거부한다. `capture_all()`은 capture된
  payload가 비호환이면 `payload_not_json_compatible`+section id 반환(envelope 미생성). `validate_envelope()`도
  payload를 section `validate_save()`에 넘기기 전에 호환성을 검사한다(required=error, 비-required=skip). 테스트
  S(StringName/non-String key/중첩 Array StringName), T(int overflow), U(유효 중첩 JSON 통과+JSON 왕복),
  V(envelope 비호환 payload required 실패+plan 0) 추가.
- **[P2 수정] 등록 후 식별자 변경 재검증**: `SaveGameManager._revalidate_sections()` 추가. `section_id`/
  `section_version`이 export var라 등록 후 바뀔 수 있으므로, `capture_all()`/`validate_envelope()`가 op 전에
  live section들의 현재 필드(빈 id, live 중복 id, invalid version, freed 인스턴스)를 재검증한다. 깨졌으면
  `sections_invalid`+errors로 거부해 envelope 키 덮어쓰기를 막는다. 테스트 W(등록 후 id 중복→capture/validate
  거부), X(등록 후 빈 id→capture 거부) 추가.

재검증: `sg001_step1_core_test` 24/24(A~X) ALL PASS, static guard ALL PASS, `--import` 0 error,
DT-006 adapter 회귀 ALL PASS.

#### Step 1 2차 코드 리뷰 결과 (2026-06-17)

판정: **미완료** — P1 수정 필요.

- **[P1] 등록 후 `section_id`를 다른 고유 값으로 바꾸면 restore 경로가 깨질 수 있음.**
  `_revalidate_sections()`가 빈 id, live 중복 id, invalid version은 잡지만 등록 당시 `_sections` key와
  현재 `section.section_id`가 달라진 경우를 실패로 보지 않는다. `validate_envelope()`의 plan에는 live id가
  들어가고, `restore_all()`은 그 live id로 `_sections[id]`를 조회하므로 lookup miss/SCRIPT ERROR 위험이 있다.
  권장 수정: `_revalidate_sections()`에서 `StringName(id_key) != live_id`를 `section_id_changed`로 거부하거나,
  등록 record를 명시 구조로 두고 id를 고정한다. 테스트는 “등록 후 id를 다른 고유 id로 변경 →
  capture/validate/restore 거부” 케이스를 추가한다.
- 확인된 수정: payload JSON 호환성 강제는 닫힘. 등록 후 duplicate/empty id 재검증도 닫힘.
- 문서 정리: Step 1 완료 판정은 P1 수정 후 재검증 완료 시 갱신한다.

#### Step 1 2차 리뷰 P1 대응 (2026-06-17)

수정 완료:

- **[P1 수정] 등록 후 고유 rename 거부**: `_revalidate_sections()`에서 `StringName(id_key) != live_id`를
  `section_id_changed`로 거부하도록 추가했다. `_sections`는 등록 당시 id로 keyed인데 plan/restore는 live id로
  `_sections[id]`를 조회하므로, 등록 후 id가 빈/중복뿐 아니라 다른 고유 값으로 바뀌어도 lookup miss/null
  접근(SCRIPT ERROR) 전에 거부한다. 등록 key는 항상 고유하므로 live 중복은 곧 id 변경의 결과이고
  `section_id_changed` 한 검사가 이를 포함한다(이전 unreachable한 `section_id_duplicate` 분기 제거, freed/
  empty/changed/version_invalid만 검출). capture/validate는 `sections_invalid`, restore는 validate 단계에서
  걸려 plan loop 미진입(restore 0회). 테스트 Y(등록 후 고유 rename → capture/validate/restore 거부 +
  SCRIPT ERROR 0) 추가.

재검증: `sg001_step1_core_test` 25/25(A~Y) ALL PASS(SCRIPT ERROR 0), static guard ALL PASS,
`--import` 0 error, DT-006 adapter 회귀 ALL PASS. 판정 갱신 제안: **수정 후 완료**.

남은 위험/공백:
- 파일 IO/slot/atomic write/list/delete는 Step 2 범위(미구현).
- 실제 WorldState SAVE snapshot 왕복은 Step 3(`WorldStateSaveSection` + `peek_world_state_compatibility`).
- `world_core` umbrella migration 미수행(ADR-013 trigger 미충족).
- `discover_sections`의 group 경로는 `is_inside_tree()` 의존이라 트리 밖 호출 시 group은 건너뛴다(subtree는
  동작). 현재 테스트는 subtree 경로 위주로 검증.

### Step 2: File Slot Store MVP

목표:
- `user://saves/<slot>.json`에 envelope를 저장/로드/list/delete한다.

작업 범위:
- slot id validation(`^[a-zA-Z0-9_-]{1,64}$`).
- JSON parse/stringify.
- atomic write via tmp + replace.
- `list_slots()` metadata read.
- corrupt/missing file failure report.
- Windows atomic replace 검증.
- per-slot corrupt isolation 검증.

제외 범위:
- backup recovery.
- autosave/quicksave UI.
- compression/encryption.

#### Step 2 구현 결과 (2026-06-17)

판정: 구현 완료 — 리뷰 대기.

변경 파일:
- `addons/save_game/save_game_manager.gd`: 파일 slot store API 추가(`save_slot`/`load_slot`/`list_slots`/
  `delete_slot`/`has_slot`) + 파일 helpers(`_slot_path`/`_tmp_path`/`_check_slot_id`/`_ensure_saves_dir`/
  `_atomic_write_json`/`_read_envelope`). `SAVES_DIR="user://saves"`, `SLOT_ID_PATTERN`, `_slot_id_regex` 추가.
  `validate_envelope`의 `save_version` 검사를 JSON round-trip 대응으로 완화(아래).
- `addons/save_game/tests/sg001_step2_slot_store_test.gd`(+`.tscn`): 11 시나리오(A~K).

구현 내용:
- **slot_id validation**: `^[a-zA-Z0-9_-]{1,64}$`(RegEx 지연 컴파일). 빈 값/공백/`/`/`.`/65자 거부
  → `invalid_slot_id`(capture·파일 IO 전에 차단).
- **save_slot**: `capture_all()`(검증·busy guard·JSON 호환 강제 포함)로 envelope를 만든 뒤 slot 메타
  (`slot_id`/`created_at_unix`/`updated_at_unix`/`metadata`)를 더해 atomic write. capture 실패 시 파일 미작성
  (`capture_failed`). metadata 비-JSON이면 `metadata_not_json_compatible`. 기존 slot의 `created_at_unix`는
  보존하고 `updated_at_unix`만 갱신.
- **atomic write**: `<slot>.json.tmp`에 `JSON.stringify(envelope, "\t")`로 쓴 뒤 `DirAccess.rename`으로
  `<slot>.json` 교체(Godot rename은 기존 대상 제거 후 교체 — Windows 포함, 실측 통과). partial write가 실제
  slot을 덮지 않는다. rename 실패 시 tmp 정리.
- **load_slot**: 파일 읽기→`JSON.parse_string`→`restore_all(envelope)`. 누락=`slot_not_found`,
  파싱 실패=`parse_error`, 비-Dictionary JSON=`corrupt`(크래시 없이 보고형 실패, 게임 상태 불변).
- **list_slots**: saves 디렉터리의 `.json`만(정렬) 메타 읽기. 손상 slot은 `ok:false`로 보고하되 다른 slot
  나열을 막지 않는다(per-slot corrupt isolation). `.json.tmp`/`.bak`은 제외.
- **delete_slot/has_slot**: 파일 삭제(없으면 `slot_not_found`)/존재 확인(잘못된 id는 false).
- **JSON number 처리(중요)**: Godot `JSON.parse_string`은 모든 number를 float로 읽는다(`7`→`7.0`). JSON은
  int/float를 구분하지 않으므로 core는 JSON number를 그대로 돌려준다(추측 정규화 금지 — int/float 어느 쪽으로도
  손실). int 의미가 필요한 section은 자기 `restore_save`에서 정규화한다(WorldState adapter=Step 3가 schema로
  처리). 이에 맞춰 `validate_envelope`의 `save_version`을 INT/정수형 FLOAT 모두 허용하도록 완화(비정수 float만
  거부) — save_slot이 쓴 파일을 load_slot이 다시 읽을 수 있게 하는 self-consistency 수정. in-memory envelope
  (int)와 파일 round-trip(float) 양쪽 통과. Step 1 in-memory 회귀 무영향(int 계속 허용).

검증(Godot 4.6.3 headless):
- `--import`: 0 parse error.
- `sg001_step2_slot_store_test`: 11 시나리오(A~K) ALL PASS — slot_id validation, save/has_slot, save→load
  왕복, overwrite atomic replace(Windows rename-over 실측), created 보존·updated 갱신, missing/corrupt 실패,
  list_slots 메타+corrupt isolation, delete/has_slot, capture 실패 시 파일 미작성, 디스크 파일 유효 JSON.
- 회귀: `sg001_step1_core_test`(25)·`sg001_step1_static_guard_test`·DT-006 adapter ALL PASS.

#### Step 2 코드 리뷰 대응 (2026-06-17)

1차 코드 리뷰 판정 **미완료**(P1 section_version 정수형 미검증). 수정 완료:

- **[P1 수정] section_version 정수형 강제**: `_is_integral_number(value)`/`_is_number(value)` helper 추가.
  `validate_envelope`의 section block 검사에 `_is_integral_number(block["section_version"])`를 추가해 비정수
  float(`1.5`)/string(`"1"`)/null을 `malformed_section`으로 막는다(이전엔 `int()` 강제 변환으로 `1.5`가
  `1`로 통과 가능했음). `save_version` 검사도 같은 helper로 통일(정수형 FLOAT 허용, 비정수/문자열 거부).
  테스트 Z(정수형 FLOAT 1.0 통과 / 1.5·"1" → malformed_section + restore 0) 추가.
- **[P2 수정] corrupt JSON 로그 오염 제거**: `_read_envelope`를 정적 `JSON.parse_string`(실패 시 엔진
  ERROR 로그) 대신 인스턴스 `JSON.new().parse()` + error code 처리로 변경. 예상 가능한 손상 slot이 빨간
  엔진 로그를 남기지 않고 조용히 `parse_error`/`corrupt`로 보고한다. 회귀 실행에서 `Parse JSON failed` 로그 0건 확인.
- **[P2 수정] list_slots 구조 손상 격리**: `_extract_slot_meta(sid, env)` helper 추가. 파싱은 됐지만
  `save_version`이 정수형 number가 아니거나 timestamp가 number가 아니거나 `metadata`가 Dictionary가 아니면
  `ok:false, error:corrupt`로 격리한다(이전 `as Dictionary` 캐스팅/필드 접근 위험 제거). 테스트 L(파싱되지만
  metadata가 String이고 save_version 비정수인 slot → list에서 corrupt 격리 + 정상 slot은 계속 나열) 추가.

재검증: `sg001_step2_slot_store_test` 12/12(A~L) ALL PASS, `sg001_step1_core_test` 26/26(A~Z) ALL PASS,
static guard ALL PASS, DT-006 adapter ALL PASS, `--import` 0 parse error, corrupt 시나리오 엔진 ERROR 로그
0건. 판정 갱신 제안: **수정 후 완료**.

남은 위험/공백:
- `DirAccess.rename` overwrite는 진정한 원자성이 아니라 "기존 제거 후 rename"이다(이 사이 크래시면 slot 소실).
  완화책 backup(.bak)은 Step 4 범위.
- WorldState 실제 snapshot 왕복(int/float 정규화 포함)은 Step 3에서 `WorldStateSaveSection`이 검증한다.
- 동시 접근/멀티 프로세스 잠금은 범위 밖(단일 프로세스 가정).

### Step 3: WorldStateSaveSection Integration

목표:
- `WorldStateRuntime` adapter를 SaveSection으로 붙여 실제 WorldState SAVE snapshot을 slot에 저장/복원한다.

작업 범위:
- `WorldStateSaveSection`.
- `WorldStateRuntime.peek_world_state_compatibility(snapshot) -> Dictionary`.
- WorldStateRuntime path injection or NodePath lookup.
- new game -> mutate -> save -> mutate -> load -> SAVE restore + SESSION default 검증.
- load validation 실패 시 기존 WorldState 보존.
- capture-not-ready 실패 검증(store/session not ready이면 save file 미작성, 빈 payload 미포함).
- restore 중간 실패 시 중단 + partial restore report 검증.

제외 범위:
- Dialogue SaveEffect.
- game schema migration.

#### Step 3 구현 결과 (2026-06-17)

판정: **수정 후 완료**(코드 리뷰 2026-06-17, P2 1건 수정 — 아래 "Step 3 코드 리뷰 대응").

변경 파일:
- `addons/dialogtool/world_state/world_state_runtime.gd`: `peek_world_state_compatibility(snapshot)` 추가
  (Store `peek_snapshot_compatibility`를 감싸는 얇은 비파괴 public adapter, ADR-007 D5). 기존 메서드 무수정.
- `addons/save_game_world_state/world_state_save_section.gd`(신규): `class_name WorldStateSaveSection
  extends SaveSection`.
- `addons/save_game_world_state/tests/sg001_step3_world_state_section_test.gd`(+`.tscn`): 통합 5 시나리오(A~E).

**배치 결정**: 통합 adapter를 standalone `addons/save_game_world_state/`에 두었다(core `addons/save_game/`와
WorldState `addons/dialogtool/world_state/` 어느 쪽에도 넣지 않음). ADR-013 목표
`addons/world_core/save_game_world_state/`의 interim sibling이며, migration 시 단순 `git mv`. 이로써 core는
domain-free를 유지하고 WorldState 결합은 이 adapter 디렉터리에만 격리된다(Step 1 정적 가드 계속 통과).

구현 내용:
- **WorldStateSaveSection**: `section_id=&"world_state"`, `section_version=1`, `restore_order=-100`(이른 복원).
  `WorldStateRuntime`은 class_name이 없으므로 NodePath(`world_state_runtime_path` 기본 `/root/WorldStateRuntime`)
  또는 `set_runtime()` 주입으로 해석하고 duck-type 호출만 한다(parse-safe: preload/class_name 미참조). 계약
  메서드 미충족이면 `runtime_unavailable` fail-closed.
- **capture_save**: `is_store_ready()`+`is_session_ready()` 선확인. 준비 안 됐으면 빈 payload 없이
  `store_not_ready`/`session_not_ready` 실패 → manager가 save 전체 미작성. 준비 시 `capture_world_state()`
  snapshot 반환.
- **validate_save**: `peek_world_state_compatibility(payload)`(비파괴).
- **restore_save**: `restore_world_state(payload)`(transactional). **int/float 정규화는 adapter가 하지 않는다** —
  Store `import_snapshot`의 `_coerce_wire_value`가 JSON round-trip의 정수형 float→int, int→float(FLOAT key),
  String→StringName를 schema 타입으로 복원하므로 파일 저장 왕복이 무손실이다(설계 우려였던 별도 정규화 불필요).
- WorldStateRuntime은 SaveGame을 모른다(역의존 0).

검증(Godot 4.6.3 headless):
- `--import`: 0 parse error, `WorldStateSaveSection` 전역 클래스 등록.
- `sg001_step3_world_state_section_test`: 5 시나리오(A~E) ALL PASS — (A) 실제 slot 파일 경유
  new game→mutate→save→mutate→load 후 SAVE 값/타입(INT/FLOAT/STRING_NAME) 복원 + SESSION default,
  (B) 파일 schema_version 변조 → load validation 실패 → restore 0회 + 기존 WorldState 보존,
  (C) store not-ready capture 실패 → 파일 미작성, (D) store ready·session not-ready capture 실패 → 파일 미작성,
  (E) 후속 fake section restore 실패 → `partial_restore`(world_state는 복원 후 중단, failed_section 보고).
- 회귀: `sg001_step1_core_test`(26)·`sg001_step1_static_guard_test`·`sg001_step2_slot_store_test`(12)·
  DT-006 step3 lifecycle·step4 adapter ALL PASS, SCRIPT ERROR 0(테스트 라벨 텍스트 매칭 제외).

#### Step 3 코드 리뷰 대응 (2026-06-17)

1차 코드 리뷰 판정 **수정 후 완료**(P0/P1 없음, P2 1건). 수정 완료:

- **[P2 수정] duck-type runtime 반환 shape 방어**: `_resolve_runtime`은 메서드 존재만 확인하므로, 같은 이름의
  메서드를 갖되 다른 타입을 반환하는 Node가 주입되면 `var x: Dictionary = rt.capture_world_state()` 같은 typed
  대입에서 SCRIPT ERROR가 날 수 있었다. capture/validate/restore가 반환을 `Variant`로 받아 `is Dictionary`를
  확인하고, 아니면 `runtime_contract_invalid` 실패 report를 반환하도록 수정. ready 검사도 `== true`로만 통과시켜
  비-bool 반환을 fail-closed. reason 변환은 `_to_reason(StringName(str(...)))`로 임의 타입에도 안전.
  테스트 F(계약 메서드는 갖지만 String/int를 반환하는 `BadRuntime` → capture/validate/restore 모두
  `runtime_contract_invalid`, SCRIPT ERROR 0) 추가.

재검증: `sg001_step3_world_state_section_test` 6/6(A~F) ALL PASS, Step 1(26)·Step 2(12)·static guard·
DT-006 step3/step4 회귀 ALL PASS, `--import` 0 parse error, SCRIPT ERROR 0. 판정 갱신 제안: **수정 후 완료**.

남은 위험/공백:
- restore 중간 실패 시 이미 복원된 section(예: world_state)은 manager가 되돌리지 않는다(MVP validate-first 한계).
  WorldState 자체는 transactional이라 자기 내부는 보존되지만, 다중 section 전역 rollback은 범위 밖.
- backup/recovery(.bak)는 Step 4. 패키징/User Guide 문서 + completion review는 Step 5.
- `world_core` migration 미수행(ADR-013 trigger 미충족).

### Step 4: Backup and Recovery Policy

목표:
- save overwrite 중 손상 위험을 줄인다.

작업 범위:
- `.bak` 생성 정책.
- primary corrupt + backup valid recovery report.
- backup corrupt failure.

#### Step 4 구현 결과 (2026-06-17)

판정: 구현 완료 — 리뷰 대기.

변경 파일:
- `addons/save_game/save_game_manager.gd`: `_bak_path` 추가, `_atomic_write_json`에 백업 회전 추가,
  `load_slot`에 bak 복구 경로 추가, `delete_slot`이 primary+bak 모두 제거.
- `addons/save_game/tests/sg001_step4_backup_test.gd`(+`.tscn`): 6 시나리오(A~F).

구현 내용:
- **백업 회전(한 세대)**: `_atomic_write_json` 순서 = (1) tmp write → (2) 기존 primary가 있으면
  `primary → <slot>.json.bak` rename(기존 bak 교체) → (3) `tmp → primary` rename. 첫 save에는 기존 primary가
  없어 bak이 생기지 않는다. (2)와 (3) 사이 크래시에도 bak에 직전 good 상태가 남는다. 백업 rename 실패는
  안전망 없는 덮어쓰기를 막기 위해 `backup_failed`로 save를 실패시키고 primary를 보존한다.
- **load 복구**: `load_slot`은 primary가 없거나 손상이면 `<slot>.json.bak`을 시도한다. bak이 유효하면 거기서
  복원하고 `recovered_from_backup=true`, `source=&"backup"`로 보고한다. primary+bak 둘 다 없으면
  `slot_not_found`, 둘 다 손상이면 실패(`parse_error`/`corrupt`, restore 0회, 게임 상태 불변).
- **delete**: primary와 bak을 모두 제거한다(백업으로 slot이 되살아나지 않게). 둘 중 하나라도 있으면 삭제,
  둘 다 없으면 `slot_not_found`.
- **list_slots/has_slot 불변**: list_slots는 `.json`만 나열하고(.bak 제외), has_slot은 primary 기준 그대로다
  (bak은 복구 artifact이지 나열 대상이 아님 — 알려진 한계).

검증(Godot 4.6.3 headless):
- `--import`: 0 parse error.
- `sg001_step4_backup_test`: 6 시나리오(A~F) ALL PASS — overwrite 시 bak 생성+직전 내용 보존, 첫 save bak
  없음, primary 손상→bak 복구, primary 없음→bak 복구(크래시 시뮬), primary+bak 둘 다 손상→실패(restore 0),
  delete가 primary+bak 모두 제거.
- 회귀: Step 1(26)·static guard·Step 2(12)·Step 3(6)·DT-006 step3/step4 ALL PASS(백업 회전이 기존 save/load/
  overwrite 경로 무회귀).

#### Step 4 코드 리뷰 대응 (2026-06-17)

1차 코드 리뷰 판정 **수정 후 완료**(P1 1건 + P2 1건). 수정 완료:

- **[P1 수정] 손상 primary가 good `.bak`을 덮는 데이터 손실 방지**: `_atomic_write_json`의 백업 회전을
  primary가 **유효할 때만**(`_read_envelope(primary).ok`) 수행하도록 변경. primary가 이미 손상된 상태에서 다시
  save하면 손상 primary를 `.bak`으로 회전하지 않고 기존 `.bak`(마지막 good)을 보존하며, `tmp → primary` rename이
  손상 primary를 새 내용으로 교체한다. 이로써 "primary 손상 + bak good"에서 save 후에도 마지막 good backup이
  유지되고, 직후 크래시에도 복구 가능하다. 테스트 G(손상 primary로 save → bak 여전히 good(v1) + 새 primary v3,
  재손상 시 bak에서 v1 복구) 추가.
- **[P2 수정] 손상 bak의 실제 원인 보고**: `load_slot`에서 bak read가 실패하면 `read = bread`로 마지막 실패
  원인을 보존하도록 변경. primary 없음 + bak 손상에서 내부 기본값 `read_failed` 대신 `parse_error`/`corrupt`를
  보고한다(Step 2/4 보고형 계약 일치). 테스트 H(primary 없음 + 손상 bak → `parse_error`, restore 0) 추가.

재검증: `sg001_step4_backup_test` 8/8(A~H) ALL PASS, Step 1(26)·Step 2(12)·Step 3(6)·static guard·
DT-006 step3/step4 회귀 ALL PASS, `--import` 0 parse error. 판정 갱신 제안: **수정 후 완료**.

남은 위험/공백:
- 백업은 한 세대만 유지(직전 good). 다세대 history/롤링 백업은 범위 밖.
- 진정한 원자성은 여전히 OS rename에 의존(bak이 그 사이 손상 위험을 완화). 멀티 프로세스 잠금은 범위 밖.
- 패키징/User Guide 문서 + completion review는 Step 5.

### Step 5: Packaging and Documentation Completion

목표:
- SaveGame 사용법과 패키징 경계를 문서화하고 completion review를 받는다.

작업 범위:
- `SaveGame-System`/User Guide 작성.
- `Open-Tasks` 후속 정리.
- `world_core` umbrella migration이 별도 Task라면 명시.

#### Step 5 구현 결과 (2026-06-17)

판정: **완료**(문서 + completion review, [[SG-001-SaveGame-Core-Section-System-Review]]). SG-001 전체 완료.

작성/갱신 문서:
- `LLM_WIKI/20_Systems/SaveGame-User-Guide.md`(신규): SaveSection 작성, manager 등록/in-memory API,
  파일 slot, 백업/복구, WorldStateSaveSection, 호스트 autoload 설치, JSON number 주의, reason 표, 한계.
- `addons/save_game/README.md`(신규): core domain-free 경계, 폴더 구조(core + 통합 adapter, interim 위치),
  호스트 설치(autoload `SaveGame` 수동 등록), 사용 요약, 헤드리스 테스트 목록.
- `LLM_WIKI/50_Reviews/SG-001-SaveGame-Core-Section-System-Review.md`(신규): Step 0~5 단계별 판정과
  완료 조건 대조, 검증 매트릭스 통합.
- `SaveGame-System`/`Current-State`/`Open-Tasks`/`Home` 갱신.

`world_core` migration 명시: ADR-013 migration trigger(두 번째 core 소비자 / 외부 독립 배포 / 설치 문서
오해)가 충족될 때 별도 Task로 수행한다. 현재 interim 위치는 core=`addons/save_game/`,
통합 adapter=`addons/save_game_world_state/`이며, `git mv`로 `addons/world_core/` 하위로 이동 가능하다.

검증: 문서 전용 변경(제품 코드 무변경). 전체 회귀 재실행 — `sg001_step1~4` + `sg001_step3_world_state` +
DT-006 step3/step4 ALL PASS, `--import` 0 parse error.

완료(2026-06-17 completion review): 단계별 회귀(`sg001_step1~4` + `sg001_step3_world_state` + DT-006
step3/step4 ALL PASS, `--import` 0 parse error) 재확인, 문서 상태 불일치(리뷰 P2) 해소.

후속(범위 밖): save slot UI, autosave/quicksave, 다세대 백업, compression/encryption, schema migration
registry, `world_core` umbrella migration.

## Verification Matrix

| 영역 | 정상 | 실패/회귀 |
| --- | --- | --- |
| Section registration | explicit register, helper discovery, order 정렬 | duplicate id, empty id, invalid version |
| Envelope | JSON-compatible capture | malformed, missing sections, save_version mismatch |
| Load transaction | validate all -> restore order | validation fail -> restore 0회 |
| Slot file | save/load/list/delete, Windows atomic replace | missing, invalid JSON, partial write, per-slot corrupt isolation |
| WorldState adapter | SAVE restore, SESSION default, capture ready check | malformed snapshot, version mismatch, unknown key, not-ready capture |
| Packaging | SaveGame core domain-free static guard | core가 WorldState/DialogTool 참조 금지 |

## Risks

- 자동 group discovery가 저장 참여 범위를 예측하기 어렵게 만들 수 있다.
- section restore 중 실패한 경우 완전 rollback은 일반적으로 어렵다. MVP는 validate-first로 위험을 줄인다.
- `world_core` path migration을 SaveGame core 구현과 섞으면 리뷰와 회귀 범위가 과도하게 커진다.
- SaveGame core가 WorldState/DialogTool을 직접 참조하면 재사용 가능한 core라는 목표가 깨진다.
- unknown section을 무시하면 오래된 save의 데이터가 일부 빠진 상태로 로드될 수 있다. report와 strict mode
  후보를 남긴다.

## Related

- [[ADR-013-WorldCore-Umbrella-Packaging]]
- [[World-State-System]]
- [[World-State-User-Guide]]
- [[DT-006-WorldState-Runtime-Integration]]
- [[DT-006-WorldState-Runtime-Review]]
- [[ADR-007-WorldState-Runtime-Lifecycle]]
- [[ADR-011-DialogueWorldState-Addon-Packaging]]
