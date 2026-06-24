---
id: DT-013
type: task
status: complete
system: DialogueTool, WorldState
created: 2026-06-18
updated: 2026-06-18
---

# DT-013 State Read Data Node

## Goal

WorldState의 단일 key 값을 Dialogue Data Flow에서 재사용 가능한 값으로 읽는 `State Read` Data 노드를 추가한다.

이 노드는 ConditionSet처럼 boolean 조건만 내는 노드가 아니라, `bool/int/float/String/StringName` 값을 그대로
Data consumer(Branch, Choice 조건, Expression 등)에 공급하는 leaf Data node다.

## Non-Goals

- WorldState 값을 변경하지 않는다. 변경은 기존 `state_set`/`state_add` Effect가 맡는다.
- schema-aware key picker, key autocomplete, live schema introspection은 범위 밖이다.
- int/float/string 전용 GraphEdit port category를 새로 만들지 않는다. 현재 포트 체계는 `data`/`boolean`/
  `effect`/`flow`이고, 새 typed port family는 별도 그래프 타입 시스템 작업이다.
- Text interpolation, Response Selector, DialogueHistory/State Inspector UI는 범위 밖이다.
- `/root/WorldState` autoload를 직접 조회하지 않는다. 기존 read provider 주입 경계를 유지한다.

## Context

- `DialoguePlayer`는 read provider를 `set_read_state_provider(provider)`로 주입받고, `/root`를 직접 조회하지
  않는다.
- `state_condition`은 `ConditionEvaluator.evaluate(condition_set, _read_state_provider)`로 원본 provider를
  전달하고, 오류는 Data error-dominance로 Branch/Choice에서 fail-closed된다.
- `state_set`/`state_add`는 별도 mutation provider를 소비한다. read provider가 mutation 권한으로 승격되지
  않는다.
- `DialogueNode.port_type`은 `flow`, `data`, `boolean`, `effect`만 갖는다. 따라서 State Read의 expected type은
  런타임 strict validation과 노드 표시 계약이지, 새 포트 색/카테고리 계약이 아니다.

## Design

자세한 장기 결정은 [[ADR-015-State-Read-Data-Node]]에 둔다.

### Runtime Contract

- runtime type: `state_read`
- params:
  - `key: StringName | String`
  - `value_type: int` (`TYPE_BOOL`, `TYPE_INT`, `TYPE_FLOAT`, `TYPE_STRING`, `TYPE_STRING_NAME` 중 하나)
- return shape: 기존 Data evaluator와 동일하게 `{ value, errored }`.
- success:
  - provider가 존재하고 계약을 만족한다.
  - `has_state(key) == true`.
  - `read_state(key)`의 `typeof(value)`가 `value_type`과 정확히 일치한다.
  - 반환: `{ "value": value, "errored": false }`.
- failure:
  - 반환: `{ "value": null, "errored": true }`.
  - Branch/Choice/Expression 소비자는 기존 error-dominance 흐름으로 fail-closed된다.
  - provider 누락/계약 위반/key invalid/state missing/type mismatch가 SCRIPT ERROR가 되면 안 된다.

### Report Signal

`DialoguePlayer`에 평가 관찰용 signal을 추가한다.

```gdscript
signal state_read_evaluated(read_node_id: int, consumer_node_id: int, report: Dictionary)
```

report 권장 shape:

```gdscript
{
    "ok": bool,
    "key": StringName,
    "expected_type": int,
    "actual_type": int,
    "value": Variant,
    "error": StringName,
}
```

- 평가 1회당 정확히 1회 발행한다.
- signal에는 `report.duplicate(true)`를 전달한다.
- `consumer_node_id`는 해당 Data 입력 포트를 직접 소유한 노드 id다(`state_condition` 패턴).
- 성공 report는 `value`를 포함한다. 이 seam은 privacy filter가 없는 Dialogue runtime 내부 debug/trace seam이다.
- 값을 읽지 않은 실패(`provider_missing`, `provider_contract_invalid`, `key_invalid`, `state_missing` 등)는
  `actual_type = TYPE_NIL`, `value = null`로 고정한다.
- 값을 읽은 뒤 expected type과 불일치한 경우는 `actual_type = typeof(read_value)`, `value = read_value`를
  포함한다.

### Provider Contract

State Read는 `_read_state_provider`를 직접 소비한다. facade `DialoguePlayer.read_state()`를 경유해
`provider_missing`과 `state_missing`을 섞지 않는다.

필수 메서드:

```gdscript
has_state(key: StringName) -> bool
read_state(key: StringName) -> Variant
```

검사 정책:

- null provider: `provider_missing`.
- freed/non-Object/method missing/arity mismatch/`has_state` non-bool: `provider_contract_invalid`.
- `read_state` method missing/arity mismatch/first-arg type mismatch는 `has_state == true` 경로에서도 호출 전에
  `provider_contract_invalid`로 차단한다.
- `has_state(key) == false`: `state_missing`, `read_state` 호출 없음.
- `read_state` 반환 타입이 expected type과 다름: `actual_type_mismatch`.
- String과 StringName, int와 float 사이의 암시적 변환은 없다.

### Editor Node Shape

Graph node 이름: `State Read`.

노출 필드:

- key LineEdit: `player.gold`, `quest.main_state` 같은 WorldState key 문자열.
- type OptionButton: `BOOL`, `INT`, `FLOAT`, `STRING`, `STRING_NAME`.
- summary label: `<key> : <TYPE>` 또는 key가 없으면 `No State Key`.

포트:

- input 없음.
- output 1개: generic `data`.

BOOL도 `boolean` 포트가 아니라 `data` 포트를 쓴다. 현재 editor는 `data <-> boolean` 연결을 호환 처리하므로
Branch/Choice 조건 입력에도 연결할 수 있다. expected type이 `BOOL`이면 런타임에서 실제 bool만 통과한다.

### Save Validation

저장/캡처 시 최소 구조를 막는다.

- key empty 또는 WorldState key pattern 불일치: 저장 실패.
- value_type이 허용 5타입 밖: 저장 실패.
- schema 존재 여부는 검증하지 않는다. editor는 provider-free 상태를 유지하고, 실제 key 존재는 runtime provider가
  판정한다.
- key pattern source of truth는 `StateSchema.KEY_PATTERN` 또는 같은 helper다. `ConditionValidator`가 같은 패턴을
  재사용하는 현재 구조와 맞춘다.

## Steps

### Step 0 — Design Review

- 이 문서와 [[ADR-015-State-Read-Data-Node]]를 실제 코드와 대조한다.
- 특히 포트 계약(`data` output 고정), provider 실패 정책, signal/report shape, Step 분해가 구현 가능한지 확인한다.
- 제품 코드, `.tscn`, `.tres`는 수정하지 않는다.

### Step 1 — Runtime State Read Evaluator

Scope:

- `DialoguePlayer._eval_data`에 `state_read` 분기를 추가한다.
- `_evaluate_state_read(...)` helper와 `state_read_evaluated` signal을 추가한다.
- runtime snapshot을 직접 구성하는 headless 테스트를 추가한다.
- editor 노출/GraphNode/Definition은 아직 추가하지 않는다.

Required tests:

- success: 실제 `WorldStateStore` read provider에서 BOOL/INT/FLOAT/STRING/STRING_NAME 값을 읽는다.
- missing provider: `provider_missing`, errored true, SCRIPT ERROR 0.
- bad provider: non-Object/freed/method missing/arity mismatch/has_state non-bool -> `provider_contract_invalid`.
- `read_state` method missing/arity mismatch/first-arg typed int 또는 incompatible type은 호출 전에
  `provider_contract_invalid`; SCRIPT ERROR 0.
- key param은 `String`/`StringName`이면 동일하게 읽고, int/Dictionary 등 손상 Variant는 변환하지 않고
  `key_invalid` + errored true + SCRIPT ERROR 0.
- state missing: `has_state == false`이면 `read_state` 호출 0.
- type mismatch: `actual_type_mismatch`, 암시적 변환 없음.
- report sentinel: 값 미읽기 실패는 `actual_type == TYPE_NIL`, `value == null`; type mismatch는 실제
  `actual_type`과 `value` 포함.
- Branch/Choice/Expression 소비에서 errored가 true로 뒤집히지 않는 error-dominance 회귀.
- signal이 평가당 1회, consumer id와 detached report를 보존.

### Step 2 — Editor Authoring and Resource Round-Trip

Scope:

- `WorldStateReadDef` Data Definition 추가.
- `WorldStateReadNode` GraphNode scene/script 추가.
- `world_state_read_editor_adapter`와 `node_type_registry` 등록.
- capture/apply/save/reload/recapture 왕복 테스트.

Required tests:

- 노드 목록에 `State Read` 노출.
- key/type 입력이 runtime params로 보존된다.
- output port는 generic `data` 1개이며 기존 Flow/Data/Boolean 포트 index를 깨지 않는다.
- Branch/Choice boolean 입력과 연결 가능한지 검증한다.
- invalid key/type은 저장 validation에서 차단된다.
- invalid key matrix는 `StateSchema.KEY_PATTERN` 기준으로 `quest`, `Quest.main`, `quest..main`,
  `1quest.main` 등을 포함한다.
- `.tres` 저장 -> cache ignore reload -> 재캡처에서 key/type/connection이 보존된다.

### Step 3 — End-to-End Integration

Scope:

- 실제 `DialogueManager -> DialogueUI -> DialoguePlayer` provider 주입 경로에서 State Read가 값 supplier로
  동작하는지 검증한다.
- Expression/Branch/Choice 소비를 함께 확인한다.
- debug Play preview provider(DT-010)와 충돌 없이 같은 read provider를 소비하는지 확인한다.

Required tests:

- `State Read(INT)` -> Expression 비교/계산 -> Branch 또는 Choice 조건.
- `State Read(BOOL)` -> Branch/Choice 조건.
- provider 누락/unknown key/type mismatch가 플레이어 흐름을 fail-closed로 만들고 상태를 변경하지 않는다.
- debug preview example schema key는 읽히고, 없는 game schema key는 `state_missing`으로 닫힌다.

### Step 4 — Documentation and Completion Review

Scope:

- [[DialogueTool]], [[World-State-System]], [[DialogueTool-User-Guide]], [[Open-Tasks]], [[Current-State]] 갱신.
- Step 1~3 완료 조건 대조와 회귀 재실행.
- 리뷰 문서 `50_Reviews/DT-013-State-Read-Data-Node-Review.md` 작성.

Suggested regression:

- DT-008 State Condition/conditional Choice.
- DT-009 State Mutation.
- DT-010 debug preview.
- DT-012 Condition summary.
- editor `--import`.

## Open Questions

현재 blocking open question은 없다. Step 0 설계 리뷰에서 아래 항목을 확정했다.

- `state_read_evaluated` report에 성공 `value`를 포함할지, trace privacy를 위해 생략할지.
  - 확정: 포함. Dialogue runtime 내부 값 trace seam이며 기존 mutation report도 old/new를 노출한다.
- key param에서 `String`을 호환 입력으로 허용할지.
  - 확정: 허용 후 `StringName`으로 정규화. String/StringName 외 Variant는 변환하지 않고 `key_invalid`.
- BOOL expected type일 때 output port를 `boolean`으로 바꿀지.
  - 확정: 바꾸지 않음. dynamic port category는 연결 안정성과 round-trip 위험을 키운다.

## Step 0 Design Review Result

2026-06-18 설계 리뷰 판정: **Approved after design fixes**([[DT-013-State-Read-Data-Node-Review]]).

반영한 design fixes:

- Step 1 provider 테스트에 `read_state` method missing/arity/first-arg mismatch를 호출 전 차단하는 조건 추가.
- report 실패 sentinel을 고정: 값 미읽기 실패는 `actual_type = TYPE_NIL`, `value = null`; type mismatch는 실제
  타입과 값을 report에 포함.
- key validation source of truth를 `StateSchema.KEY_PATTERN` 또는 동일 helper로 명시하고 invalid key matrix 추가.
- 손상 key Variant 처리 테스트 추가: String/StringName만 정규화, 그 외는 `key_invalid`로 fail-closed.

## Step 1 Implementation Result (2026-06-18)

판정: 구현 완료 — 리뷰 대기. 제품 코드는 `DialoguePlayer`만 변경했고 editor/Definition/Adapter/Registry/
`.tscn`/`.tres`는 건드리지 않았다(Step 2 범위 유지).

변경 파일:

- `addons/dialogtool/RunTime/dialogue_player.gd`
  - `state_read_evaluated(read_node_id, consumer_node_id, report)` signal 추가.
  - `_eval_data`에 `state_read` 분기 추가.
  - `_evaluate_state_read(node_id, consumer_node_id, params)` + `_finish_state_read(...)` helper 추가.
  - read provider 계약 검증 helper `_is_valid_read_provider(...)` + `_method_returns_bool_or_untyped(...)`
    추가(기존 `_method_accepts`/`_arg_compatible` 재사용).
- `addons/dialogtool/RunTime/tests/dt013_step1_state_read_test.gd`/`.tscn` 신규(헤드리스 14 시나리오 A~N).

구현 판단:

- provider 계약 검증은 `state_set/state_add` mutation 검증과 같은 **`as Object` 캐스트 없는** 안전 패턴을
  쓴다. `ConditionEvaluator._read_provider_contract_error`는 `p as Object`로 캐스트하는데, freed Object
  캐스트는 그 자체로 SCRIPT ERROR이므로 재사용하지 않고 `typeof + is_instance_valid`로 Variant에 직접
  검사한 뒤 reflection으로 두 메서드의 arity/첫 인자 타입을 확인한다. has_state 런타임 반환형 non-bool은
  호출부에서 재확인한다.
- key는 String/StringName만 `StringName`으로 정규화하고, 그 외 Variant는 변환하지 않고 `key_invalid`로
  fail-closed한다(provider 미접촉). 평가 검사 순서: key 정규화 → provider null → 계약 → has_state 런타임
  bool → state_missing → read_state + strict typeof.
- 반환 Data value는 발행 전에 확정하고 signal에는 `report.duplicate(true)`를 넘겨, 동기 listener가 분기/값을
  못 바꾼다. type mismatch report는 실제 `actual_type`/`value`를 보존하지만 반환 Data value는 null이다.

검증:

- `dt013_step1_state_read_test` ALL PASS(SCRIPT ERROR 0). 실제 `WorldStateStore`에서 BOOL/INT/FLOAT/
  STRING/STRING_NAME success, provider_missing, provider_contract_invalid matrix(non-Object/freed/has·read
  method missing/arity/has_state 선언+런타임 non-bool), read_state 계약 위반 호출 전 차단(read_calls 0),
  state_missing(read_calls 0), key String/StringName 호환 + 손상 Variant key_invalid(provider 미접촉),
  actual_type_mismatch 무변환, report sentinel, Branch/Expression/Choice error-dominance, INT 값 공급,
  signal 1회/consumer 보존/detached report.
- 회귀: dt008_step1/step4/step5, dt009_step2, dt010_step1 ALL PASS. `--import` 0 parse error.

## Step 2 Implementation Result (2026-06-18)

판정: 구현 완료 — 리뷰 대기. editor authoring 표면(Definition/GraphNode/Adapter/Registry + 저장 validation)만
추가했고 런타임(`DialoguePlayer`)은 Step 1에서 확정한 그대로 변경하지 않았다.

변경/추가 파일:

- `addons/dialogtool/Resource/NodeDefinitions/Data/world_state_read_def.gd` 신규
  (`class_name WorldStateReadDef extends DataDefinition`). `key: StringName`, `value_type: int`(기본 TYPE_BOOL),
  `get_runtime_type() -> &"state_read"`, `get_runtime_params() -> {key, value_type}`, `validate_structure()`
  (provider-free: value_type 허용 5타입 + key empty/`StateSchema.KEY_PATTERN` 형식 검사), `type_label()`(BOOL/INT/
  FLOAT/STRING/STRING_NAME 표시 라벨). `READ_VALUE_TYPES` 단일 정의.
- `addons/dialogtool/Node/world_state_read_node.gd`/`.tscn` 신규
  (`class_name WorldStateReadNode extends DialogueNode`). key LineEdit + type OptionButton + summary label.
  summary는 `<key> : <TYPE>` 또는 `No State Key`(invalid 색). 값 변경 시 deferred `_capture`.
- `addons/dialogtool/Editor/Adapter/world_state_read_editor_adapter.gd` 신규(경로 기반 extends).
  generic data output 포트를 slot 1에 두고 params↔노드 접근자(`set_key/get_key/set_value_type/get_value_type`)를 잇는다.
- `addons/dialogtool/Editor/Adapter/node_type_registry.gd`: `state_read` → world_state_read_editor_adapter 등록.
- `addons/dialogtool/Editor/editor.gd`: `_validate_runtime_snapshot`에 `WorldStateReadDef.validate_structure()`
  저장 차단 분기 추가(StateEffectDef literal 검증 패턴과 동일).
- `addons/dialogtool/RunTime/tests/dt013_step2_editor_roundtrip_test.gd`/`.tscn` 신규(실제 `dialoguetool_main.tscn`
  fixture, A~F).

구현 판단:

- 노드 목록/타이틀 표시 이름은 `class_name`에서 "Def"를 떼어 도출되므로(`DialogueNodeItemList`,
  `DialogueNode._ready`) "State Read"(공백 포함)는 ItemList에 표현 불가다. WorldState 계열 명명
  (WorldStateConditionDef→"WorldStateCondition") 규칙을 따라 **WorldStateReadDef → "WorldStateRead"**로 노출한다.
  ADR의 "State Read"는 개념 명칭이며, 사용자에게 보이는 라벨은 "WorldStateRead"다(naming deviation 보고).
- output port는 generic `data` 1개로 고정(ADR-015 D2). `editor.gd`가 등록한 data↔boolean 호환으로 Branch/Choice
  boolean 조건 입력에 연결된다. BOOL도 dynamic boolean port로 바꾸지 않는다.
- key validation source of truth = `StateSchema.KEY_PATTERN`(ConditionValidator와 동일 regex 재사용). schema에
  key 실재 여부는 검사하지 않는다(D6: runtime provider가 판정).

### Step 2 P3 — 노드 표시 이름(수용, 후속 이관)

- **현상**: ADR-015 Editor Node Shape는 graph node 이름을 "State Read"로 적었지만, 실제 노드 목록
  (`dialogue_node_item_list.gd`)과 그래프 타이틀(`dialogue_node.gd`)은 `class_name`에서 "Def"를 떼어 도출하므로
  `WorldStateReadDef` → **"WorldStateRead"**로 표시된다. 공백 포함 "State Read"는 이 메커니즘으로 표현 불가다.
- **판정**: 수용(P3, blocking 아님). 기능/저장·재로드/런타임 동작에 영향이 없고, 기존 `WorldStateConditionDef`
  → "WorldStateCondition" 명명 규칙과 일치한다. ADR의 "State Read"는 개념 명칭으로 둔다.
- **후속**: 노드별 display name/alias 시스템을 도입할 때 "State Read"로 보이게 정리한다([[Open-Tasks]] Later).
  그때까지 사용자 표시 라벨은 "WorldStateRead"임을 문서에 명시한다.

검증:

- `dt013_step2_editor_roundtrip_test` ALL PASS, SCRIPT ERROR 0:
  - [A] 노드 목록 "WorldStateRead" 노출 + `NodeTypeRegistry`에 state_read 어댑터 등록.
  - [B] key/type 입력이 `runtime_nodes[id].params`(key/value_type)로 보존, runtime type `state_read`.
  - [C] output port = generic data 1개, input 0개, data↔boolean 연결 호환, Branch boolean 입력 연결 capture
    (kind != effect).
  - [D] invalid key matrix(`quest`, `Quest.main`, `quest..main`, `1quest.main`, "") + value_type 허용 5타입/그 외
    차단, editor `_validate_runtime_snapshot`가 invalid key·invalid type을 fatal로 막고 valid는 통과.
  - [E] summary `<key> : <TYPE>` / `No State Key`(invalid 색) + 타입 변경 반영.
  - [F] `.tres` 저장 → cache ignore reload → 재캡처에서 key/type/connection + data output 포트 보존.
- 회귀: dt013_step1, dt009_step3(editor 저장 validation/노드 목록/registry), dt008_step2/step5, dt012_step2
  ALL PASS(SCRIPT ERROR 0). `--import` 0 parse error.

## Step 3 Implementation Result (2026-06-18)

판정: 구현 완료 — 리뷰 대기. **제품 코드 변경 없음**(통합 검증 단계). 런타임(Step 1)·에디터(Step 2)가 이미
확정돼 있어, 실제 `DialogueManager → DialogueUI → DialoguePlayer` provider 주입 경로에서 state_read가 값
supplier로 동작하는지 e2e로만 확인했다.

추가 파일:

- `addons/dialogtool/RunTime/tests/dt013_step3_e2e_test.gd`/`.tscn` 신규(A~G, watchdog 30s).

검증(`dt013_step3_e2e_test` ALL PASS, SCRIPT ERROR 0, 실제 Manager 경로):

- [A] `State Read(INT) → Expression("x > 5") → Branch`: 7→TRUE(report ok/value=7, consumer=expression id),
  5→FALSE. State Read가 Expression 비교의 값 공급자로 동작.
- [B] `State Read(BOOL) → Branch`: true→TRUE(consumer=branch id), 유효 false→FALSE(report ok=true, 논리 false).
- [C] `State Read(BOOL) → Choice 항목 조건`: true→["A","B"], 유효 false→["B"](항목0 숨김), consumer=choice id.
- [D] provider 미지정 → Branch FALSE(`provider_missing`) / Choice 항목 숨김. fail-closed.
- [E] unknown(미등록) key → `state_missing` fail-closed(FALSE) + store 값 불변(순수 read).
- [F] type mismatch(FLOAT key를 INT로 read) → `actual_type_mismatch` fail-closed(FALSE), report `actual_type`=
  FLOAT 보존 + store 값 불변.
- [G] **debug preview store**(`DialogueDebugPreviewProvider.make_preview_store()`): example schema key
  (session.intro.seen)는 정상 읽힘(TRUE, ok), example schema에 없는 game key(game.only.flag)는 `state_missing`으로
  닫힘. State Read가 DT-010 debug preview read provider와 충돌 없이 같은 계약을 소비.

설계 판단:

- type mismatch fail-closed은 errored Data를 Branch에 직접 공급해 확인했다. 비교 연산자(`x > 5`)가 null(errored)
  입력에 닿는 Expression 경로는 엔진이 "Invalid operands" 로그를 내며 `has_execute_failed()`로 잡혀 fail-closed되는데
  (state_read 한정 동작이 아니라 모든 null Data 입력 공통), 그 error-dominance 회귀는 `or/not` 형태로
  dt013_step1[K]에서 별도 검증한다.

회귀: dt013_step1/step2, dt008_step3, dt009_step4, dt010_step3 ALL PASS(SCRIPT ERROR 0). `--import` 0 parse error.

## Step 4 Implementation Result (2026-06-18) — DT-013 완료

판정: **완료**([[DT-013-State-Read-Data-Node-Review]] Completion Review). 문서 갱신 + Step 1~3 완료 조건 대조 +
회귀 재실행.

문서 갱신:

- [[DialogueTool]]: Supported Runtime Nodes 표에 `state_read` 추가, "State Read (DT-013)" 절 신규,
  Project Integration Dependency에 read provider 소비 + `StateSchema.KEY_PATTERN` 재사용 추가.
- [[World-State-System]]: State Read 노드 완료 사실로 갱신("아직 없는 것"에서 제거).
- [[DialogueTool-User-Guide]]: §6 Data 노드에 "State Read (DT-013)" 절 추가(필드/포트/fail-closed/저장 validation).
- [[Current-State]]/[[Open-Tasks]]: DT-013 Step 1~4 완료 반영, 노드 display name/alias는 Later 후속.

검증(회귀 매트릭스 11/11 GREEN, 실제 `SCRIPT ERROR:` 0건, `--import` 0 parse error):

- DT-013: step1/step2/step3.
- DT-008: step1/step4/step5. DT-009: step2/step4. DT-010: step1/step3. DT-012: step2.

남은 후속(범위 밖): 노드별 display name/alias 시스템(Step 2 P3, "WorldStateRead" → "State Read" 표시).

## Completion Criteria

- State Read Data 노드를 editor에서 만들고 저장/재로드할 수 있다.
- runtime은 injected read provider만 사용하며 `/root/WorldState`를 직접 조회하지 않는다.
- provider/key/type 오류는 SCRIPT ERROR 없이 구조화 report + Data error-dominance로 fail-closed된다.
- `bool/int/float/String/StringName` strict read가 실제 `WorldStateStore`로 검증된다.
- 문서와 리뷰가 완료되고 [[Open-Tasks]]에서 DT-013이 제거되거나 후속만 남는다.
