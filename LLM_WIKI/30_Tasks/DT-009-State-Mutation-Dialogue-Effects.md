---
id: DT-009
type: task
status: done
system: DialogueTool, WorldState
created: 2026-06-15
updated: 2026-06-16
tags: [task, dialogue, world-state, mutation, effect]
---

# State Mutation Dialogue Effects

## Goal

DialogueTool의 비대기 Effect 연결에서 타입 안전한 World State 변경을 실행한다.
제작자는 선택 결과나 대사 진행 지점에 `State Set` 또는 숫자 `State Add` Effect를 연결해 퀘스트 단계,
호감도, 자원 등을 변경하고, 이후 Branch/조건부 Choice가 변경된 최종 상태를 즉시 읽을 수 있어야 한다.

## Context

- [[DT-005-StateSchema-WorldStateStore]]는 `set_state(key, value)`와 atomic
  `apply_state_batch(changes)` mutation facade를 제공한다.
- `WorldStateStore`는 strict type, writable, JSON-safe 숫자 domain, notification 재진입 정책을 이미 강제한다.
- [[DT-004-Nonblocking-Effect-Flow]]는 `kind == "effect"` 연결, 저장 순서 실행, 순환 차단과 Effect 대상
  whitelist를 제공한다. 현재 Effect 대상은 Portrait 타입뿐이다.
- [[DT-008-State-Condition-Dialogue-Integration]]은 read provider를 Manager -> UI -> Player로 주입하고,
  State Condition이 변경된 Store 값을 Branch/Choice에서 읽는 경로를 완료했다.
- 현재 Dialogue runtime에는 mutation provider가 주입되지 않으며, State mutation Effect도 없다.
- 기존 `apply_state_batch`는 절대값 변경만 지원한다. `Add`를 Player에서 read -> calculate -> set으로 구현하면
  read/mutation provider가 어긋나거나 연산 원자성이 깨질 수 있으므로 Store/provider 책임을 먼저 확정해야 한다.

## User Outcome

- 그래프에서 `State Set` Effect에 key와 타입이 보존되는 literal 값을 지정한다.
- `State Add` Effect로 INT 또는 FLOAT state에 같은 타입의 delta를 더한다.
- Start/Say/Choice의 기존 Effect 출력에 mutation Effect를 연결한다.
- Choice 선택 시 mutation이 원래 Flow로 이동하기 전에 실행되며, 다음 조건 노드는 새 값을 읽는다.
- provider 누락, 미등록/read-only key, 타입/domain 오류, busy 상태는 mutation을 적용하지 않고 관찰 가능한
  report를 남기며 대화 Flow는 정해진 실패 정책을 따른다.

## Scope

### Included

- read provider와 분리된 mutation provider 주입 경계
- `State Set` 비대기 Effect Definition/GraphNode/Editor Adapter/runtime snapshot
- INT/FLOAT 전용 `State Add` Effect와 provider 소유 원자적 연산 API
- 기존 Effect whitelist/validation/runtime dispatcher 확장
- mutation 결과 report signal seam
- strict literal 타입과 `.tres` 저장/재로드 보존
- Effect 저장 순서, 반복 실행, 대화 교체, provider 누락과 Store 오류 회귀
- mutation 이후 State Condition Branch/Choice 재평가 end-to-end

### Out of Scope

- 범용 State Read Data 노드
- Multiply/Toggle/Append/Clamp 또는 퀘스트 전용 명령
- Data 입력이나 Expression 결과를 mutation 값으로 사용하는 동적 Effect
- 여러 Effect 노드를 하나의 all-or-nothing transaction으로 묶는 그래프 문법
- schema-aware key/type picker와 inline Schema 편집
- undo/rollback, DialogueHistory, trace inspector UI
- SaveGame file/slot, autosave 트리거와 schema migration
- 네트워크 복제와 멀티스레드 mutation

## Proposed Runtime Contract

아래 계약은 **Step 0 설계 리뷰의 입력안**이며 승인 전 확정이 아니다.

### Provider Separation

```gdscript
DialogueManager.play(dialogue, read_state_provider, mutation_state_provider)
DialogueUI.play(dialogue, read_state_provider, mutation_state_provider)

DialoguePlayer.set_read_state_provider(provider)
DialoguePlayer.set_mutation_state_provider(provider)
```

- read와 mutation 권한은 분리한다. mutation provider가 생략됐다고 read provider를 자동 승격하지 않는다.
- deferred/latest-wins 시작 요청은 resource/read provider/mutation provider를 한 묶음으로 캡처한다.
- 테스트는 `DialoguePlayer.new()` 또는 Manager에 fake provider와 실제 `WorldStateStore`를 명시적으로 주입한다.
- 일반 게임 경로에서는 동일한 `WorldState` Store를 read/mutation 양쪽에 전달할 수 있다.

### Mutation Provider

기존 계약:

```gdscript
set_state(key: StringName, value: Variant) -> Error
apply_state_batch(changes: Array[Dictionary]) -> Dictionary
```

`State Add` 추가 계약 (Step 0에서 **보고형으로 확정** — 아래 Step 0 Resolutions D3/D4 참조):

```gdscript
# 입력안은 -> Error 였으나, authoritative old/new 확보를 위해 -> Dictionary 로 확정.
add_state(key: StringName, delta: Variant) -> Dictionary   # { applied, changed, old, new, error }
```

- `add_state`가 현재 값 read, strict numeric type 확인, overflow/domain 확인과 commit을 Store 내부에서 수행한다.
- INT state에는 INT delta, FLOAT state에는 FLOAT delta만 허용한다. int/float 암시 변환은 없다.
- 미등록/read-only/non-numeric/type mismatch/out-of-domain/not-ready/busy는 값 불변으로 실패한다.
- DialoguePlayer가 `read_state() + delta`를 계산한 뒤 `set_state()`하는 방식은 사용하지 않는다.

### Effect Types

```text
StateSetEffectDef
runtime type: state_set
fields: key: StringName, value: Variant

StateAddEffectDef
runtime type: state_add
fields: key: StringName, delta: Variant   # INT 또는 FLOAT만
```

- 두 노드는 Effect 입력만 갖는 leaf command다. 실행 커서를 이동하거나 wait state를 만들지 않는다.
- 기존 Start/Say/Choice Effect 출력에 연결하고 `DialogueGraphResource.EFFECT_TARGET_TYPES`에 등록한다.
- literal의 `typeof()`는 capture -> save -> cache-ignore reload -> runtime snapshot에서 보존돼야 한다.

### Execution and Failure

- 같은 실행 지점의 Effect는 `runtime_connections` 저장 순서대로 실행한다.
- 각 Effect는 독립 transaction이다. 앞 Effect 성공 뒤 뒤 Effect가 실패해도 앞 변경을 rollback하지 않는다.
- mutation은 주 Flow 이동 전에 동기적으로 완료된다. 다음 Branch/Choice는 변경된 값을 읽는다.
- mutation 실패는 해당 Effect만 fail-closed(값 불변)하고 기본 권장안은 대화 Flow 계속이다.
- provider 누락이나 계약 위반을 SCRIPT ERROR로 만들지 않고 구조화 report로 변환한다.
- `WorldStateStore`의 notification 중 mutation 거부(`ERR_BUSY`)를 우회하거나 자동 재시도하지 않는다.

### Report Seam

```gdscript
signal state_mutation_evaluated(
    effect_node_id: int,
    report: Dictionary
)
```

권장 report:

```text
{
  applied: bool,
  operation: "set" | "add",
  key: StringName,
  old_value: Variant,
  new_value: Variant,
  error: StringName
}
```

- 성공/실패 모두 실행 1회당 정확히 1회 발행한다.
- signal listener가 실제 mutation 결과나 이후 실행을 바꾸지 못하도록 detached deep copy를 발행한다.
- report의 old/new 확보 책임과 provider facade 최소 계약은 Step 0에서 확정한다.

## Open Decisions for Step 0

1. **Provider 주입 API**: 세 번째 positional 인자 사용 여부, 별도 context Resource/Dictionary 도입 여부.
   - 권장: 현재 API와 호환되는 세 번째 선택 인자. 자동 권한 승격은 금지.
2. **Add 원자성 API**: `add_state` 단일 메서드 vs 일반화된 operation batch.
   - 권장: 이번 범위에는 `add_state`만 추가하고 일반 operation DSL은 후속으로 미룬다.
3. **Effect 실패 시 Flow**: 계속 진행 vs 대화 중단/실패 Flow.
   - 권장: 기존 비대기 Effect 의미를 유지해 Flow는 진행하고 report/signal로 실패를 노출한다.
4. **report old/new 값**: mutation provider가 결과 report를 반환하도록 확장할지, Player가 read provider로 전후를
   읽을지 결정해야 한다. 서로 다른 provider 조합에서도 거짓 report가 없어야 한다.
   - 권장: provider가 authoritative mutation report를 반환하는 계약 검토.
5. **여러 Effect의 원자성**: 저장 순서의 독립 transaction으로 확정할지, source node 단위 batch로 묶을지.
   - 권장: 독립 transaction. all-or-nothing 그래프 mutation은 별도 Task.
6. **Set literal authoring**: 지원 5타입 전체(bool/int/float/String/StringName) vs 숫자/불리언 우선.
   - 권장: Store 허용 5타입 전체를 Set에서 지원하고 Add는 INT/FLOAT만 지원.
7. **read-only와 시스템 mutation**: Dialogue Effect가 gameplay mutation임을 확정하고 read-only 우회를 금지한다.
8. **Effect 체인 의미**: 앞 mutation을 뒤 mutation/다음 조건이 즉시 관찰하며, 순환은 기존 validator가 차단한다.

중요 결정은 Step 0 결과로 `ADR-010-State-Mutation-Dialogue-Effects`에 기록한다.

## Step 0 Resolutions (Design Review, 2026-06-15)

판정: **Approved after design fixes**. 실제 코드(`world_state_store.gd`, `dialogue_player.gd`,
`dialogue_manager.gd`, `dialogue_ui.gd`, `dialogue_graph_resource.gd`, `editor.gd`)와 대조해 P0가
없음을 확인하고, Open Decisions를 D1~D10으로 확정했다. 전체 결정은 [[ADR-010-State-Mutation-Dialogue-Effects]].

핵심 확정(요약):

- **D1/D2 provider 주입.** `play(resource, read_provider=null, mutation_provider=null)` 세 번째 선택
  인자 + `DialoguePlayer.set_mutation_state_provider()`(별도 `_mutation_state_provider` 필드). read의
  mutation 권한 자동 승격 금지. mutation provider 누락은 `provider_missing` report + Flow 계속.
- **D3/D4 Add 원자성 + old/new.** Store에 `add_state(key, delta) -> Dictionary`(보고형, Error 아님)를
  추가한다. authoritative old/new는 provider가 반환한다 — **Set은 기존 `apply_state_batch` 재사용**
  (changed→diff, same-value→`applied && diff 빈 것`이 old==new==value를 authoritatively 보장),
  **Add는 `add_state` report**. Player의 read provider 사후 read 금지. 소비 계약 = `{apply_state_batch, add_state}`.
- **D5/D6 실패·순서.** Effect 실패는 fail-closed(값 불변) + Flow 계속, SCRIPT ERROR 금지. 같은 노드의
  Effect는 저장 순서의 독립 transaction(앞 성공+뒤 실패 rollback 없음). mutation은 Flow 이동 전 동기 완료.
- **D7 타입.** Set = 5타입 전체, Add = INT/FLOAT strict(같은 타입 delta, 암시 변환 없음).
- **D8 오류 표.** read_only / store_not_ready / store_busy / unknown_key / type_mismatch / out_of_domain /
  provider_missing|provider_contract_invalid. 모두 구조화 report + Flow 계속, `ERR_BUSY` 우회·재시도 금지.
- **D9 lifecycle.** `_pending_start` 번들에 mutation provider 포함(resource+read+mutation 한 묶음).
  폐기 UI는 `cancel_pending_start`로 전체 취소 → 폐기 provider mutation 0회.
- **D10 report seam.** `state_mutation_evaluated(effect_node_id, report)`를 commit 후 1회 발행, authoritative
  값 capture-before-emit + `duplicate(true)`. listener 변조·재진입이 결과/Flow를 못 바꾼다.

리뷰가 식별한 **구현 시 필수 제약**:

1. (P1-2 / Design Risk 7) `_run_effects`는 현재 whitelist 통과 대상에 무조건 `_build_portrait_request`만
   발행한다([dialogue_player.gd 약 485행]). `state_set`/`state_add`를 `EFFECT_TARGET_TYPES`에만 추가하면
   state 노드에 garbage Portrait 요청이 발행된다. Step 2에서 `_run_effects`에 **타입 디스패치**를 도입하고
   whitelist·런타임 dispatch·에디터 validation 세 곳을 함께 갱신한다.
2. (P1-3 / Design Risk 3·5) 직접 mutation 경로는 Portrait의 `ui_request` source-guard를 통과하지 않으므로
   D9 번들 캡처와 D10 commit-후-발행으로 폐기/재진입을 방어한다.
3. (P2-2) Add 오버플로: INT 피연산자가 모두 `±(2^53-1)` 안이라 int64 자체 오버플로는 불가능. 결과를
   int64로 계산 후 JSON-safe 도메인만 검사하고 경계 초과는 `out_of_domain` 실패로 처리한다.

검증 매트릭스 보강(아래 Verification Matrix 외 추가 케이스):

- Store Add: `2^53-1` 경계 성공 / `+1` overflow 실패(값·signal 불변), delta=0 → applied·changed=false·무발행,
  FLOAT NAN/INF delta 거부, INT↔FLOAT delta mismatch.
- Provider: `apply_state_batch` 또는 `add_state` 누락 → `provider_contract_invalid`(둘 다 검증).
- Set 보고: same-value Set이 사후 read 없이 authoritative old==new report. read≠mutation provider 인스턴스에서 거짓 report 0건.
- Lifecycle: `state_mutation_evaluated` listener가 `play()` 재진입해도 폐기 provider mutation 0회.

status: proposed → **in-progress**(Step 0 완료, Step 1 착수 가능).

## Steps

## Step 0: Design Review and ADR

목표:
- mutation 권한, Add 원자성, Effect 실패/순서/report 계약을 구현 전에 확정한다.

작업 범위:
- 실제 Store facade, Manager/UI deferred lifecycle, DialoguePlayer Effect dispatcher, editor validation 대조
- 위 Open Decisions 확정
- `ADR-010-State-Mutation-Dialogue-Effects` 작성 및 Task 계약 갱신

제외 범위:
- 제품 코드, `.tscn`, `.tres` 수정

완료 조건:
- P0/P1 설계 문제가 없고 provider/API/report/failure/order/type 결정이 구현 가능한 수준으로 고정된다.
- 판정이 Approved 또는 Approved after design fixes다.

검증 방법:
- [[Design-Review-Prompt]] 형식의 코드 대조 리뷰

상태: **완료(2026-06-15)**. 판정 Approved after design fixes, D1~D10 확정,
[[ADR-010-State-Mutation-Dialogue-Effects]] accepted. 상세는 위 Step 0 Resolutions.

## Step 1: Atomic Add Provider Contract

목표:
- `WorldStateStore`가 Dialogue와 무관한 범용 mutation provider로서 원자적 숫자 Add를 제공한다.

작업 범위:
- Step 0에서 승인된 Add API와 facade
- strict INT/FLOAT delta, writable/domain/not-ready/busy 검사
- 실제 변경 시 기존 `value_changed` 계약 유지

제외 범위:
- Dialogue 코드와 에디터 노드
- 일반 operation DSL과 다중 연산 transaction

완료 조건:
- INT/FLOAT Add 성공, 같은 타입 강제, 오류 시 값/signal 불변.
- JSON-safe 경계, read-only, 미등록, busy와 overflow/domain 실패가 구조적으로 검증된다.
- DT-005/006 회귀가 유지된다.

검증 방법:
- Store 전용 headless unit/regression test + editor import

선행 조건: Step 0 승인

상태: **구현·리뷰 완료(2026-06-16).**

구현 결과:
- `WorldStateStore`에 보고형 원자 API `add_state(key, delta) -> Dictionary`를 추가했다
  ([world_state_store.gd:163](../../Assets/Script/gds/world_state/world_state_store.gd), 보고형 메서드 본문 163~233행).
  Dialogue/Player를 참조하지 않는 범용 mutation provider 메서드다.
- 한 transaction으로 현재 값 read → strict 타입/도메인 검증 → delta 계산 → commit을 Store 내부에서
  수행한다. 실제 변경은 기존 `_stage`/`_emit_changes` 경계를 재사용하고, 값이 바뀐 경우에만
  `value_changed`를 1회 발행한다(delta=0/같은 값은 `applied=true, changed=false`, 무발행).
- INT state는 INT delta, FLOAT state는 FLOAT delta만 허용(`typeof(delta) != builtin_type`이면
  `type_mismatch`). 비숫자 state(BOOL/String/StringName)도 `type_mismatch`로 거부.
- 덧셈 전에 **delta 자체**를 `_value_in_domain`으로 검사한다(ADR-010 전제: 두 피연산자 모두 JSON-safe).
  이로써 범위 밖 INT delta가 결과에서 상쇄돼 거짓 승인되는 경우(예: `-1 + 2^53 = 2^53-1`)와 FLOAT
  INF/NAN delta를 `out_of_domain`으로 거부한다. 두 피연산자가 도메인 안이면 int64 합은 `±(2^54-2)`로
  wrap이 불가능하므로 별도 wrap 감지는 불필요하다. 이후 결과도 `_value_in_domain`으로 검사해 경계 초과와
  FLOAT `finite+finite=inf` overflow를 `out_of_domain`으로 거부한다.
- not-ready/notification busy/unknown/read-only/type/domain 실패는 값·signal 불변.
- report old/new는 commit 전에 캡처하고, report는 호출마다 새로 만든 local Dictionary라 외부 변조가
  Store나 다음 호출에 영향을 주지 못한다(value 타입 필드).
- 오류 코드(StringName): `store_not_ready / store_busy / unknown_key / read_only / type_mismatch /
  out_of_domain`. 성공 시 `&""`. report 필드: `{applied, changed, operation:"add", key, old_value,
  new_value, error}` — ADR-010 D8/D10 계약 준수.
- 기존 `set_state`/`apply_state_batch`/snapshot/reset 동작은 변경하지 않았다.

검증:
- 신규 `dt009_step1_add_state_test`(20 시나리오 A~U) ALL PASS. 1차 리뷰 P1(범위 밖 delta 상쇄 거부,
  케이스 T)과 P2(report 필드 `typeof` 단언, 케이스 U)를 반영했다.
- 회귀 DT-005 Step1~6 + DT-006 Step1~5 ALL PASS. Godot 4.6.3 headless `--import` 0 오류.

남은 위험/이월:
- mutation provider 주입, `state_set`/`state_add` Effect Definition·런타임 dispatch·whitelist·report
  signal은 Step 2 범위다. Step 1은 Store 계약만 다룬다.

## Step 2: Runtime Mutation Provider and Effects

목표:
- 직접 구성한 runtime snapshot에서 State Set/Add Effect가 주입 mutation provider를 통해 실행된다.

작업 범위:
- Manager -> UI -> Player mutation provider 전달과 latest-wins 캡처
- Player mutation provider facade/contract validation
- `state_set`/`state_add` runtime dispatch와 Effect whitelist
- Step 0에서 확정한 mutation report signal

제외 범위:
- 에디터 GraphNode/Adapter와 저장 UX
- 동적 Data 입력 값

완료 조건:
- Set/Add 성공과 모든 provider/Store 오류가 SCRIPT ERROR 없이 승인된 실패 정책으로 처리된다.
- mutation은 Flow 이동 전에 완료되고 다음 조건 평가가 새 값을 읽는다.
- 같은 프레임 dialogue 교체 시 폐기된 Player/provider는 mutation하지 않는다.
- 기존 Portrait Effect 실행 순서와 UI 요청이 변하지 않는다.

검증 방법:
- fake provider + 실제 Store runtime snapshot headless test
- DT-004/005/008 핵심 회귀

선행 조건: Step 1 리뷰 완료

상태: **구현·리뷰 완료(2026-06-16).**

구현 결과:
- **provider 주입 경계(D1/D2/D9).** `DialogueManager.play(resource, read=null, mutation=null)`와
  `DialogueUI.play(...)`에 세 번째 선택 인자(mutation provider)를 추가하고, `DialoguePlayer`에
  `_mutation_state_provider` 별도 필드 + `set/get/has_mutation_state_provider()`를 두었다. read의
  mutation 권한 자동 승격은 없다. `DialogueUI._pending_start`를 `{resource, read_provider,
  mutation_provider}` 한 묶음으로 deferred 캡처해 latest-wins/폐기 시 provider가 분리되지 않게 했다
  ([dialogue_manager.gd](../../addons/dialogtool/RunTime/dialogue_manager.gd),
  [dialogue_ui.gd](../../addons/dialogtool/UI/dialogue_ui.gd),
  [dialogue_player.gd](../../addons/dialogtool/RunTime/dialogue_player.gd)).
- **런타임 타입 디스패치(D6, 런타임 디스패치 제약).** `_run_effects` 루프가 whitelist 통과 대상을
  타입별로 분기한다: `portrait_*`는 기존 `_build_portrait_request` UI 요청, `state_set`/`state_add`는
  `_run_state_effect`로 mutation provider 호출 + report signal. state 노드에 garbage Portrait 요청을
  더는 발행하지 않는다.
- **provider 계약 검증(D4/D8) — 호출 전 + 호출 후 2단계.** 호출 전 `_is_valid_mutation_provider`:
  (1) `typeof == TYPE_OBJECT`(freed에 `is Object`를 쓰면 SCRIPT ERROR라 typeof 사용), (2) `is_instance_valid`
  (freed 거름), (3) 두 메서드 구현, (4) reflection로 **arity + 인자 타입(+ typed array 원소 타입)** 검사
  (`_method_accepts`/`_arg_compatible`): `apply_state_batch` 1번째 인자는 untyped Array 또는 **정확히
  `Array[Dictionary]`**(typed array는 `hint == PROPERTY_HINT_ARRAY_TYPE`일 때 `hint_string == "Dictionary"`까지
  확인 — `Array[int]` 등 원소 타입 불일치 거부), `add_state`는 `StringName`/`String` key + untyped delta.
  잘못된 arity/인자 타입 호출은 SCRIPT ERROR이므로 호출 전 거른다.
  호출 후 반환 **스키마 검증**: 최상위가 `Dictionary`이고 `applied`가 bool(truthy 거짓 승인 금지),
  성공 시 Set은 `diff`가 Array + 항목에 `old`/`new`(또는 빈 diff=same-value), Add는 `old_value`/`new_value`
  존재, 실패 시 Set은 `errors[0].reason`(String→StringName 정규화)·Add는 `error`가 StringName(D10). 어느
  하나라도 위반이면 `provider_contract_invalid`. genuine null(`typeof==TYPE_NIL`)만 `provider_missing`,
  공급됐지만 못 쓰는 provider(freed/non-Object/arity/인자형/반환형/스키마)는 모두 `provider_contract_invalid`,
  Flow 계속, SCRIPT ERROR·거짓 성공 0.
- **authoritative report(D4).** Set은 신규 Store API 없이 `apply_state_batch([{key, value}])`를 재사용하고
  `diff`에서 old/new를, same-value는 `applied && diff 빈 것`으로 old==new==value를 확정한다. Add는
  `add_state` report의 old/new를 그대로 쓴다. Player의 read 사후 read 없음.
- **report seam(D10) + 재진입 방어.** `state_mutation_evaluated(effect_node_id, report)`를 실행 1회당 정확히
  1회, mutation commit 후 발행한다. signal에는 `report.duplicate(true)` deep copy를 넘긴다. **mutation
  provider는 effect chain 시작 시(`_run_effects` 진입) 한 번 고정**되어, report listener가 실행 도중
  `set_mutation_state_provider()`로 교체해도 같은 chain의 뒤 Effect가 다른 Store를 건드리지 못한다.
  report = `{applied, operation:"set"|"add", key, old_value, new_value, error}`.
- **손상 snapshot 방어.** Effect `key`가 String/StringName가 아니면 `_coerce_effect_key`가 빈 key로 좁혀
  `StringName(non-string)` 런타임 오류를 막는다(빈 key는 Store가 `unknown_key`로 fail-closed).
- **whitelist(공유).** `DialogueGraphResource.EFFECT_TARGET_TYPES`에 `state_set`/`state_add`를 추가했다
  (런타임·에디터 validation 공유). 각 Effect는 독립 transaction이라 앞 성공 뒤 실패가 앞을 rollback하지 않는다.
- **구현 중 수정한 실 버그:** `apply_state_batch`는 `Array[Dictionary]`를 요구하는데 duck-typed provider
  호출은 정적 변환이 없어 untyped literal이 SCRIPT ERROR(`Invalid type ... typed array`)를 냈다. D5(SCRIPT
  ERROR 금지) 위반이므로 `_apply_set_effect`에서 명시적 typed array를 만들어 호출한다.

검증:
- 신규 `dt009_step2_runtime_mutation_test`(22 단언군 A~U, T에 `Array[int]` 케이스 포함) ALL PASS, 엔진
  SCRIPT ERROR 0: Set 5타입/same-value,
  Add 양수·음수·0, 연속 Add 순서 + Branch가 변경값 읽음, provider_missing/contract_invalid/non-Object,
  read↔mutation 분리, Store 오류(read_only/type_mismatch/unknown_key/out_of_domain/**store_busy/store_not_ready**)
  fail-closed, 앞 성공+뒤 실패 독립 transaction, report 1회+변조 안전, Portrait+state 혼합 순서(garbage 없음),
  반복 실행, Manager same-frame latest-wins 폐기 provider mutation 0회.
  리뷰 반영 회귀: **[P]** 잘못된 provider 변형(arity/반환형/freed) → SCRIPT ERROR 없이
  `provider_contract_invalid`, **[Q]** report listener의 provider 교체가 같은 chain 뒤 Effect에 무영향(고정),
  **[R]** store_busy/store_not_ready report 경로, **[S]** 손상 key fail-closed, **[T]** provider 인자 타입
  불일치(set/add scalar + **`Array[int]` 원소 타입**) 사전 거부, **[U]** 손상된 반환 스키마(diff non-array/
  applied int/errors 손상/diff key 누락/add String error/add applied int) → `provider_contract_invalid` + 거짓 성공 없음.
- 회귀 DT-004 Step1~4(effect flow), DT-008 Step1/3/4/5(condition/choice), DT-005 Step4, DT-006 Step5 ALL PASS.
  Godot 4.6.3 headless `--import` 0 오류.

남은 위험/이월:
- 에디터 GraphNode/Definition/Adapter/registry와 typed literal `.tres` 왕복은 Step 3 범위(런타임 snapshot은
  수작업 구성으로 검증). 에디터 validation 오류 메시지("Portrait만 허용")는 state 노드 authoring과 함께 Step 3에서
  갱신한다. Choice→mutation→Branch/conditional Choice 재평가 e2e는 Step 4.

## Step 3: Editor Authoring and Resource Round-trip

목표:
- 제작자가 State Set/Add Effect를 그래프에서 생성·편집·연결·저장할 수 있다.

작업 범위:
- Definition, GraphNode, Editor Adapter, NodeTypeRegistry 등록
- key + typed literal/delta UI
- Effect 입력 포트와 validation/whitelist 통합
- external dialogue `.tres` 저장 -> cache-ignore 재로드 -> 재캡처

제외 범위:
- schema-aware picker와 inline Schema 편집
- Data 입력 기반 동적 mutation

완료 조건:
- 두 노드를 목록에서 생성해 Start/Say/Choice Effect 출력에 연결할 수 있다.
- key, operation, literal 타입/값, node id와 Effect 연결이 저장/재로드 후 보존된다.
- Add UI는 INT/FLOAT 외 타입을 만들 수 없거나 저장 validation에서 명시적으로 거부한다.
- 기존 Dialogue 리소스와 Portrait Effect round-trip이 유지된다.

검증 방법:
- 실제 `dialoguetool_main.tscn` fixture editor round-trip + headless import

선행 조건: Step 2 리뷰 완료

상태: **구현·리뷰 완료(2026-06-16).**

구현 결과:
- **Definition.** `StateEffectDef`(추상 base, `FlowDefinition` 상속, `Abstract/`에 두어 노드 목록 제외) +
  `StateSetDef`(`State/state_set_def.gd`, runtime type `state_set`, `value_type:int`+`value:Variant`) +
  `StateAddDef`(`State/state_add_def.gd`, `state_add`, `delta_type`+`delta`). Portrait처럼 비대기 leaf
  (Effect 입력만, `_is_done`/`execute` no-op).
- **strict literal(1차 리뷰 P1 반영).** capture는 선택 타입으로 텍스트를 **엄격히 파싱**한다(`coerce_text`:
  INT는 `is_valid_int`, FLOAT는 `is_valid_float`, BOOL은 `true`/`false`만). 형식이 틀리면 조용히 0/false로
  바꾸지 않고 원본 String을 보존해 typeof가 선언 타입과 어긋나게 둔다. 저장 시 `_validate_runtime_snapshot`이
  `validate_literal`로 타입 불일치를 잡아 **저장을 차단**한다. `get_runtime_params`는 값을 변환하지 않고
  그대로 넘기고(과거 `coerce_to_type` 제거 — 손상 .tres를 조용히 변환해 Store strict 검사를 우회하던 문제),
  런타임에서 불일치는 Store가 `type_mismatch`로 거부한다. 정상 literal의 typeof는 capture + `.tres`가 보존한다.
- **Editor Adapter.** 공유 `state_effect_editor_adapter`(Portrait 패턴 — 노드 상태 없이 meta로 위젯 재조회).
  key(LineEdit) + type(OptionButton) + value/delta(LineEdit) + Effect 입력 포트(주황, row 2)를 구성한다.
  노드 type으로 노출을 결정: `state_set`은 5타입, `state_add`는 **INT/FLOAT 2개뿐**(다른 타입 literal 불가).
  허용 타입 목록은 `StateEffectDef.SET_VALUE_TYPES`/`ADD_VALUE_TYPES` 단일 정의를 공유한다. 기본
  `dialogue_node.tscn` 사용(전용 GraphNode scene 불필요).
- **등록/whitelist.** `node_type_registry`에 `state_set`/`state_add` → 공유 어댑터. 노드 목록은
  `NodeDefinitions/` 자동 탐색으로 "StateSet"/"StateAdd" 노출(Abstract base 제외). `EFFECT_TARGET_TYPES`는
  Step 2에서 이미 추가됨(런타임·에디터 validation 공유). 에디터 validation 오류 메시지를 "Portrait 또는
  State Set/Add만 허용"으로 갱신.
- 기존 `set_runtime_snapshot`/`capture_current_graphedit`/`load_resource`의 Effect 연결 `kind` 파생 +
  `_find_effect_port` 정규화를 그대로 재사용한다(에디터/런타임 코드 변경 없음).

검증:
- 신규 `dt009_step3_editor_roundtrip_test`(실제 `dialoguetool_main.tscn` fixture, 시나리오 A~F) ALL PASS:
  **[E]** `DialogueNodeItemList`에 "StateSet"/"StateAdd" 실제 노출 + Abstract base 제외,
  **[A]** Effect 입력 포트 존재 + 출력 없음, StateAdd 타입 옵션 INT/FLOAT 2개·StateSet 5개,
  **[B]** StateSet 5타입 capture→save→CACHE_MODE_IGNORE 재로드→re-capture에서 key/value_type 보존 +
  **재로드 직후·재캡처 후 Definition value typeof 직접 단언**(`_check_typeof` — String/StringName, int/float 구분)
  + runtime_nodes params typeof + Effect 연결(kind, Say 소스 포함) + node id 보존,
  **[C]** StateAdd INT/FLOAT 왕복 + Definition delta typeof 직접 단언, **[D]** 저장·재로드된 authored 그래프가
  실제 `WorldStateStore`로 런타임 실행돼 `gold` 100→set 200→add +5=205, **[F]** 잘못된 INT literal "abc"는
  capture에서 String으로 보존(조용한 0 변환 없음) → `_validate_runtime_snapshot` 저장 차단 → 런타임 Store
  `type_mismatch` 거부, 값 불변.
- 회귀 DT-004 Step2~4(effect flow + editor), DT-008 Step2/4/5(editor round-trip + condition/choice), DT-009
  Step1/Step2 ALL PASS. Godot 4.6.3 headless `--import` 0 오류.

남은 위험/이월:
- Choice→mutation→Branch/conditional Choice 재평가 **전체 e2e**와 완료 판정은 Step 4.
- Choice Effect 출력은 Step 3 리뷰에서 **항목별 Effect authoring이 필요**하다고 판정되어 Step 3b로 구현했다(아래).

## Step 3b: Per-Choice Effect Authoring

목표:
- 선택지마다 별도 Effect chain을 연결해 "선택 결과에 따라" 다른 mutation을 실행한다(단일 공유 포트로는
  어떤 선택지를 골라도 같은 mutation이 실행돼 User Outcome을 만족하지 못함 — Step 3 리뷰 P1).

작업 범위:
- Choice 노드에 항목별 Effect 출력 포트 추가(flow/data 포트 index 보존을 위해 flow 출력 뒤에 배치).
- 연결에 `choice_index` 보존(capture/save/reload), 선택 시 해당 항목 + 공통 Effect만 실행.
- Choice resize 시 남은 항목의 항목별 Effect 연결 유지(포트 remap), 삭제 항목만 제거.
- 공통 Effect(choice_index 없는 수작업/레거시 연결) 호환.

완료 조건:
- 선택 항목의 Effect만 실행되고 다른 항목 Effect는 실행되지 않는다(공통 Effect는 모든 선택지에서 실행).
- 항목별 연결과 choice_index가 저장/재로드/리사이즈 후 보존된다.
- 기존 Choice flow/data 연결과 DT-004/DT-008 Choice 동작이 유지된다.

상태: **구현·리뷰 완료(2026-06-16).**

구현 결과:
- **데이터/런타임.** `get_runtime_effect_node_ids(from, choice_index=-1)`: choice_index<0이면 전체 Effect,
  >=0이면 `connection.choice_index == choice_index`거나 choice_index 없는(공통, ci<0) Effect만 반환한다.
  `DialoguePlayer.select_choice`가 `_run_effects(choice_id, original_port)`로 선택 항목 index를 넘기고,
  `_run_effects(from, choice_index=-1)`가 초기 큐에 필터를 적용한다(Effect→Effect 체인 자식은 -1=전체).
  연결 dict의 `choice_index`는 `set_runtime_snapshot`의 `duplicate(true)`로 runtime에 보존된다.
  `choice_index`는 `has()`로 필드 부재와 명시적 값을 구분한다(typed int 대입 회피로 런타임 SCRIPT ERROR도 방지).
  계약(에디터 load와 동일): **필드 없음 → 공통 실행**, **유효 int → 항목/공통 규칙**, **필드 있으나 null/String/
  Dictionary → fail-closed 건너뜀**. 명시적 null은 공통으로 취급하지 않는다(에디터도 거부 — 계약 일치, Step 3b 리뷰).
- **에디터.** `ChoiceNode` 출력 포트 = flow(0..n-1) + 항목별 effect(n..2n-1) + **공통 effect(2n)**. flow/data
  포트 index를 보존해 기존 리소스/연결과 호환. `effect_choice_index_for_port`(항목별만 0..n-1, 공통/비effect는
  -1) / `effect_port_for_choice_index` / `common_effect_port`(=2n)로 매핑. `editor.gd` capture가 항목별 effect
  연결에만 `choice_index`를 기록하고(공통 포트는 -1이라 미기록), load가 정규화한다:
  choice_index 있음 → 유효 int면 항목 포트·**잘못된 타입/범위는 첫 포트 fallback 없이 오류 후 연결 건너뜀**,
  choice_index 없음 → **Choice 전용 공통 포트**(비-Choice는 첫 effect 포트). resize(`update_item`)는
  old_count→count base로 항목별 + 공통 effect 연결을 모두 remap해 남은 항목/공통을 유지한다.
- **공통 Effect 왕복 버그 수정(Step 3b 리뷰 P1).** 이전에는 공통 연결을 첫 effect 포트(=항목0 포트)에 실어
  recapture 시 `choice_index=0`이 붙어 공통이 "A 선택 전용"으로 오염됐다. 전용 공통 포트(2n)를 추가해
  capture/recapture에서 choice_index가 부여되지 않도록 분리했다.
- Start/Say Effect 출력 tooltip을 "Portrait/State 명령"으로 갱신(실제 기능 반영).

검증:
- 신규 `dt009_step3b_per_choice_effect_test` ALL PASS: **[A]** 항목0 선택 → gold=200(항목0)+hp=5.0(공통),
  999(항목1) 미실행 / 항목1 선택 → gold=999+hp=5.0, 200 미실행, **[B]** 공통 Effect는 양쪽 선택에서 실행,
  **[C]** 2항목 Choice 출력 5개(flow 2 + 항목별 effect 2 + 공통 1), capture가 choice_index 0/1 보존·공통은 미부여,
  save→reload→recapture 보존, **[D]** resize 2→1 시 항목1 Effect 제거·항목0 유지 + **공통 연결도 remap 보존
  (choice_index 부재 유지)** + 포트 remap, **[E]** 공통 Effect 저장→재로드→재캡처 후 **choice_index 부재 유지**
  (항목0 오염 없음) + 양쪽 선택 모두 실행, **[F]** 잘못된 choice_index(범위 밖)는 첫 포트 fallback 없이 연결
  건너뜀(에디터 load), **[G]** choice_index 계약(런타임 직접 실행): 필드 없음=공통 실행, 유효 int=실행,
  **명시적 null/String/Dict=건너뜀**(13 뒤의 명시적 null이 gold를 덮어쓰지 않아 777 유지로 검증, 필드 없는 연결은
  hp=5.0 공통 실행), 무크래시.
- 회귀 DT-004 Step1~4(Choice 포함), DT-008 Step2~5(conditional Choice 포함), DT-009 Step1/2/3 ALL PASS.
  Godot 4.6.3 headless `--import` 0 오류.

## Step 4: End-to-End Integration and Completion Review

목표:
- 선택 결과가 상태를 변경하고 이후 조건 분기/선택지에 반영되는 전체 RPG 대화 흐름을 완료 판정한다.

작업 범위:
- 실제 Manager/UI/WorldStateStore를 사용한 복합 그래프
- Choice 선택 -> Set/Add Effect -> Branch/conditional Choice 재평가
- 반복 실행, 교체, provider 누락, read-only/type/domain/busy 실패
- 전체 회귀와 System/User Guide/Current-State/Open-Tasks/Review 갱신

완료 조건:
- 선택 전후 Store 값, mutation report, 다음 조건 결과와 Effect 저장 순서가 일치한다.
- 실패 Effect가 값을 부분 변경하거나 stale provider를 건드리지 않는다.
- DT-004~008 회귀와 headless editor load가 성공한다.
- P0/P1이 없고 수동 에디터 테스트 시나리오가 문서화된다.

검증 방법:
- editor-authored resource round-trip + runtime e2e + 전체 회귀 matrix

선행 조건: Step 3 리뷰 완료

상태: **완료(2026-06-16). 제품 코드 변경 없음(검증 + 문서 단계). 판정 Approved after design fixes.**

구현 결과:
- 신규 `dt009_step4_e2e_completion_test`(실제 `DialogueManager → DialogueUI → DialoguePlayer → WorldStateStore`
  전체 경로). 통합 그래프 `Start → Choice["take"/"leave"] → Branch(gold>=150) → Say "Rich"/"Poor" → End`,
  "take"(항목0) Effect = `state_add(gold, +50)`. read/mutation provider 양쪽에 같은 Store 주입.
- 시나리오 A~G ALL PASS:
  - **[A]** "take" 선택 → 항목0 Effect로 gold 100→150 → 그 직후 Branch `state_condition`이 변경된 150을 읽어
    true → "Rich". mutation report(add, old 100/new 150) 1회. (선택→mutation→다음 조건 평가 일치)
  - **[B]** "leave" 선택 → mutation 없음 → gold 100 → Branch false → "Poor", report 0건.
  - **[C]** 반복 실행 일관성(각 실행이 자기 Store로 동일 결과).
  - **[D]** same-frame 교체(latest-wins) → 폐기 provider mutation 0회·값 불변, 활성만 150.
  - **[E]** mutation provider 누락 → `state_add` `provider_missing` 실패, gold 불변(100), Flow 계속 → "Poor".
  - **[F]** read-only gold → `read_only` 실패, gold 불변, Flow 계속 → "Poor".
  - **[G]** 에디터 authored 그래프(Choice + 항목별 Add) save → CACHE_IGNORE reload → DialogueManager 실행에서
    take=gold 150 / leave=gold 100(항목별 mutation이 왕복 후에도 동작).
- **전체 회귀 matrix ALL PASS(30 scene)**: DT-004 Step1~4(+pipeline), DT-005 Step1~6, DT-006 Step1~5,
  DT-007 Step1~4, DT-008 Step1~5, DT-009 Step1/2/3/3b/4. Godot 4.6.3 headless `--import` 0 오류.
- P0/P1 없음(완료 리뷰는 사용자/리뷰어 판정).
- 완료 리뷰 판정: **Approved after design fixes**. 상세는 [[DT-009-State-Mutation-Review]].

### 수동 에디터 테스트 시나리오(제작자용)

1. DialogueTool 에디터에서 노드 목록의 **StateSet**/**StateAdd**를 캔버스로 드래그한다.
2. StateSet: key(예: `player.gold`) + type(int) + value(예: `200`)를 입력한다. 잘못된 값(예: int에 "abc")은
   저장 시 검증 오류로 차단된다. StateAdd: key + type(int/float만) + delta를 입력한다.
3. Start/Say의 **effect 출력(주황)** 또는 Choice의 **항목별/공통 effect 출력(주황)** 을 state 노드의
   **effect 입력(주황)** 에 연결한다. 항목별 포트는 그 선택지를 고를 때만, 공통 포트는 항상 실행된다.
4. 저장 후 재로드해 key/type/value와 연결, choice_index가 보존되는지 확인한다.
5. 게임 코드에서 `DialogueManager.play(resource, world_state, world_state)`로 실행하고, 선택 후 Branch/조건부
   Choice가 변경된 상태를 읽는지 확인한다(provider 누락/ read-only는 값 불변 + Flow 계속).

## Design Risks

1. Player가 Add를 read -> set으로 계산하면 provider 불일치와 비원자적 변경이 생긴다.
2. mutation provider를 read provider에서 암시적으로 승격하면 읽기 전용 대화가 쓰기 권한을 얻는다.
3. deferred 시작에서 resource/read/mutation 중 하나가 분리되면 폐기 대화가 잘못된 Store를 변경한다.
4. Effect 실패 뒤 report를 다른 provider의 read 값으로 만들면 실제 적용 결과와 로그가 불일치한다.
5. signal listener가 report나 실행 상태를 변조하면 Flow와 실제 Store 상태가 어긋날 수 있다.
6. Add의 int/float 암시 변환이나 overflow가 Store의 JSON-safe snapshot 보장을 깨뜨릴 수 있다.
7. mutation Effect를 whitelist에만 추가하고 editor validation/runtime dispatch 중 하나를 빠뜨리면 저장은 되지만
   실행되지 않거나 반대로 수작업 리소스가 검증을 우회한다.
8. 여러 Effect를 암묵적으로 atomic하다고 가정하면 중간 실패 시 제작자 기대와 실제 상태가 달라진다.

## Verification Matrix

| Area | Required cases |
| --- | --- |
| Store Add | INT/FLOAT 성공, zero/no-op, strict mismatch, non-numeric, unknown, read-only, unsafe domain, busy |
| Provider | null/non-Object/missing method/arity/type/return contract, explicit read/mutation separation |
| Runtime Set | 5개 타입, same-value, provider failure, report 불변성 |
| Runtime Add | 양수/음수/0, 연속 Add 순서, 경계 실패, 앞 성공+뒤 실패 정책 |
| Lifecycle | 반복 실행, same-frame latest-wins, 폐기 provider mutation 0회, 종료 후 stale callback 없음 |
| Effect | Start/Say/Choice 실행 시점, Portrait와 혼합 저장 순서, cycle/invalid target validation |
| Editor | 생성/capture/save/cache-ignore reload/re-capture, typed literal `typeof()`, Effect 연결 보존 |
| E2E | Choice -> mutation -> Branch/conditional Choice, reset/snapshot restore 이후 재실행 |
| Regression | DT-004 Effect, DT-005 Store/batch, DT-006 lifecycle, DT-007 evaluator, DT-008 condition/Choice |

## Completion Criteria

- State Set/Add가 명시적 mutation provider를 통해 타입 안전하게 실행된다.
- Add는 provider/Store 내부의 승인된 원자적 API를 사용한다.
- mutation 결과와 실패가 구조화 report로 관찰 가능하며 listener 변조에 안전하다.
- Choice/Flow 실행 순서와 다음 Condition 평가가 실제 Store 최종 상태와 일치한다.
- 에디터 저장/재로드와 기존 Effect/Dialogue 회귀가 유지된다.
- Task/ADR/System/User Guide/Review가 현재 코드 사실과 일치한다.

## Related

- [[DT-004-Nonblocking-Effect-Flow]]
- [[ADR-005-Nonblocking-Effect-Connections]]
- [[DT-005-StateSchema-WorldStateStore]]
- [[ADR-006-Typed-World-State]]
- [[DT-008-State-Condition-Dialogue-Integration]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[World-State-System]]
- [[DialogueTool]]
