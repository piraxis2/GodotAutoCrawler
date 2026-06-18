---
id: DT-012
type: task
status: completed
system: DialogueTool, WorldState
created: 2026-06-16
updated: 2026-06-16
tags: [task, dialogue, world-state, condition, editor, ux]
---

# Condition Authoring UX

## Goal

DialogueTool 그래프에서 `WorldStateCondition` 노드가 어떤 조건을 의미하는지 바로 읽을 수 있게 한다.
현재 노드는 `ConditionSet` `.tres` 경로 또는 `(inline ConditionSet)`만 보여 주기 때문에, 제작자가 Branch와
Choice 조건을 확인하려면 리소스를 따로 열어야 한다.

최종 목표는 수십~수백 개 상태 조건이 섞인 대화 그래프에서도 조건 의미가 그래프 위에서 드러나게 하는 것이다.

## Context

- [[DT-007-ConditionSet-ConditionEvaluator]]는 `ConditionSet`/`StateCondition`/`ConditionGroup` 데이터 모델과
  strict evaluator를 완료했다.
- [[DT-008-State-Condition-Dialogue-Integration]]은 `WorldStateConditionDef` Data 노드와 에디터 picker를
  완료했다.
- 현재 에디터 구조:
  - `addons/dialogtool/Resource/NodeDefinitions/Data/world_state_condition_def.gd`
  - `addons/dialogtool/Node/world_state_condition_node.gd(.tscn)`
  - `addons/dialogtool/Editor/Adapter/world_state_condition_editor_adapter.gd`
  - `addons/dialogtool/Editor/Adapter/condition_set_picker.gd`
- 현재 picker는 유효한 `ConditionSet` `.tres`를 드롭하면 resource path를 표시하고, inline set이면
  `(inline ConditionSet)`만 표시한다.
- 실제 사용자가 불편한 점: 노드 목록에는 `WorldStateCondition`이 보이지만, 그래프 위 노드만 봐서는
  `actor.example.affinity >= 10`인지 `quest.main.stage == 3`인지 알 수 없다.

## User Outcome

- 제작자가 `WorldStateCondition` 노드를 봤을 때 조건의 의미를 즉시 파악한다.
- `ConditionSet.description`이 있으면 사람이 쓴 설명을 우선 표시한다.
- description이 없으면 leaf/group 트리에서 안정적인 자동 요약을 만든다.
- invalid/null 조건은 그래프 위에서 명확히 보인다.
- 런타임 조건 평가, 저장 포맷, provider 계약은 바뀌지 않는다.

예상 표시(예시 — 실제 노드 title은 기존 `WorldStateCondition`을 유지할 수 있음):

```text
State Condition
actor.example.affinity >= 10

State Condition
ALL(quest.main.stage >= 3, actor.example.affinity >= 10)

State Condition
No ConditionSet

State Condition
Invalid: root_null
```

## Scope

### Included

- `ConditionSet`을 사람이 읽을 수 있는 짧은 문자열로 요약하는 editor/helper API.
- `StateCondition` leaf 요약:
  - key
  - operator symbol/string
  - expected literal
  - literal type 구분(String vs StringName, int vs float 등).
- `ConditionGroup` 요약:
  - `ALL(...)`, `ANY(...)`, `NOT(...)`
  - 긴 조건은 안전하게 잘라서 표시하고 full text는 tooltip 또는 별도 필드로 제공.
- `WorldStateConditionNode`에 summary/invalid state 표시.
- `ConditionSet.description` 우선 표시 정책 검토.
- null/invalid/cycle/alias/depth/node-limit 같은 structural reject 상태의 표시 정책.
- 에디터 저장/재로드 왕복 테스트.
- User Guide 갱신.

### Out of Scope

- inline ConditionSet tree editor.
- schema-aware key picker.
- operator/value 편집 UI.
- 조건 trace inspector UI.
- disabled-choice reason UI.
- 런타임 evaluator/report 계약 변경.
- `ConditionSet` 저장 포맷 변경.

## Step 0 Design Review — 완료 (Approved after design fixes)

Step 0 설계 리뷰(2026-06-16)에서 실제 코드 구조와 대조해 아래 계약을 확정했다. 제품 코드는 수정하지 않았다.

### 확정 결정

1. **Formatter 위치.** `condition_summary.gd` 같은 provider-free helper를 `addons/dialogtool/world_state/condition/`
   아래에 둔다. UI 위젯(`condition_set_picker.gd`, `WorldStateConditionNode`)은 표시만 담당하고 포맷 로직을
   소유하지 않는다.

2. **validate-first 안전 계약.** 요약기는 먼저 `ConditionValidator.validate(condition_set)`를 호출한다.
   - null/invalid면 트리를 포맷하지 않고 `No ConditionSet` 또는 `Invalid: <대표 code>`를 반환한다.
   - valid일 때만 트리를 순회해 summary를 만든다.
   - 따라서 cycle/alias/depth/node-limit/group_empty/not_arity_invalid 같은 구조 문제는 validator의
     iterative traversal 불변식에 맡기고, 요약기 자체가 손상 트리를 naive recursion으로 순회하지 않는다.

3. **invalid/null 우선 표시.** `ConditionSet.description`이 있어도 structural invalid/null 상태는 항상
   그래프 위에 invalid/null로 표시한다. description-first가 깨진 조건을 정상처럼 보이게 해서는 안 된다.
   full tooltip에는 description과 error code를 병기할 수 있다.

4. **description 우선순위.** structural valid일 때만 `ConditionSet.description`이 노드 첫 줄/주요 summary로
   우선 표시된다. description이 비어 있으면 자동 구조 요약을 표시한다. tooltip/full text는 구조 요약을
   함께 제공한다.

5. **표시 문자열과 trace 계약 분리.** summary 문자열은 editor UX용 표시이며 ADR-008 trace 안정 계약이 아니다.
   `StateCondition.operator_to_string()`/`ConditionGroup.logic_to_string()` trace 문자열을 그대로 UI 표시로
   재사용하지 않는다. summary 전용 operator 기호 맵을 둔다: `==`, `!=`, `<`, `<=`, `>`, `>=`.

6. **literal 표기.** strict typeof 차이를 숨기지 않는다.
   - INT `10`과 FLOAT `10.0`은 다르게 보인다. FLOAT는 정수처럼 보이는 값도 소수점 표기를 강제한다.
   - String과 StringName은 구분되게 표기한다.
   - bool은 `true`/`false`, null/unsupported literal은 invalid 경로로 표시한다.

7. **긴 요약 overflow.** 노드 summary는 제한된 한두 줄로 자르고 말줄임을 붙인다. full summary와 full errors는
   tooltip로 제공한다. `WorldStateConditionNode`는 summary label에 max width/autowrap/clipping 정책을 둬
   `_process()`의 `get_combined_minimum_size()` 때문에 노드 폭이 무한히 커지지 않게 한다.

8. **갱신 시점.** MVP는 live external resource edit 구독을 하지 않는다. summary는 drop/clear/adapter apply/
   editor load/reload 시점에 갱신한다. 외부 `.tres`나 description을 Inspector에서 바꾼 뒤의 즉시 live 갱신은
   범위 밖이며, 사용자는 리로드/재적용으로 반영한다.

9. **path 보존.** 현재 picker가 보여 주는 external `.tres` path는 참조 정체성 확인에 유용하므로 버리지 않는다.
   권장 UI는 전용 summary label 추가 + picker path 유지 + tooltip에 full summary/path/errors 제공이다.

10. **title 정책.** 코드/리소스 호환을 위해 class/runtime type은 유지한다. 노드 title을 `State Condition`으로
    바꾸는 것은 UX 개선으로 허용하되, 이번 Step의 핵심 완료 조건은 summary/invalid 표시다. title 변경이
    저장 호환이나 테스트를 키우면 별도 작은 변경으로 분리한다.

### 리뷰에서 해소한 문제

- [P1] description-first가 invalid를 가리는 문제 → invalid/null 우선 표시로 해소.
- [P1] 요약기 naive recursion 위험 → validate-first 계약으로 해소.
- [P2] int/float, String/StringName 표기 손실 → literal 표기 규칙으로 해소.
- [P2] 빈 group/잘못된 NOT가 정상 요약처럼 보이는 문제 → structural invalid는 구조 요약 대신 invalid 배지.
- [P2] trace 문자열과 표시 문자열 결합 위험 → summary는 안정 trace 계약이 아님을 명시.
- [P2] live external edit 미갱신 → MVP 갱신 시점 한정.
- [P2] 긴 summary 폭주 → clipping/autowrap/tooltip 정책 확정.
- [P3] title/path 레이아웃 모호성 → title은 선택, path는 유지, summary label 추가 권장.

판정: **Approved after design fixes**. 위 수정 사항을 Task에 반영했으므로 Step 1 구현으로 진행 가능하다.

## Steps

### Step 0: Design Review

목표:
- formatter 책임, 표시 정책, invalid 표시, 테스트 범위를 확정한다.

작업 범위:
- 제품 코드 수정 없음.
- 실제 코드 구조(`WorldStateConditionNode`, picker, adapter, `ConditionValidator`)와 대조.

완료 조건:
- formatter 위치와 public/static API 확정.
- description/auto summary/invalid/null 표시 정책 확정.
- Step 1~3 구현 범위와 제외 범위 확정.
- Approved / Approved after design fixes / Rework required 판정.

검증 방법:
- 코드 대조 리뷰.

결과:
- 완료. 판정: Approved after design fixes. 위 "Step 0 Design Review" 계약을 Step 1~3의 선행 조건으로 삼는다.

### Step 1: Condition Summary Formatter

목표:
- `ConditionSet`/`ConditionClause`를 provider 없이 안정적인 문자열로 요약한다.

작업 범위:
- formatter/helper 추가.
- leaf/group/null/invalid/long tree 포맷.
- strict literal 표시(String/StringName, int/float 구분).
- summary 표시 문자열은 trace 안정 문자열과 분리한다.
- provider를 읽지 않고 structural validation만 사용한다.

완료 조건:
- leaf: `actor.example.affinity >= 10` 같은 요약 생성.
- group: `ALL(...)`, `ANY(...)`, `NOT(...)` 요약 생성.
- invalid/null은 명시적 summary와 error code 제공.
- provider read 0.
- formatter는 validate-first로 동작한다. deep/cyclic/aliased/empty group/invalid NOT/depth/node-limit
  resource는 트리 포맷을 시도하지 않고 structural invalid로 표시한다.
- INT/FLOAT와 String/StringName 표기가 구분된다.
- 긴 summary는 configured limit 안에서 잘리고 full text는 별도로 반환된다.

검증 방법:
- headless unit test.
- DT-007 validator/evaluator 회귀.

선행 조건:
- Step 0 승인.

결과:
- 구현 완료 — 리뷰 대기. provider-free helper `ConditionSummary`
  (`addons/dialogtool/world_state/condition/condition_summary.gd`)를 추가했다.
  - public/static `ConditionSummary.summarize(condition_set, options := {}) -> Dictionary`,
    반환 `{ valid, summary, full_summary, tooltip, error_codes, errors }`.
  - validate-first: 먼저 `ConditionValidator.validate(condition_set)`를 호출하고, null/invalid면
    트리를 순회하지 않는다. `condition_set == null` → `No ConditionSet`,
    structural invalid → `Invalid: <대표 code>`(error_codes[0]).
  - valid일 때만 bounded recursion으로 트리 요약. leaf = `key <op> literal`, group =
    `ALL(...)`/`ANY(...)`/`NOT(...)`(children 순서 보존).
  - 표시 전용 operator 기호 맵(`==`,`!=`,`<`,`<=`,`>`,`>=`)/logic 라벨 맵을 두어 ADR-008 trace
    문자열(`greater_equal`/`all` 등)을 UI 표시로 재사용하지 않는다.
  - literal: INT `10` vs FLOAT `10.0`(소수점 강제), String `"calm"` vs StringName `&"calm"`,
    bool `true`/`false` 구분.
  - String/StringName literal은 `_escape_string`으로 `\`, `"`, `\n`, `\r`, `\t`를 escape해
    따옴표/제어문자가 들어가도 summary가 모호하거나 여러 줄로 깨지지 않는다(Step 1 코드 리뷰 P2 수정,
    Step 2에서 GraphNode label/tooltip에 그대로 올라가는 것을 대비). 백슬래시를 먼저 치환해 이중 escape 회피.
  - 긴 summary는 `max_length`(기본 80, options로 override)로 잘리고 ellipsis(`…`)를 붙이며,
    `full_summary`는 잘리지 않은 전체를 보존한다.
  - description: structural valid + description 있을 때만 `summary`가 description(우선),
    `full_summary`는 구조 요약, `tooltip`은 description+구조 병기. invalid/null은 description과
    무관하게 invalid/null 우선.
  - provider read 0(provider를 알지 않음).
- 검증: 신규 `addons/dialogtool/world_state/condition/tests/dt012_step1_condition_summary_test`
  (14 시나리오, 완료 조건 1~12 전부 + operator 기호≠trace + literal escaping(quote/newline/backslash/
  tab/cr) 회귀) ALL PASS. DT-007 step1/step2 회귀 ALL PASS. `--import` 0 parse 에러
  (`ConditionSummary` 전역 클래스 등록 확인).
- 코드 리뷰: 판정 **수정 후 완료**. P0/P1 없음. P2(String/StringName escaping 누락)는 위
  `_escape_string` + case 14 테스트로 해소.
- UI 표시(`WorldStateConditionNode` summary label/tooltip)는 Step 2 범위로 분리.

### Step 2: WorldStateCondition Node Display

목표:
- Dialogue editor GraphNode에서 조건 의미를 직접 보여 준다.

작업 범위:
- `WorldStateConditionNode` UI에 summary label/tooltip 추가.
- picker 변경, clear, adapter apply/load 뒤 summary 갱신.
- structural valid일 때만 description 우선 표시 정책 반영.
- external path는 picker에 유지하고, summary는 별도 label로 표시한다.
- live external resource edit 구독은 하지 않는다(drop/clear/apply/load/reload 시점 갱신).

완료 조건:
- external `.tres` ConditionSet 드롭 시 path뿐 아니라 readable summary 표시.
- inline ConditionSet도 summary 표시.
- null/invalid 상태가 description 유무와 무관하게 그래프 위에서 구분된다.
- 저장→재로드→에디터 load 후 summary가 동일하게 표시된다.
- 기존 `condition_set` capture/runtime params 보존.
- 긴 summary가 노드 폭을 과도하게 키우지 않고 tooltip/full text로 확인 가능하다.

검증 방법:
- 실제 `dialoguetool_main.tscn` fixture 기반 editor round-trip test.
- DT-008 Step 2/5 회귀.
- Godot headless import.

선행 조건:
- Step 1 리뷰 완료.

결과:
- 구현·리뷰 완료(판정: 완료). `WorldStateConditionNode`에 전용 summary label을 추가하고 Step 1
  `ConditionSummary`를 표시에 연결했다.
  - [world_state_condition_node.tscn](addons/dialogtool/Node/world_state_condition_node.tscn):
    GraphNode 직속 자식으로 `SummaryLabel`(Label) 추가. `clip_text=true`,
    `text_overrun_behavior=3`(ellipsis), `custom_minimum_size=(240,0)`로 노드 폭 폭주 방지.
    delete_button(slot 0)·HBoxContainer(slot 1, boolean output 유지) 뒤 slot 2라 boolean 포트
    인덱스 회귀 없음.
  - [world_state_condition_node.gd](addons/dialogtool/Node/world_state_condition_node.gd):
    `_refresh_summary()`가 `ConditionSummary.summarize(picker.condition_set)`로 label.text=요약,
    tooltip=full summary(+외부 `.tres` path 병기), invalid/null은 `modulate`를 빨강 계열로 구분.
    갱신 시점은 `set_condition_set`(adapter apply/load), picker drop(`_on_picker_changed`),
    clear(`_on_clear_pressed`) — live external edit 구독 없음(Step 0 D8). picker는 path 유지,
    summary는 별도 label(Step 0 D9). capture/runtime params·adapter 미변경.
- 검증: 신규 `addons/dialogtool/RunTime/tests/dt012_step2_node_display_test`(A~E, 실제
  `dialoguetool_main.tscn` fixture) ALL PASS — external 요약+path, inline 요약, description 우선(valid),
  null `No ConditionSet`+invalid 색, invalid(`root_null`)는 description 무관하게 우선, 긴 요약
  label 잘림+tooltip full+노드 폭 제한, 저장→재로드 summary 동일+condition_set/connection/boolean(port 0)
  보존. 회귀 DT-008 step2/step3/step5 + DT-012 step1 ALL PASS, `--import` 0 parse 에러.
- 문서(User Guide) 갱신은 Step 3 범위.

### Step 3: Docs and Completion Review

목표:
- 사용자가 조건 표시 UX를 이해하고, 남은 한계를 명확히 알 수 있게 문서화한다.

작업 범위:
- [[DialogueTool-User-Guide]] State Condition 절 갱신.
- [[DialogueTool]] 시스템 문서 현재 사실 갱신.
- 필요하면 Review 문서 생성.

완료 조건:
- User Guide가 summary/description/tooltip/invalid 표시를 설명한다.
- Open Tasks에서 inline editor/schema picker/trace inspector를 후속으로 유지한다.
- DT-004/007/008/010 관련 회귀와 editor import가 통과한다.

검증 방법:
- 문서 링크/경로 확인.
- 관련 headless 회귀.

선행 조건:
- Step 2 리뷰 완료.

결과:
- 구현·리뷰 완료(판정: 완료). 문서를 현재 동작에 맞춰 갱신했다.
  - [[DialogueTool-User-Guide]] §6 State Condition에 "그래프 위 조건 요약 표시 (DT-012)" 절 추가:
    자동 요약(leaf/group, 표시 기호 vs trace 분리), literal 표기 구분(INT/FLOAT·String/StringName·bool,
    문자열 escape), description 우선(valid 한정), null/invalid 빨강 구분, tooltip(full/path/오류), 갱신
    시점(drop/clear/load), inline editor·schema picker는 후속.
  - [[DialogueTool]] State Condition 절에 `ConditionSummary` validate-first 요약 표시 사실 추가.
- 회귀: DT-004(5)+DT-007(4)+DT-008(5)+DT-010(3)+DT-012(step1+step2) **19/19 scene ALL PASS**,
  `--import` 0 parse 에러. Open Tasks Later에 inline ConditionSet tree editor·schema-aware picker·
  condition trace inspector를 후속으로 유지.

#### 완료 리뷰 결과 (2026-06-17)

판정: **완료**([[DT-012-Condition-Authoring-UX-Review]]).

P0/P1/P2 발견 사항 없음. `ConditionSummary`의 validate-first/provider-free 요약 정책,
`WorldStateConditionNode`의 SummaryLabel/tooltip/invalid 표시, User Guide와 시스템 문서 설명을 실제 구현과
대조했다. 문서는 구현과 일치하며, Open Tasks의 Next에서는 DT-012를 제거하고 후속 UX 항목만 Later에 유지한다.

재검증:
- Godot 4.6.3 mono headless `--import`: exit 0, parse/class error 없음.
- 선택 회귀 7/7 PASS:
  - `addons/dialogtool/world_state/condition/tests/dt012_step1_condition_summary_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt012_step2_node_display_test.tscn`
  - `addons/dialogtool/world_state/condition/tests/dt007_step1_validation_test.tscn`
  - `addons/dialogtool/world_state/condition/tests/dt007_step2_evaluator_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt008_step2_editor_roundtrip_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt008_step5_completion_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt010_step3_editor_play_e2e_test.tscn`

## Completion Criteria

- `WorldStateCondition` 노드를 그래프에서 봤을 때 조건 의미를 알 수 있다.
- null/invalid 조건이 조용히 정상처럼 보이지 않는다.
- 기존 `.tres` 리소스의 저장/재로드와 런타임 평가 계약이 바뀌지 않는다.
- provider read/mutation 계약에 영향이 없다.
- Task/System/User Guide가 현재 동작과 일치한다.

## Related

- [[DT-007-ConditionSet-ConditionEvaluator]]
- [[DT-008-State-Condition-Dialogue-Integration]]
- [[DT-010-Dialogue-Debug-WorldState-Preview]]
- [[ADR-008-Structured-Condition-Evaluation]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[DialogueTool]]
- [[DialogueTool-User-Guide]]
- [[World-State-System]]
