---
id: DT-008
type: task
status: done
system: DialogueTool, WorldState
created: 2026-06-14
updated: 2026-06-15
tags: [task, dialogue, world-state, condition, branch, choice]
---

# State Condition Dialogue Integration

## Goal

DT-007의 구조화 `ConditionSet`/`ConditionEvaluator`를 DialogueTool의 실제 Data Flow에 연결한다.
제작자는 문자열 Expression 없이 `ConditionSet`을 Branch 조건으로 사용하고, 같은 조건 계층으로 Choice를
표시하거나 숨길 수 있어야 한다.

## Context

- `DialoguePlayer`는 `DialogueManager -> DialogueUI -> DialoguePlayer` 경로로 read provider를 이미 주입받는다.
- Branch는 입력 port 0의 Data 값을 `_get_data_value()`로 읽어 true/false Flow를 선택한다.
- `_get_data_value()`는 현재 `variable`과 `expression`만 처리한다.
- Choice의 각 항목에는 이미 Data 입력 port가 있지만 런타임은 이를 읽지 않고 모든 선택지를 표시한다.
- Choice UI의 표시 index는 현재 원래 Flow 출력 port index와 같다. 조건으로 일부 항목을 숨기면 이 대응이
  깨지므로 visible index에서 원래 output port로의 명시적 mapping이 필요하다.
- 조건 평가 계약과 실제 Store 통합은 [[DT-007-ConditionSet-ConditionEvaluator]],
  [[ADR-008-Structured-Condition-Evaluation]], [[DT-007-Condition-Review]]에서 완료됐다.

## User Outcome

- `ConditionSet` Resource를 Dialogue 그래프의 boolean Data 노드에 지정한다.
- 해당 노드를 기존 Branch에 연결해 World State에 따라 대화 Flow가 갈린다.
- Choice 항목별 Data 입력에 조건을 연결해 현재 상태에서 가능한 응답만 표시한다.
- 조건 오류는 대화를 크래시시키거나 잘못 통과시키지 않고 false/숨김으로 fail-closed된다.
- 평가 report를 디버거/후속 trace inspector가 소비할 수 있다.

## Scope

### Included

- `WorldStateConditionDef` Data Definition과 runtime type `state_condition`
- `ConditionSet` Resource 지정 UI, boolean output port, 저장/재로드
- `DialoguePlayer`에서 `ConditionEvaluator.evaluate(set, injected_provider)` 호출
- Branch에서 State Condition 결과 소비
- 조건 평가 report signal seam
- 기존 Choice 항목별 Data 입력의 런타임 활성화
- 조건부 Choice의 visible index -> original output port mapping
- 반복 실행, provider 미지정, invalid/missing/type mismatch, save/reload 회귀

### Out of Scope

- State Set/Add/Multiply Effect와 mutation provider
- State Read 값 자체를 반환하는 범용 Data 노드
- Response Selector, weighted/random response
- 조건 trace inspector UI와 DialogueHistory
- schema-aware key/operator picker
- ConditionSet inline tree editor
- SaveGame file/slot 시스템

## Proposed Runtime Contract

### State Condition Data Node

```text
Definition class: WorldStateConditionDef
runtime type:     state_condition
field:            condition_set: ConditionSet
input ports:      없음
output port 0:    boolean
```

- 이름은 DT-007 leaf Resource인 전역 클래스 `StateCondition`과 충돌하지 않도록
  `WorldStateConditionDef`를 사용한다.
- runtime params는 `condition_set` Resource를 보존한다.
- `_get_data_value(state_condition_node_id)`는 주입된 원본 read provider를
  `ConditionEvaluator.evaluate()`에 전달하고 `report.passed`를 반환한다.
- provider가 없거나 ConditionSet이 null/invalid이거나 runtime read 오류가 있으면 evaluator 계약에 따라
  `valid=false`, `passed=false`다. 조용한 true/default는 없다.
- `DialoguePlayer`의 `has_state()` facade를 provider로 다시 감싸지 않는다. 원본 provider가 없을 때
  `state_missing`으로 위장하지 않고 `provider_missing`을 보존하기 위해 `_read_state_provider`를 직접 전달한다.

### Evaluation Report Seam

```gdscript
signal condition_evaluated(
    condition_node_id: int,
    consumer_node_id: int,
    report: Dictionary
)
```

- valid/invalid 모두 평가 1회당 1회 발행한다(구조 invalid `read_count==0` 포함).
- report는 evaluator 내부와 이후 평가 결과에 영향을 주지 않는 detached deep copy다.
- 첫 소비자는 Branch/Choice 테스트와 debugger seam이다. UI가 조건을 다시 평가하지 않는다.
- `consumer_node_id`는 **이 Data 노드의 입력 포트를 직접 소유한 노드의 id**다(Step 0 확정, F1).
  `_get_data_value(node_id, consumer_node_id, visited)`로 consumer를 명시적으로 전달한다.
  Branch는 `current_node_id`(branch id), Choice는 choice node id, expression 입력으로 중첩된
  경우는 expression node id를 consumer로 넘긴다. signal 발행과 `report.passed` 반환은
  `state_condition`을 처리하는 `_get_data_value` 분기에서 한 곳에서 수행한다(call site 중복 발행 금지).

### Conditional Choice

- **Choice 포트 번호 계약(Step 0 확정, F2).** `choice_node.gd update_item`이 slot 1을 flow 입력으로,
  slot `i+2`(i=0..count-1)를 항목 i의 (data 입력 + flow 출력)으로 둔다. Godot GraphEdit의 enabled-slot
  순서 포트 인덱싱에 따라:
  - 항목 i의 **data 입력 port = i + 1**(port 0은 Choice의 flow 입력이다).
  - 항목 i의 **flow 출력 port = i**. 런타임 `select_choice(index)`가 index를 그대로 `from_port`로 쓴다.
  - 따라서 항목 i의 조건은 `get_runtime_input_node_id(choice_id, i + 1)`로 읽고,
    visible→original mapping은 `original_output_port == original_item_index`다.
- 기존 Choice 항목별 Data 입력 port를 사용한다. 새 parallel condition 배열을 Choice Resource에 추가하지 않는다.
- Data 입력이 없는 항목은 기존 호환을 위해 항상 표시한다.
- Data 입력이 있는 항목은 Choice 진입 시 한 번 평가하고 bool true인 항목만 표시한다.
- 표시 목록과 함께 `visible_index -> original_output_port` mapping을 DialoguePlayer가 대기 상태 동안 보관한다.
- `select_choice(visible_index)`는 mapping으로 원래 output port를 찾아 Flow를 진행한다.
- 조건은 Choice 화면이 열린 동안 다시 평가하지 않는다. 외부 상태가 바뀌어도 현재 제안 목록은 고정되고,
  Choice에 다시 진입할 때 새로 평가한다.
- 조건 오류는 해당 항목을 숨기고 report를 발행한다. 모든 항목이 숨겨지면 warning 후 기존 empty-choice와
  같은 종료 정책을 사용한다.
- 잘못된 visible index는 대기 상태와 Flow를 변경하지 않고 warning으로 거부한다.

## Compatibility

- 기존 Dialogue Resource에는 `state_condition` 노드가 없으므로 runtime snapshot 형식은 additive다.
- 기존 Choice는 항목별 Data 입력이 연결되지 않았으므로 모든 선택지가 이전과 동일하게 표시된다.
- 기존 Variable/Expression -> Branch 동작과 truthy 변환 규칙은 유지한다.
- Choice 항목의 원래 output port index와 저장된 Flow 연결은 변경하지 않는다.

## Steps

## Step 0: Design Review

목표:
- 위 runtime/editor/Choice mapping 계약을 실제 코드와 대조해 구현 전에 승인한다.

작업 범위:
- `dialogue_player.gd`, `DialogueGraphResource`, Definition/Adapter/GraphNode, Choice dynamic port 구조 검토
- [[ADR-009-State-Condition-Dialogue-Consumption]]의 D1~D7 확정
- signal, fail-closed, all-hidden Choice, 저장 호환 정책 검토

제외 범위:
- 제품 코드와 `.tscn`/`.tres` 수정

완료 조건:
- P0/P1 설계 문제가 없고 미결정 사항이 구현 가능한 수준으로 해소된다.
- 판정이 Approved 또는 Approved after design fixes다.

검증 방법:
- [[Design-Review-Prompt]] 형식의 코드 대조 리뷰

### Step 0 결과 (Design Review, 2026-06-15)

판정: **Approved after design fixes**. 실제 코드(`dialogue_player.gd`, `dialogue_graph_resource.gd`,
`choice_node.gd`, `branch_def`/`choice_def`, `DataDefinition`/`DialogueDefinition`,
`node_type_registry`, `condition_evaluator.gd`)와 대조해 D1~D7과 runtime/choice 계약이 구현 가능함을
확인했다. 아래 F1~F5를 본 문서/ADR-009에 반영하고 Step 1을 착수한다.

- **F1 consumer_node_id 스레딩(P1, 해소).** 위 Evaluation Report Seam에 확정 — `_get_data_value`에
  consumer 인자를 추가하고 `state_condition` 분기에서 signal 1회 발행 + `report.passed` 반환.
- **F2 Choice 포트 번호(P1, 해소).** 위 Conditional Choice에 data 입력=i+1 / flow 출력=i로 고정.
- **F3 addon↔world_state 결합(P2, 수용).** `WorldStateConditionDef`/`state_condition` 평가가
  `ConditionSet`/`ConditionEvaluator`(전역 class_name)를 참조하므로 DialogueTool addon이
  `Assets/Script/gds/world_state/condition/`에 하드 의존하게 된다. 본 단일 게임 repo에서는 수용하고,
  DialogueTool 시스템 문서 Extension Rules에 의존성을 기록한다(별도 addon 배포는 비목표).
- **F4 runtime snapshot 보존 spike(P2).** `get_runtime_params()`가 `{"condition_set": <Resource>}`를
  반환해 untyped `runtime_nodes: Dictionary`에 중첩 저장된다. DT-007 spike는 typed
  `Array[ConditionClause]` 직접 export만 보장했으므로, Dictionary 2중 중첩 Resource 참조(external
  `ext_resource` + inline `sub_resource` 양쪽)의 `.tres` 왕복을 Step 2 착수 시 spike로 먼저 확인한다.
- **F5 select_choice 순서/invalid-index(P2).** 현재 `select_choice`는 포트 해석 전에
  `waiting_for=&"none"`/`selected_choice`를 먼저 커밋하고, 포트 미연결 시 대화를 종료한다. D6의
  "잘못된 visible index는 대기 유지" 정책과 충돌하므로 Step 4에서 (1) visible index를 mapping 범위로
  먼저 검증하고 (2) 통과 시에만 waiting/effects/Flow를 커밋하도록 재배치한다. mapping은
  `start_dialogue`/`_end_dialogue`/Choice 재진입에서 초기화한다.

## Step 1: Runtime State Condition Data Node

목표:
- 직접 구성한 runtime snapshot에서 `state_condition`이 strict/fail-closed 평가 결과를 boolean Data로 제공한다.

작업 범위:
- Definition/runtime params
- `DialoguePlayer._get_data_value()` state condition 처리
- `condition_evaluated` signal과 consumer context
- provider 미지정/invalid/missing/type mismatch 처리

제외 범위:
- 에디터 UI, Choice filtering

완료 조건:
- true/false ConditionSet이 정확한 bool을 반환한다.
- 구조 오류는 provider read 0, 모든 runtime 오류는 false이고 report가 1회 발행된다.
- 기존 Variable/Expression Data 평가가 변하지 않는다.

검증 방법:
- fake provider + 실제 `WorldStateStore` headless runtime test
- DT-007 Step 1~4와 DT-004 data/branch 회귀

선행 조건: Step 0 승인

### Step 1 구현 결과 (2026-06-15)

판정 대기: **코드 리뷰 요청**(자기 승인하지 않음). 1차 리뷰(미완료)의 P1/P2를 아래 "코드 리뷰 처리"에 반영했다.

**변경 파일**

- `addons/dialogtool/Resource/NodeDefinitions/Data/world_state_condition_def.gd` — 신규.
  `WorldStateConditionDef extends DataDefinition`. `@export var condition_set: ConditionSet`,
  `get_runtime_type()→&"state_condition"`, `get_runtime_params()→{"condition_set": condition_set}`.
  `_node_init`/`_capture`는 adapter graceful-degrade(Step 2에서 등록), `_get_data_output()→null`
  (런타임 평가는 provider가 필요하므로 Definition은 평가하지 않음).
- `addons/dialogtool/RunTime/dialogue_player.gd` — `condition_evaluated(condition_node_id,
  consumer_node_id, report)` signal 추가. `_get_data_value(node_id, consumer_node_id := -1,
  visited := [])`로 consumer context 전달. `state_condition` 분기 + `_evaluate_state_condition()`
  추가(원본 `_read_state_provider`를 `ConditionEvaluator.evaluate`에 직접 전달, signal 1회 발행,
  `report.passed` 반환). Branch는 consumer=branch id, expression 중첩 입력은 consumer=expression id로
  스레딩. `expression_node.gd`의 단일 인자 호출은 `-1` default로 무수정 유지.
- `addons/dialogtool/RunTime/tests/dt008_step1_state_condition_test.{gd,tscn}` — 신규 Step 1 매트릭스
  (fake provider + 실제 `WorldStateStore`).

**구현 내용 / 설계 판단**

- ADR-009 D1/D2/D3 그대로 구현. `state_condition`은 boolean Data 노드이고 Branch는 기존 Data 입력만
  소비한다. evaluator에는 DialoguePlayer facade(`has_state`)가 아니라 원본 `_read_state_provider`를
  전달해 provider 미지정이 `state_missing`이 아닌 `provider_missing`으로 fail-closed되게 했다.
- 손상된 snapshot(잘못된 타입/누락 `condition_set`)은 `is ConditionSet` 가드로 null로 좁혀 evaluator의
  `condition_set_null`로 fail-closed한다(크래시 없음).
- signal 발행과 `report.passed` 반환은 `_evaluate_state_condition` 한 곳에서만 수행한다(call site 중복
  발행 금지). report는 evaluator가 호출별 deep copy로 반환하므로 소비자가 변조해도 다음 평가에 영향 없다.
- `state_condition`은 Data 입력 포트가 없어 `visited` 순환 셋을 받지 않는다(Variable/Expression 순환
  방어 경로는 그대로). 에디터 build 미리보기 경로(`expression_node.gd`)는 consumer=-1로 호출돼도
  안전하다(provider 미지정 → false, 듣는 이 없는 signal 1회).

**검증 (headless)**

- `--import`: `WorldStateConditionDef`/`DialoguePlayer` 전역 클래스 등록, parse 오류 0(editor load 성공).
- `dt008_step1_state_condition_test`: **ALL PASS**(아래 14 사례, exit 0).
- 회귀: DT-007 `step1`(24)/`step2`(23)/`step3`(11)/`step4`(e2e) ALL PASS. DT-005 `step5`(provider seam,
  Variable/Expression Branch) ALL PASS. DT-004 `step1`/`step2`/`step3`/`step4-pipeline`/`step4-integration`
  ALL PASS.

**테스트 행렬 (dt008_step1_state_condition_test)**

| 사례 | 내용 | 결과 |
| --- | --- | --- |
| A | true/false ConditionSet → 정확한 bool, report passed/valid | pass |
| B | provider 미지정 → false, `provider_missing`, read_count 0 | pass |
| C | null set→`condition_set_null`, empty group→`group_empty`, 둘 다 false | pass |
| D | missing key → false, `state_missing`, read_count 1 | pass |
| E | actual type mismatch(String vs int expected) → false, `actual_type_mismatch` | pass |
| F | 구조 invalid → read_count 0 + fake provider has/read 호출 0(미접촉) | pass |
| G | 실제 `WorldStateStore`: default false → set_value 후 true | pass |
| H | Branch flow: 라우팅(TRUE/FALSE) + consumer==branch id(1), condition==2, event 1회 | pass |
| I | Expression 중첩: 상위 consumer 99 전달해도 consumer==expression id(4) | pass |
| J | 평가당 signal 정확히 1회 | pass |
| K | signal report.passed == 반환 bool(true/false) | pass |
| L | 반환 report 변조(passed/valid/read_count/errors) 후 재평가 불변 | pass |
| M | Variable→Branch, Expression→Branch 회귀 | pass |
| N | expression self-reference 순환 → null, 크래시 없음, condition event 0 | pass |
| O | signal listener의 동기 report 변조에도 반환값/Branch Flow 불변(P1 회귀) | pass |

**코드 리뷰 처리 (1차 리뷰, 2026-06-15)**

판정: **미완료** → 처리 후 재검증 ALL PASS.

- **[P1] signal listener가 분기 결과를 변조 — 수정.** GDScript는 signal로 Dictionary를 *참조 전달*하므로,
  발행 뒤 같은 `report`에서 `passed`를 읽으면 동기 listener의 `report["passed"]=true`가 반환값과 Branch
  Flow를 뒤집을 수 있었다(실측: 취약 코드에서 O.return_false=true, O.routed_false=TRUE로 false-green).
  `_evaluate_state_condition`에서 (1) `passed`를 발행 *전*에 캡처하고 (2) signal에는 `report.duplicate(true)`
  별도 deep copy를 넘기도록 고쳤다([dialogue_player.gd](../../addons/dialogtool/RunTime/dialogue_player.gd)).
  회귀 테스트 **O**를 추가했다(직접 `_get_data_value` 경로 + Branch flow 경로 모두 변조해도 false/FALSE 유지,
  변조 없는 baseline 대조 포함). 취약 코드로 되돌리면 O가 실패함을 확인해 회귀 가드 유효성을 검증했다.
  기존 L 테스트는 callback *종료 후* 변조라 이 경로를 못 잡았다(리뷰 지적 반영).

- **[P2] Step 2 이전 미완성 노드의 에디터 노출 — 문서화(Step 2에서 해소).**
  [dialogue_node_item_list.gd](../../addons/dialogtool/dialogue_node_item_list.gd)이 `NodeDefinitions/`
  하위 모든 `.gd`를 자동 발견하고 이름으로 `Start`/`Description`만 제외하므로, `Data/`에 둔
  `WorldStateConditionDef`("WorldStateCondition")가 즉시 노출된다. 현재는 Adapter 미등록이라 포트·
  ResourcePicker 없는 빈 노드로 표시되고, 드래그해 배치·저장하면 `condition_set=null` 노드가 생긴다.
  **데이터 손상은 없다**(런타임이 null→`condition_set_null`→false로 fail-closed). Step 2에서 GraphNode UI/
  ResourcePicker와 NodeTypeRegistry/Adapter 등록으로 정식 노드로 만들어 해소한다. 그 전까지는 빈 노드
  노출이 알려진 제약이다(P2, 데이터 위험 없음).

**남은 위험 / 다음 Step 입력**

- 에디터 GraphNode UI·ResourcePicker·Adapter/NodeTypeRegistry 등록과 `.tres` 왕복은 Step 2 범위다.
  F4 중첩 Resource snapshot 보존 spike는 Step 2 착수 시 선행한다. **P2 빈 노드 노출도 Step 2에서 해소한다.**
- 런타임 평가는 직접 구성한 snapshot으로만 검증했다. 실제 Manager/UI provider 주입 경로의 Branch
  end-to-end는 Step 3 범위다(DT-005 step5가 provider 전달 경로 자체는 이미 회귀로 보장).
- Choice 항목별 Data 입력 소비와 visible→original port mapping은 Step 4 범위다.

## Step 2: Editor Authoring and Resource Round-trip

목표:
- 에디터에서 State Condition 노드를 만들고 ConditionSet을 지정해 저장/재로드할 수 있다.

작업 범위:
- boolean output GraphNode UI
- ConditionSet Resource picker
- Editor Adapter/NodeTypeRegistry 등록
- Definition capture/apply와 runtime snapshot 생성

제외 범위:
- inline ConditionSet tree editor, schema-aware picker

완료 조건:
- 노드 목록에서 생성 가능하고 boolean-compatible port로 Branch/Choice Data 입력에 연결된다.
- external/subresource ConditionSet 참조와 node id/connection이 저장 -> cache 무시 재로드 후 보존된다.
- null ConditionSet도 저장 가능하지만 runtime에서 fail-closed된다.

검증 방법:
- **선행 spike(F4)**: `{"condition_set": ConditionSet}`를 `runtime_nodes` Dictionary에 중첩 저장한 뒤
  `CACHE_MODE_IGNORE` 재로드해 external/inline 양쪽에서 Resource 참조와 내부 트리가 보존되는지 확인.
- editor graph 생성 -> capture -> `.tres` 저장 -> 재로드 왕복 headless test
- Godot headless editor load

선행 조건: Step 1 리뷰 완료

### Step 2 구현 결과 (2026-06-15)

판정 대기: **코드 리뷰 요청**(자기 승인하지 않음). 1차 리뷰(미완료)의 P2 2건을 아래 "코드 리뷰 처리"에 반영했다.

**선행 spike(F4) 결과 — PASS, Design Deviation 없음**

제품 에디터 구현 전에 중첩 ConditionSet snapshot 왕복을 spike로 확인했다
(`dt008_step2_snapshot_spike`). `{"condition_set": <ConditionSet>}`를 `runtime_nodes`(untyped
Dictionary)에 2중 중첩 저장하고 `.tres` 저장 → `CACHE_MODE_IGNORE` 재로드했을 때:

- **external**(미리 `.tres`로 저장한 ConditionSet 참조): 그래프 저장 파일에 `ext_resource`로 경로가
  기록되고, 재로드 후 같은 트리를 가리킨다.
- **inline**(in-memory ConditionSet): `sub_resource`로 인라인되고, 재로드 후 트리(자식 순서/구체
  subtype/operator/expected typeof)가 보존된다.
- 재로드된 두 set 모두 실제 `ConditionEvaluator`에서 정상 평가된다(valid+passed).

DT-007 spike 범위(typed `Array[ConditionClause]` 직접 export) 밖의 Dictionary 2중 중첩 참조도 안전함을
확인했으므로 임의 확장 없이 Step 2를 진행했다.

**변경 파일**

- `addons/dialogtool/Node/world_state_condition_node.{gd,tscn}` — 신규. `WorldStateConditionNode extends
  DialogueNode`. boolean output 포트 + ConditionSet picker + Clear 버튼. `set_condition_set`/
  `get_condition_set`로 어댑터와 값 주고받기, picker 변경 시 deferred `_capture`.
- `addons/dialogtool/Editor/Adapter/condition_set_picker.gd` — 신규. LineEdit 기반 ConditionSet `.tres`
  드롭 picker(`portrait_texture_path_edit` 패턴). 단일 `.tres`이고 `ConditionSet`으로 로드되는 것만
  수락, 보관 Resource를 표시(external 경로 / `(inline ConditionSet)` / placeholder).
- `addons/dialogtool/Editor/Adapter/world_state_condition_editor_adapter.gd` — 신규. `apply_params`가
  slot 1에 boolean output을 두고 picker에 현재 set을 적용, `capture_params`가 picker의 set을 반환.
- `addons/dialogtool/Editor/Adapter/node_type_registry.gd` — `&"state_condition"` 어댑터 등록.
- `world_state_condition_def.gd` — `_get_dialogue_node()`가 전용 노드 씬을 반환, `_node_init`이 현재
  `condition_set`을 어댑터에 전달.
- `addons/dialogtool/RunTime/tests/dt008_step2_snapshot_spike.{gd,tscn}`,
  `dt008_step2_editor_roundtrip_test.{gd,tscn}` — 신규 spike + 에디터 왕복 테스트.

**구현 내용 / 설계 판단**

- 포트: state_condition output은 `port_type.boolean`이다. Branch 조건 입력(port 0)이 `boolean`이라 동일
  타입으로 연결되고(GraphEdit 기본 허용), editor.gd가 등록한 `data↔boolean` 교차 호환으로 Choice/Variable
  계열 data 입력에도 연결된다. 동일 타입 연결은 `is_valid_connection_type` 등록 목록과 무관하므로 테스트는
  실제 연결(capture conn)과 포트 타입 동일성으로 검증했다.
- 어댑터/picker로 에디터 UI 책임을 Definition 밖에 두는 기존 경계를 따랐다(Variable/Expression과 동일).
- picker는 외부 `.tres` 참조 중심이지만, 이미 지정된 inline ConditionSet 참조도 보관·표시한다(왕복 보존).
  inline ConditionSet **tree editor**는 Out of Scope.
- null ConditionSet도 저장 가능하고 런타임에서 `condition_set_null`로 fail-closed된다(완료 조건 충족).

**1차 리뷰 P2 해소**: 노드가 이제 boolean 포트 + picker로 정상 등록·렌더링되므로, Step 1에서 문서화한
"빈 노드 노출" 위험이 해소됐다(어댑터 등록 + 전용 씬).

**검증 (headless)**

- `--import`: `WorldStateConditionNode`/어댑터/registry parse 오류 0(editor load 성공).
- `dt008_step2_snapshot_spike`: **ALL PASS**(external/inline 왕복 + 재평가).
- `dt008_step2_editor_roundtrip_test`: **ALL PASS** — 실제 `dialoguetool_main.tscn` fixture(0 ERROR로
  깨끗하게 종료). boolean 포트 계약 + Branch 호환(A), 외부 참조 capture→save→재로드→재캡처 보존 +
  재로드 set 런타임 평가(B), inline ConditionSet 에디터 왕복(D), null 저장/재로드/런타임 fail-closed(C).
- 회귀: DT-008 step1(15), DT-004 step1~4, DT-005 step5, DT-007 step1~4 **ALL PASS**(합계 13 headless GREEN).

**코드 리뷰 처리 (1차 리뷰, 2026-06-15)**

판정: **미완료**(제품 코드 P0/P1 없음, 테스트 품질 P2 2건) → 처리 후 재검증 ALL PASS, 0 ERROR.

- **[P2] inline ConditionSet 실제 에디터 왕복 미검증 — 수정.** 기존 테스트는 외부 참조(B)/null(C)만,
  spike는 `runtime_nodes` 직접 구성만 다뤘다. **케이스 D**를 추가했다: in-memory ConditionSet을 가진
  Definition을 실제 그래프에 배치 → picker → `capture_current_graphedit` → `.tres` 저장(`sub_resource`로
  인라인 확인) → `CACHE_MODE_IGNORE` 재로드 → 새 에디터 `load_resource`(adapter apply) → 재캡처. inline
  set의 트리(key/operator/expected typeof), boolean 포트, node id, connection 보존과 재로드 set 런타임
  평가를 단언한다. 재로드된 inline set의 `resource_path`는 외부 `.tres`가 아니라 `graph.tres::SubResId`
  built-in 형태임을 확인한다(여전히 inline).
- **[P2] 에디터 테스트가 실제 ERROR를 내며 성공 — 수정.** bare `GraphEdit` fixture가 editor.gd의
  `@onready` 형제 UI(PathLabel/PopupMenu)를 갖지 못해 매 초기화마다 "Node not found" ERROR가 났다.
  `_make_editor`를 실제 `dialoguetool_main.tscn`을 instantiate해 그 안의 GraphEdit을 쓰도록 바꿨고,
  `_free_editor`로 메인 씬 루트를 통째로 정리한다. 재실행에서 **ERROR/Node-not-found 0건**으로 깨끗하게
  종료한다.

**남은 위험 / 다음 Step 입력**

- 실제 에디터 드래그-드롭/클릭 UX는 headless로 재현 불가하다. 테스트는 직렬화 backbone과 어댑터
  capture/apply 왕복(external/inline/null)만 보장한다(드롭 핸들러 로직은 정적 검토). 실제 마우스 드롭
  검증은 후속 수동 확인 대상.
- Branch end-to-end(실제 Manager/UI provider 주입 경로 + 상태 변경/restore 후 재실행)는 Step 3 범위다.
- Choice 항목별 Data 입력 소비와 visible→original port mapping은 Step 4 범위다.

## Step 3: Branch End-to-End Integration

목표:
- 실제 Manager/UI/Player provider 주입 경로에서 State Condition이 기존 Branch를 제어한다.

작업 범위:
- `Start -> Branch(State Condition) -> Say true/Say false -> End` 통합 그래프
- 실제 WorldState mutation/reset/snapshot restore 후 재실행
- report signal의 node/consumer/report 검증

제외 범위:
- Choice filtering

완료 조건:
- 상태 변경과 restore 뒤 Branch 결과가 실제 Store 최종값과 일치한다.
- provider 미지정/조건 오류는 false Flow이며 크래시/자동 true가 없다.
- 반복 실행과 dialogue 교체에서 이전 provider/report가 새 실행에 섞이지 않는다.

검증 방법:
- DialogueManager end-to-end headless test
- DT-004/005/006/007 회귀

선행 조건: Step 2 리뷰 완료

### Step 3 구현 결과 (2026-06-15)

판정 대기: **코드 리뷰 요청**(자기 승인하지 않음). 1차 리뷰(미완료)의 P1과 검증 우려를 아래 "코드 리뷰 처리"에 반영했다.

**제품 코드 변경 없음**

Step 3는 통합 검증 단계다(DT-007 Step 3와 동일 성격). 런타임은 이미 주입된 원본 `_read_state_provider`로
`state_condition`을 평가하고(Step 1), `DialogueManager.play(resource, provider)` →
`DialogueUI.play(resource, provider)` → `DialoguePlayer.set_read_state_provider` + deferred
`start_dialogue` 경로가 provider를 그대로 전달한다(DT-005 Step 5). 어느 쪽도 수정하지 않았다.

**변경 파일**

- `addons/dialogtool/RunTime/tests/dt008_step3_branch_e2e_test.{gd,tscn}` — 신규 e2e 테스트.

**구현 내용 / 설계 판단**

- 통합 그래프 `Start → Branch(state_condition) → Say "TRUE"/"FALSE" → End`를 runtime snapshot으로
  구성하고, 실제 `WorldStateStore`(bootstrap schema)를 provider로 `DialogueManager.play`에 주입한다.
  분기 결과는 Manager가 중계하는 `ui_request`의 첫 `display_text` say(TRUE/FALSE)로 관찰한다.
- `condition_evaluated`는 `DialogueManager._ui.dialogue_player`에 연결해 관찰한다. play()가 UI/player를
  동기 add_child로 만들고 실제 시작은 deferred이므로, play 직후 시작 전에 signal을 연결할 수 있다.
- 상태 변경(`set_value`/`reset_value`/`export+import_snapshot`)은 테스트가 Store native API로 수행하고,
  그 뒤 새 `play`가 변경된 값을 읽는지 확인한다(런타임은 mutation/signal에 의존하지 않음).

**검증 (headless) — ALL PASS**

| 사례 | 내용 | 결과 |
| --- | --- | --- |
| A | default(stage 0) → FALSE 분기, report.passed=false | pass |
| B | set_value(stage=5) → TRUE 분기, passed=true | pass |
| C | reset_value → default 복귀 → FALSE | pass |
| D | export(stage=5)→0으로 변경→import 복원 → Store 최종값과 일치(TRUE) | pass |
| E | provider 미지정 → FALSE(provider_missing, valid=false), 크래시/자동 true 없음 | pass |
| F | 미등록 key 조건 오류 → FALSE(state_missing) | pass |
| G | condition_evaluated: node=2(state_condition)/consumer=1(branch)/passed=true, say와 일치 | pass |
| H1 | 서로 다른 Store로 반복 실행 — 각 결과가 자기 Store와 일치 | pass |
| H2 | 같은 프레임 play 교체(latest-wins): 폐기 player 평가 0회·폐기 provider read 0회, 활성 player 평가 1회·passed=false·provider read>0, say=["FALSE"] | pass |

- 회귀: DT-004(step1~4), DT-005(step1~6), DT-006(step1~5), DT-007(step1~4), DT-008(step1~3) 전부
  **ALL PASS**(합계 24 headless GREEN). editor `--import` 0 오류.

**코드 리뷰 처리 (1차 리뷰, 2026-06-15)**

판정: **미완료**(제품 코드 P0/P1 없음, 테스트 강화 P1 + 검증 우려) → 처리 후 재검증 ALL PASS.

- **[P1] H2가 폐기된 조건 평가를 검출 못함 — 수정.** 기존 H2는 Manager가 중계한 `say==["FALSE"]`만
  확인했다. Manager의 source guard가 폐기 UI의 say를 숨기므로, 폐기된 첫 player가 실제로 조건을 평가해도
  테스트가 통과할 수 있었다. 첫(폐기)/최종(활성) **두 player의 `condition_evaluated`를 각각 수집**하고,
  read 횟수를 세는 `_CountingProvider`로 폐기 provider의 접근까지 단언하도록 강화했다:
  폐기 player 평가 0회(`H2.discarded_no_eval`), 폐기 provider read 0회(`H2.discarded_provider_untouched`),
  활성 player 평가 1회(`H2.active_one_eval`)·`passed==false`(`H2.active_passed_false`)·provider read>0
  (양성 대조 `H2.active_provider_read`), 두 player가 서로 다른 인스턴스(`H2.distinct_players`).
- **[검증 우려] 리뷰 환경에서 출력 없이 30초+ 미종료 — 조사·완화.** 동일 헤드리스(Godot 4.6.3)에서
  `--import` 선행 후 연속 4회 실행 모두 **1~2초에 ALL PASS, exit 0**으로 행을 재현하지 못했다. 원인은
  거의 확실히 Step 2에서 추가한 새 `class_name`(`WorldStateConditionNode` 등) 이후 `--import`를 건너뛰면
  부팅 시 블로킹 재임포트가 발생하는 환경 차이다(헤더와 메모리에 명시된 선행 조건). 완화로 테스트에
  **watchdog 타이머(30초)**를 추가해, await 기반 행이 생겨도 무한 대기하지 않고 진단 메시지를 출력한 뒤
  `quit(2)`로 종료하게 했다(정상 완료가 먼저 발화하므로 통과 경로에는 영향 없음). 헤더의 `--import` 선행
  지시도 유지한다.

**완료 조건 충족**

- 상태 변경/restore 뒤 Branch 결과가 실제 Store 최종값과 일치(B/C/D).
- provider 미지정/조건 오류는 false Flow이며 크래시/자동 true 없음(E/F).
- 반복 실행과 dialogue 교체에서 이전 provider/report가 새 실행에 섞이지 않음(H1/H2).

**남은 위험 / 다음 Step 입력**

- e2e는 runtime snapshot 그래프로 구동했다(에디터 저장 `.tres` 재생은 Step 2가 별도로 보장). 실제
  마우스/클릭 UI는 headless 재현 불가.
- Choice 항목별 Data 입력 소비와 visible→original port mapping, `select_choice` 재배치(F5)는 Step 4 범위다.

## Step 4: Conditional Choice Runtime Mapping

목표:
- Choice의 기존 항목별 Data 입력을 조건으로 사용하면서 표시 index와 원래 Flow port를 안전하게 연결한다.

작업 범위:
- Choice 진입 시 조건 평가와 visible list 구성(항목 i 조건 = `get_runtime_input_node_id(choice_id, i+1)`)
- visible index -> original output port mapping 수명 주기(`start_dialogue`/`_end_dialogue`/재진입 초기화)
- all-hidden, invalid index, invalid condition 정책
- **F5**: `select_choice` 재배치 — visible index를 mapping 범위로 먼저 검증, 통과 시에만
  waiting/`selected_choice`/effects/Flow 커밋. 범위 밖 index는 경고 후 대기 유지(Flow 불변).

제외 범위:
- Choice 에디터 UI 구조 변경

완료 조건:
- 중간 항목이 숨겨져도 사용자가 고른 visible index가 올바른 원래 Flow로 진행한다.
- no-input legacy Choice는 이전과 동일하다.
- 평가 시점 이후 상태가 바뀌어도 현재 목록/mapping이 일관되고 재진입 때만 갱신된다.
- invalid/error 조건은 숨김, all-hidden은 명시적 종료, 잘못된 index는 대기 유지다.

검증 방법:
- 첫/중간/마지막 숨김, 복수 숨김, 전부 숨김, 상태 변경 중 대기, 재진입 test

선행 조건: Step 3 리뷰 완료

### Step 4 구현 결과 (2026-06-15)

판정: **수정 후 완료**(2026-06-15 코드 리뷰). 1차 리뷰(미완료)의 P1을 아래 "코드 리뷰 처리"에서 수정하고
재검증 ALL PASS로 통과했다. 남은 P3(Current-State 판정 stale/중복 정리)는 처리했고, 나머지 문서 완료
갱신(Open-Tasks/System/User Guide/Review)은 Step 5 범위다.

**변경 파일**

- `addons/dialogtool/RunTime/dialogue_player.gd` — Choice 런타임 조건 평가 + visible→original mapping.
- `addons/dialogtool/RunTime/tests/dt008_step4_conditional_choice_test.{gd,tscn}` — 신규(11 시나리오).

**구현 내용 / 설계 판단**

- `_choice_visible_map: Array` 필드 추가(visible_index → 원래 항목 index = 원래 flow 출력 port).
  `start_dialogue`/`_end_dialogue`에서 `[]`로 초기화하고, `_execute_choice`가 진입마다 새로 구성한다(재진입 갱신).
- `_execute_choice`: 항목 i의 조건 노드 = `get_runtime_input_node_id(choice_id, i+1)`(포트 계약 F2).
  cond_id가 -1이면 unconditional(항상 표시, 레거시 호환), 아니면 `_to_bool(_get_data_value(cond_id, choice_id))`로
  평가해 true인 항목만 visible list에 넣고 `visible_map`에 원래 index를 기록한다. consumer는 choice id다
  (state_condition signal에 choice id 전달). 조건은 진입 시 1회만 평가하고 대기 중 재평가하지 않는다.
  모든 항목이 숨겨지면 기존 empty-choice와 같은 종료 정책(warning + `_end_dialogue`).
- `select_choice(visible_index)` 재배치(F5): visible index를 `_choice_visible_map` 범위로 **먼저** 검증하고,
  범위 밖이면 경고 후 `waiting_for`/`selected_choice`/effects/Flow를 전혀 건드리지 않고 대기를 유지한다.
  통과 시에만 `original_port = _choice_visible_map[visible_index]`로 되돌려 effects 실행 + 원래 Flow로 진행한다.
- 하위 호환: no-input Choice는 identity map이라 `select_choice(i)`가 이전과 동일. `dialogue_ui.gd`의 버튼
  index가 곧 visible index이므로 UI 변경 불필요(에디터 구조 변경 없음 — Out of Scope 준수).

**검증 (headless) — ALL PASS (11 시나리오)**

| 사례 | 내용 | 결과 |
| --- | --- | --- |
| A | 첫 항목 숨김 → offer [B,C], select0→FLOW_1, select1→FLOW_2 | pass |
| B | 중간 항목 숨김(핵심) → offer [A,C], **select1→FLOW_2**(원래 port 2, FLOW_1 아님) | pass |
| C | 마지막 항목 숨김 → offer [A,B], select1→FLOW_1 | pass |
| D | 복수 숨김 → offer [B], select0→FLOW_1 | pass |
| E | 전부 숨김 → offer 없음, 명시적 종료(ended, waiting none) | pass |
| F | no-input 레거시 → offer [A,B,C] identity, select2→FLOW_2 | pass |
| G | 잘못된 index(5/-1) → 대기 유지(Flow/say/ended 불변), 이후 유효 index 정상 | pass |
| H | error 조건(missing key) → 숨김, signal fail-closed | pass |
| I | 대기 중 상태 변경 → 목록/mapping 고정(frozen), 선택은 snapshot 따름 | pass |
| J | 재진입(루프백) → 변경된 상태로 재평가(offer 2회: [LOOP,COND]→[LOOP]) | pass |
| K | condition_evaluated consumer == choice id(1), node == cond id | pass |
| L | error-dominance가 Expression 통해 전파(P1): missing→`not c`→Choice 숨김(L1), missing→`c or true`→Branch false(L2), valid-false→`not c`→true 허용(L3), 직접 invalid→Branch false(L4) | pass |

- 회귀: DT-004(step1~4, **Choice 경로 포함**), DT-005(step5/6), DT-007(step3/4), DT-008(step1~4)
  **ALL PASS**. editor `--import` 0 오류. DT-004 `select_choice(0)` 레거시 호출은 identity map으로 무영향.

**코드 리뷰 처리 (1차 리뷰, 2026-06-15)**

판정: **미완료**(P1 설계 결함) → 처리 후 재검증 ALL PASS.

- **[P1] Expression이 조건 오류를 true로 뒤집을 수 있음 — 수정.** state_condition이 invalid report도 단순
  `false`로 반환하면, 이를 입력으로 받는 Expression(`not c`/`c or true`)이 오류 조건을 true로 만들어
  Choice 노출/Branch true가 될 수 있었다(ADR-008 error-dominance / ADR-009 fail-closed 위반). 내부 Data
  평가를 `{value, errored}`로 전파하도록 리팩터했다([dialogue_player.gd](../../addons/dialogtool/RunTime/dialogue_player.gd)):
  - `_eval_data(node_id, consumer, visited) -> {value, errored}` 신설(기존 `_get_data_value`는 `.value`만
    돌려주는 호환 래퍼로 유지 — 에디터 미리보기 `expression_node.gd` 무수정).
  - state_condition: `errored = not report.valid`(invalid report는 errored). 정상이지만 논리상 false인
    조건은 valid=true → errored=false(이 false는 Expression이 정상적으로 다룰 수 있음).
  - Expression: 입력 중 하나라도 errored이거나 빈 식/parse/execute 실패면 결과 errored=true 전파.
  - 순환/미상 노드/빈 노드도 errored=true(구조 오류 fail-closed).
  - Branch/Choice 소비자: `errored`면 `_to_bool` 이전에 무조건 false/숨김으로 단락.
  - 회귀 테스트 **L1~L4** 추가(리뷰 필수 4건 그대로). 취약 버전(expression이 input errored 무시)으로
    되돌리면 L1=`["A","B"]`/L2=`TRUE`로 실패함을 확인해 가드 유효성을 검증했다. 기존 11개 시나리오와
    DT-004/005/007 회귀는 영향 없음(valid-false 경로 L3로 비회귀 확인).

**완료 조건 충족**

- 중간 항목 숨김에도 visible index가 올바른 원래 Flow로 진행(B/A/C/D).
- no-input 레거시 Choice 동일(F).
- 평가 후 상태 변경에도 현재 목록/mapping 고정, 재진입에서만 갱신(I/J).
- invalid/error 조건 숨김(H), all-hidden 명시 종료(E), 잘못된 index 대기 유지(G).

**남은 위험 / 다음 Step 입력**

- runtime snapshot으로 검증했다. 에디터 연결(State Condition boolean output ↔ Choice 항목별 Data 입력)의
  저장/재로드/resize 보존과 Branch+conditional Choice 복합 e2e, 문서 완료 갱신은 Step 5 범위다.

## Step 5: Conditional Choice Editor and Completion Review

목표:
- 에디터 연결부터 runtime 선택까지 조건부 Choice 전체 흐름을 완료 판정한다.

작업 범위:
- 기존 Choice 항목별 Data 입력과 State Condition boolean output 연결 UX 검증
- Choice resize/삭제/저장/재로드에서 조건 연결과 Flow port 보존
- Branch + conditional Choice 복합 RPG dialogue e2e
- System/User Guide/Current-State/Open-Tasks/Review 갱신

완료 조건:
- 조건 연결이 저장/재로드 후 동일하며 resize가 남은 항목의 condition/Flow 연결을 잘못 재배치하지 않는다.
- 실제 Store 상태에 따라 Branch와 Choice가 같은 evaluator 계약으로 동작한다.
- P0/P1 없음, headless editor load 및 DT-004~007 전체 회귀 성공.

검증 방법:
- editor round-trip + runtime e2e + 전체 회귀 matrix

선행 조건: Step 4 리뷰 완료

### Step 5 구현 결과 (2026-06-15)

판정: **Approved after design fixes**(2026-06-15 완료 판정 코드 리뷰). 제품 코드 P0/P1 없음 — Choice 포트
보존/fail-closed/visible→original mapping이 설계와 일치함을 확인받았다. 리뷰 P2(현재-사실 문서 3곳이
DT-008 완료와 모순)는 처리했다: [[Current-State]] DT-006 절의 미구현 목록, [[World-State-System]] Agent
Brief, [[World-State-User-Guide]] §15에서 "State Condition 노드 없음" 표기를 완료 사실로 갱신했다.
제품 코드 변경 없음(검증 + 문서 단계).

**변경 파일**

- `addons/dialogtool/RunTime/tests/dt008_step5_completion_test.{gd,tscn}` — 신규(에디터 조건 연결
  round-trip + resize 보존 + 복합 Branch/Choice e2e).
- 문서: 본 Task Step 5, [[Current-State]], [[Open-Tasks]], [[DialogueTool]], [[World-State-System]],
  [[DialogueTool-User-Guide]], [[DT-008-Choice-Integration-Review]](신규).

**검증 (headless) — ALL PASS, 0 ERROR**

실제 `dialoguetool_main.tscn` fixture로 editor.gd를 띄워(@onready 형제 UI 완비) 검증했다.

- **A 에디터 조건 연결 + resize 보존(Design Risk 1/2 해소)**:
  - State Condition output(boolean) → Choice 항목별 Data 입력(port i+1) 연결이 capture에 보존되고,
    포트 타입 호환(cond out=boolean / choice in=data)을 확인.
  - 저장 → `CACHE_MODE_IGNORE` 재로드 → 새 에디터 load → 재캡처에서 조건/Flow 연결 동일.
  - `update_item(2)`로 3→2 항목 resize: 항목0 조건(to_port 1)과 항목0/1 flow(from_port 0/1)는 보존,
    항목2의 조건(to_port 3)·flow(from_port 2)는 드롭, 남은 항목0 조건이 다른 항목으로 **재배치되지 않음**
    (`A.resize_no_misroute`). resize 후 저장/재로드도 보존.
- **B 복합 Branch + conditional Choice e2e(실제 `WorldStateStore` 주입)**:
  - `Start → Branch(quest.main.stage>=3) → [true] Choice["always", "cond"(affinity>=10)] / [false] Say`.
  - default(stage 0) → Branch false → LOW_STAGE(B1). stage=5·affinity=0 → Branch true → Choice에서 cond
    숨김 → ["always"], select0 → CHOSE_ALWAYS(B2). stage=5·affinity=10 → ["always","cond"], select1 →
    CHOSE_COND(B3). reset로 stage 복귀 → 다시 false flow(B4). Branch와 Choice가 같은 evaluator/Store
    계약으로 일관 동작.

- 전체 회귀 **26/26 GREEN**: DT-004(step1~4)·DT-005(step1~6)·DT-006(step1~5)·DT-007(step1~4)·
  DT-008(step1~5) ALL PASS, editor `--import` 0 오류.

**완료 조건 충족**

- 조건 연결이 저장/재로드 후 동일하며 resize가 남은 항목의 condition/Flow 연결을 잘못 재배치하지 않음(A).
- 실제 Store 상태에 따라 Branch와 Choice가 같은 evaluator 계약으로 동작(B).
- P0/P1 없음, headless editor load 및 DT-004~007 전체 회귀 성공.

**남은 위험**

- 실제 마우스 드래그-드롭/슬라이더 클릭 UX는 headless 재현 불가(연결/resize는 `connect_node`/`update_item`
  API 경로로 검증, 직렬화 backbone 보장). 실제 클릭 검증은 후속 수동 확인.
- 본 Task 범위 밖 후속: State Set/Add Effect와 mutation provider, schema-aware key/operator picker,
  조건 trace inspector UI, disabled-choice + reason UI, inline ConditionSet tree editor.

### 완료 요약

Step 0~5를 모두 구현·검증했다. State Condition Data 노드(`state_condition`)가 boolean Data로 Branch와
조건부 Choice를 같은 `ConditionSet`/`ConditionEvaluator` 계약으로 제어하며, fail-closed(error-dominance
포함)·visible→original port mapping·signal seam·저장/재로드/resize 보존이 헤드리스로 검증됐다. 각 Step
리뷰 판정은 Step 1~4 **수정 후 완료**, Step 5는 완료 판정 리뷰 대기다. 상세는 [[DT-008-Choice-Integration-Review]].

## Design Risks

1. Choice 필터링 후 visible index를 그대로 output port로 쓰면 다른 응답 Flow가 실행된다.
2. Choice dynamic rebuild가 Data/Flow 연결을 분리 보존하지 못하면 저장 데이터가 조용히 재배치될 수 있다.
3. DialoguePlayer facade를 evaluator provider로 넘기면 provider 미지정이 `state_missing`으로 오분류될 수 있다.
4. 조건 report signal의 consumer context가 없으면 같은 Condition 노드 재사용 시 어느 Branch/Choice 평가인지 알기 어렵다.
5. Choice 대기 중 조건을 재평가하면 UI 목록과 선택 index mapping이 서로 다른 시점의 상태를 볼 수 있다.

## Related

- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[DT-007-ConditionSet-ConditionEvaluator]]
- [[DT-007-Condition-Review]]
- [[ADR-008-Structured-Condition-Evaluation]]
- [[DialogueTool]]
- [[World-State-System]]
