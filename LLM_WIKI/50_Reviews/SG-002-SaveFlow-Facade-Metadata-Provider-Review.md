---
id: SG-002-Review
type: review
task: SG-002
status: complete
date: 2026-06-18
system: SaveGame
review_kind: design+completion
step: "0,1,2,3"
---

# SG-002 SaveFlow Facade and Metadata Provider — 리뷰

이 문서는 SG-002의 Step 0 설계 리뷰(아래)와 Step 1~3 완료 리뷰(맨 끝)를 함께 보존한다.

## Step 0 설계 리뷰

검토 범위: [[SG-002-SaveFlow-Facade-Metadata-Provider]], [[ADR-014-SaveFlow-Facade-And-Metadata-Provider]],
[[SaveGame-System]], [[SaveGame-User-Guide]], 실제 `addons/save_game/save_game_manager.gd`.
제품 코드/`.tscn`/`.tres`는 수정하지 않았다.

## 발견 사항

### [P2] save gate provider 오류 시 `can_save`의 `ok` 값이 명세에 없다
- 조건: gate provider가 freed/non-Object이거나 메서드 없음(`save_gate_unavailable`), 또는 반환 shape 위반
  (`save_gate_contract_invalid`)일 때 `SaveFlow.can_save()`가 반환하는 `ok`가 `true`인지 `false`인지 Task/ADR에
  명시돼 있지 않다.
- 영향: fail-open(ok:true)으로 구현되면 gate가 깨졌을 때 컷신/전투 중에도 저장이 통과해 D4 목적(UI·save 호출이
  같은 금지 정책 공유)이 무너진다. metadata provider는 fail-closed(`save_slot` 미호출)로 명확한데 gate만 모호하다.
- 위치: Task §Save Gate Provider (라인 169–190), ADR-014 §Decision.
- 권장: gate provider 오류도 **fail-closed = `ok:false`**로 명시하고, `save_manual()`은 그 경우 `save_not_allowed`가
  아니라 `save_gate_unavailable`/`save_gate_contract_invalid`를 `error`로 노출해 "정책상 금지"와 "gate 설치 오류"를
  UI가 구분하게 한다.

### [P2] `list_slots()` manager-unavailable entry의 shape가 정상 slot entry와 다르다
- 조건: 설계 기본안은 `[{ "ok": false, "error": &"manager_unavailable" }]` 단일 entry 반환. 하지만 manager의
  per-slot corrupt entry는 `{ ok:false, slot_id, error:corrupt }`로 `slot_id` 키를 포함한다
  (`save_game_manager.gd:481`).
- 영향: UI는 이미 `entry.ok==false`를 처리해야 하므로(corrupt isolation) 그 코드가 manager_unavailable도 흡수하는
  게 이상적인데, `slot_id` 키가 빠지면 별도 shape 분기를 강요받는다. naive UI가 `slots.size()`로 "저장 개수"를
  세면 manager 고장 시 "1개"로 오표시된다.
- 위치: Task §Manager Resolution (라인 118–124).
- 권장: (a) entry에 `slot_id: &""`를 포함해 per-slot ok:false 패턴과 shape 통일, (b) "단일 manager_unavailable
  entry = 리스트 전체 무효, corrupt entry = 해당 slot만 무효" 의미 차이를 User Guide에 명시. 두 번째 report API 없이
  가는 방향 자체는 기존 corrupt-isolation 패턴과 일관적이라 수용 가능(Open Question 1을 이 형태로 확정 권장).

### [P2] Step 1에서 `save_flow.gd`의 domain-free 경계를 검증하는 정적 가드가 빠져 있다
- 조건: Step 1 완료 조건은 "no product references to WorldState/DialogTool in `save_flow.gd`"이고 Verification
  Matrix에도 "domain-free SaveFlow" 항목이 있지만, Step 1 검증 목록은 "SG-001 ... static guard regression"만 적는다.
  기존 `sg001_step1_static_guard_test`는 core 2파일(`save_section.gd`/`save_game_manager.gd`)만 스캔한다
  (`SaveGame-System.md:139`).
- 영향: `save_flow.gd`의 경계 위반을 자동 검증하는 테스트가 계획에 없어 완료 조건이 검증 수단 없이 선언만 된다.
- 권장: 기존 정적 가드 대상에 `save_flow.gd`를 추가하거나 SG-002 전용 정적 가드를 Step 1 산출물에 명시.

### [P3] 실패 report shape가 경로마다 다르다
- `save_not_allowed` report는 `{ ok, slot_id, error, gate }`만 갖고 `metadata`/`manager_report`가 없는데, 일반
  실패 report는 `metadata`/`manager_report`(호출됐다면)를 포함한다. UI가 단일 shape를 기대하면 분기가 늘어난다.
- 권장: 모든 `save_manual` 실패 report에 `metadata`(없으면 `{}`)와 `manager_report`(없으면 `{}`/`null`) 키를 항상
  채워 균일화.

### [P3] gate provider 메서드명 `can_save`가 facade의 `can_save`와 충돌
- provider 계약 메서드와 `SaveFlow` public 메서드가 같은 이름(`can_save`)이라 구현자가 provider를 SaveFlow류로
  오해할 여지가 있다. metadata provider는 `make_save_metadata`로 잘 구분돼 있다.
- 권장: provider 메서드를 `query_save_gate(slot_id)` 같은 별도 이름으로 두어 duck-type 계약을 분명히.

### [P3] manager 해석 시점/캐싱과 `can_save`의 manager 비의존성 미명시
- manager를 호출마다 lazy resolve하는지 캐시하는지 명시가 없다. freed/재생성 안전성을 위해 **호출마다 resolve +
  `is_instance_valid`/`is SaveGameManager` 재확인** 권장을 적어두면 좋다.
- `can_save()`는 gate provider만 보고 manager 가용성은 보지 않으므로, "UI에서 `can_save()`가 ok:true여도
  `save_manual()`이 `manager_unavailable`로 실패할 수 있다"는 점을 User Guide에 명시.

## Open Decisions (Task의 Open Questions 판정)

- list_slots 실패 형태: 단일 실패 entry 방식을 **채택하되 P2 권고대로 `slot_id` 포함 + 의미 문서화** 조건으로 확정.
  별도 `list_slots_report()` 불필요.
- metadata merge depth: MVP는 **shallow merge로 충분**. nested override는 YAGNI — 실제 nested metadata 요구가
  생길 때 deep merge를 후속 Task로. 현 결정 유지 승인.
- provider/gate를 Object duck-type vs base class: **duck-type 유지**가 옳다. `WorldStateSaveSection`의 runtime
  duck-type(`runtime_unavailable`/`runtime_contract_invalid`, ready는 `== true`로만 통과)과 동일 철학이며 base
  class 강제는 host 결합을 늘린다. 단 P2-gate 건처럼 fail 방향(closed)을 계약에 못박을 것.

## Step Assessment

- Step 1 (SaveFlow Core): 분해 적절. manager resolution / provider / gate / merge / passthrough / boundary를 모두
  완료 조건에 담았다. 단 P2(정적 가드 누락), P2(gate fail 방향) 보강 필요.
- Step 2 (WorldState 통합 usage test): 적절. store/session not-ready 실패의 원본 manager report 전달, backup
  recovery의 `recovered_from_backup`/`source` 보존 명시가 실제 manager 동작(`save_game_manager.gd:448-457`)과
  일치. 통합 테스트만 추가하고 새 product 결합을 안 만드는 범위 한정도 좋다.
- Step 3 (문서/완료 판정): 적절. UI가 소비할 raw report·metadata key 문서화 목표가 facade의 "UI 미제공" 결정과 정합.
- 전반적으로 한 Step = 하나의 경계/흐름 원칙을 지키고 구조 변경과 기능 추가를 섞지 않았다.

## Verification Assessment

- Verification Matrix가 6개 영역(manager resolution / metadata / save gate / save flow / load·list·delete /
  boundaries)으로 정상·실패 경로를 모두 덮어 충분하다.
- 다음 케이스 추가 명시 권장:
  - gate unavailable/contract_invalid 시 save 차단(fail-closed) 검증 — 현재 "deny/invalid report/non-bool ok"는
    있으나 그때 save_slot 미호출까지 확인하는지 불명.
  - `save_flow.gd` domain-free 정적 가드 통과(P2).
  - `can_save()` 단독 호출이 manager 가용성과 무관함을 보이는 케이스.
- 회귀 대상(SG-001 Step1/2/3/4, DT-006 step3/4)과 headless import 계획은 적절.

## 판정

**Approved after design fixes (설계 수정 후 승인)**

설계 방향은 견고하다. D5(manager report 비은닉, 실제 `manager_report` 보존), duck-type provider의 fail-closed
패턴, domain-free 경계, 보고형 Dictionary 계약이 모두 기존 SaveGame/WorldState 관행과 일관된다. `SaveFlow`가
`SaveGameManager` 책임을 중복하거나 숨기지 않으며, metadata provider+caller override·save gate provider 모두
구현 가능하고 과하지 않다.

Step 1 착수 전 P2 3건을 Task/ADR에 반영하면 된다(P0/P1 없음):

1. gate provider 오류 시 `can_save` fail-closed(`ok:false`) 및 `save_manual`의 해당 error 노출 명문화.
2. `list_slots()` manager-unavailable entry에 `slot_id` 포함 + 의미 차이 문서화.
3. Step 1 산출물에 `save_flow.gd` domain-free 정적 가드 추가.

P3(report shape 균일화, gate 메서드명, manager 해석 시점/`can_save` 비의존성 문서화)는 Step 1 구현 중 흡수하거나
후속 정리 가능.

## Step 1~3 완료 리뷰 (2026-06-18)

검토 범위: 실제 `addons/save_game/save_flow.gd`, `sg002_step1_save_flow_test`(+static guard),
`sg002_step2_save_flow_world_state_test`, 갱신된 [[SaveGame-User-Guide]]/`addons/save_game/README.md`/
[[SaveGame-System]]. 구현 보고만 신뢰하지 않고 manager resolution/provider fail-closed/report shape/
manager passthrough/backup recovery 경로를 실제 코드와 헤드리스 실행으로 추적했다.

### Step 1 (SaveFlow Core) — 완료 조건 대비

- provider metadata + caller override **shallow merge**: 코드 `_build_metadata`가 provider base 먼저 복사 후
  caller override. test C(merge)/B(provider only)/A(caller only)로 검증. ✅
- metadata provider missing/bad return **fail-closed**(`save_slot` 미호출): test D/E + non-Object D2. ✅
- save gate allow/deny/unavailable/contract invalid, gate 오류 fail-closed + `save_slot` 미호출:
  test F~J + non-Object I2. `can_save()`가 gate만 보고 manager는 안 보는 점도 test T로 확인. ✅
- manager unavailable report(일반 `{ok:false, error:&"manager_unavailable"}`) + `list_slots()` 단일 실패 entry
  `{ok:false, slot_id:&"", error:&"manager_unavailable"}` + `has_slot()` false: test K/O. lazy resolve +
  freed 폴백은 test L(wrong type)/M(freed)/N(path). ✅
- manager report passthrough(원본 error 노출 + `manager_report` 보존): test P(성공)/Q(invalid_slot_id). ✅
- `save_manual()` 6키 shape 균일화(미호출 단계 `{}`): test S + 각 실패 경로의 `_check_report_keys`. ✅
- `save_flow.gd` domain-free 정적 가드: 전용 `sg002_step1_static_guard_test` 12토큰 0건. ✅
- **Step 1 코드 리뷰 [P2]**(provider/gate non-Object 경계가 `Object` 타입 인자 때문에 fail-closed 전에 타입
  오류 가능) **수정 확인**: setter/저장 변수/`_provider_usable`을 Variant 경계로 열고 검사 순서를
  `null → typeof != TYPE_OBJECT → is_instance_valid → has_method`로 재정렬. 회귀 D2/I2(String/int/Array)에서
  SCRIPT ERROR 없이 unavailable 정규화 확인. P0/P1 없음.

### Step 2 (WorldState Integration Usage Test) — 완료 조건 대비 (제품 코드 변경 없음)

- store/session ready일 때 `save_manual` 성공 + `load_manual` SAVE snapshot 파일 왕복(타입 보존/SESSION
  default/metadata merge/manager report passthrough): test A. ✅
- store/session not-ready capture 실패가 **원본 manager report로 전달**(`error=capture_failed` +
  `manager_report.capture.section_reason=store_not_ready`/`session_not_ready`, 파일 미작성): test B/C. ✅
- backup recovery의 `recovered_from_backup`/`source`/`restore`가 `load_manual`에서 손실 없이 전달되고 bak 값으로
  실제 복원: test D. ✅
- 도메인 결합 테스트를 domain-free `addons/save_game/tests/`가 아닌 `addons/save_game_world_state/tests/`에 둔
  경계 판단 적절. ✅

### Step 3 (문서/완료) — 완료 조건 대비

- UI가 소비할 raw report와 권장 metadata key 문서화: User Guide §8(raw report 소비 가이드 + `list_slots()`
  의미 차이 + 권장 key), README "SaveFlow facade" 절. ✅
- SaveFlow 보고형 reason을 User Guide §10 표에 추가. ✅
- [[SaveGame-System]]에 SaveFlow 현재 사실 + Step 2 검증 반영. ✅
- 설치 문서에 SaveFlow autoload/주입(이름≠class_name) 주의 추가. ✅

### 검증 결과

- `--import` 0 parse 에러, `SaveFlow` class 등록.
- 전체 매트릭스 **10/10 GREEN**(실제 `SCRIPT ERROR:` 0):
  SG-002 step1(save_flow A~T, static guard)·step2(integration A~D), SG-001 step1(core, static guard)/step2(slot)/
  step3(world_state)/step4(backup), DT-006 step3(lifecycle)/step4(adapter).
- 잘 된 부분: SaveFlow가 manager 책임을 재구현·은닉하지 않고 위임만 한다. provider duck-type fail-closed가
  `WorldStateSaveSection`의 `runtime_unavailable`/`runtime_contract_invalid` 철학과 일관된다. report 계약이
  보고형 Dictionary 코드베이스 관행과 맞는다.
- 검증하지 못한 항목: 실제 `/root/SaveGame`/`/root/WorldStateRuntime` autoload 부팅 조합(주입 구성으로 대체
  검증, autoload 설치는 호스트 책임). UI는 범위 밖(미구현).

### 판정

**완료.** Step 1~3 완료 조건을 모두 충족하고 Step 1 [P2]는 수정·재검증됐다. P0/P1 없음. 남은 항목(실제 save slot
UI, autosave/quicksave, 다세대 백업, migration registry, Dialogue SaveEffect, `world_core` 패키징 이동)은
SG-002 범위 밖 후속으로 [[Open-Tasks]]에 유지한다.

## Related

- [[SG-002-SaveFlow-Facade-Metadata-Provider]]
- [[ADR-014-SaveFlow-Facade-And-Metadata-Provider]]
- [[SG-001-SaveGame-Core-Section-System-Review]]
- [[SaveGame-System]]
- [[SaveGame-User-Guide]]
