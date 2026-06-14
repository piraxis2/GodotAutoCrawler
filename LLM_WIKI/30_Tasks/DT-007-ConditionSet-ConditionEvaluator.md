---
id: DT-007
type: task
status: in-progress
system: WorldState
created: 2026-06-14
updated: 2026-06-14
tags: [task, world-state, dialogue, condition, evaluator]
---

# ConditionSet and ConditionEvaluator

## Goal

DT-005/006의 타입 안전 World State 위에 구조화되고 결정론적인 조건 평가 계층을 만든다.

이 Task가 끝나면 수십~수천 개 상태가 존재하는 게임에서도 Dialogue, 퀘스트, Response Selector가
문자열 `Expression`이나 save 구조를 직접 알지 않고 같은 조건 데이터와 평가 trace를 공유할 수 있어야 한다.

```text
WorldState read provider
  -> ConditionSet (ALL / ANY / NOT tree)
  -> ConditionEvaluator (pure read, fail-closed)
  -> {passed, valid, errors, trace}
  -> Dialogue condition node / conditional Choice / quest (후속 소비자)
```

## Context

현재 사실:

- DT-005는 `has_state`/`read_state`/`try_read_state` read provider 계약을 완성했다.
- `WorldStateStore`와 `DialoguePlayer`가 이 read 계약을 제공하며 Dialogue에는 provider가 명시적으로 주입된다.
- 기존 Branch는 Variable/Expression data node의 결과를 `_to_bool()`로 변환할 뿐 World State 조건을 모른다.
- Godot `Expression` 문자열은 임의 조건 작성에는 빠르지만 타입 검증, 구조화 trace, 안전한 migration,
  제작 도구 지원이 어렵다(ADR-006 후속 방향).
- State Read/Set Dialogue 노드, 조건부 Choice, Response Selector는 아직 없다.

## User Outcome

- 제작자는 `quest.main.stage >= 3 AND actor.example.affinity >= 10` 같은 조건을 구조화된 Resource로 표현한다.
- ALL/ANY/NOT을 중첩해 복잡한 분기 조건을 만들 수 있다.
- 평가 결과가 true/false뿐 아니라 어떤 leaf가 어떤 실제값으로 실패했는지 trace로 남는다.
- 누락 key, provider 없음, 타입 불일치, 잘못된 트리는 조용히 통과하지 않고 fail-closed 된다.
- 같은 상태 key는 한 평가 transaction에서 한 번만 읽어 일관된 결과와 예측 가능한 비용을 갖는다.
- evaluator는 상태를 변경하지 않고 `/root`, save file, Dialogue UI를 직접 알지 않는다.

## Scope

### Included

- 직렬화 가능한 조건 leaf와 논리 group Resource
- 조건 트리 validation과 구조화 오류
- strict typed comparison evaluator
- ALL/ANY/NOT 중첩 평가
- provider 기반 pure read
- 전체 평가 trace와 결정론적 순서
- 같은 key의 evaluation-local read cache
- cycle/depth/node-count 방어
- `.tres` 저장/재로드 및 StringName/Variant 타입 보존
- fake provider와 실제 `WorldStateStore` 통합 테스트
- 관련 System/User Guide/Current-State 갱신

### Out of Scope

- Dialogue editor의 State Condition/Read 노드
- State Set/Add/Multiply Effect 노드와 mutation provider 주입
- 조건부 Choice, Response Selector, quest UI
- 임의 Godot `Expression` 실행
- state-to-state 비교(첫 버전은 state key와 literal 비교)
- 시간, random, scene function, autoload property 같은 비결정적 operand
- schema migration/key alias
- trace history 저장 및 inspector UI

## Approved Data Contract

장기 결정은 [[ADR-008-Structured-Condition-Evaluation]]에 둔다. 아래 계약은 Step 0 설계 리뷰의
`Approved after design fixes` 판정과 Resolutions 1~7을 반영한 구현 기준이다.

```gdscript
# 공통 base Resource. 직접 인스턴스는 invalid.
class_name ConditionClause extends Resource

# leaf: provider의 한 state key와 literal expected_value 비교.
class_name StateCondition extends ConditionClause
enum Operator {
    EQUAL,
    NOT_EQUAL,
    LESS,
    LESS_EQUAL,
    GREATER,
    GREATER_EQUAL,
}
@export var key: StringName
@export var operator: Operator
@export var expected_value: Variant

# recursive boolean group.
class_name ConditionGroup extends ConditionClause
enum Logic { ALL, ANY, NOT }
@export var logic: Logic
@export var children: Array[ConditionClause]

# top-level reusable asset.
class_name ConditionSet extends Resource
@export var root: ConditionClause
@export var description: String
@export var tags: Array[StringName]
```

첫 버전 비교 규칙:

| Operator | 허용 actual/expected |
| --- | --- |
| EQUAL / NOT_EQUAL | bool, int, float, String, StringName. 양쪽 `typeof()` 정확 일치 |
| LESS / LESS_EQUAL / GREATER / GREATER_EQUAL | int 또는 float. 양쪽 `typeof()` 정확 일치 |

- int↔float, String↔StringName 암시적 변환은 하지 않는다.
- expected `null`, 지원하지 않는 타입, 빈/잘못된 key는 validation 오류다.
- provider/key/type 오류와 malformed tree는 `valid=false`, `passed=false`다.
- 빈 group은 authoring 오류로 취급해 fail-closed 한다.
- NOT group은 child가 정확히 하나여야 한다.

## Approved Evaluation Contract

```gdscript
ConditionEvaluator.evaluate(condition_set: ConditionSet, read_provider) -> Dictionary
```

반환 형식 초안:

```gdscript
{
    "passed": false,
    "valid": true,
    "errors": [],
    "trace": {
        "kind": "group",
        "logic": "all",
        "passed": false,
        "children": [
            {
                "kind": "state",
                "path": [0],
                "key": "quest.main.stage",
                "operator": "greater_equal",
                "expected": 3,
                "actual": 2,
                "passed": false
            }
        ]
    },
    "read_count": 1
}
```

정책:

- evaluator는 stateless 또는 호출별 context를 사용하고 provider mutation API에 접근하지 않는다.
- validation을 통과한 뒤 모든 leaf를 저장된 child 순서로 평가한다. short-circuit하지 않아 전체 trace를 남긴다.
- 같은 key가 여러 leaf에 나타나도 provider에서는 한 번만 읽고 호출 내 cache를 재사용한다.
- report/trace는 호출별 새 Dictionary/Array로 반환하며 Condition Resource나 provider 값을 수정하지 않는다.
- 기본 보호 한계 초안: depth 64, node 4096. 초과는 validation 오류이며 값 읽기를 시작하지 않는다.

## Resolved Decisions

Step 0 설계 리뷰에서 다음 항목을 확정했다. 결론은 바로 아래 Resolutions 1~7이 기준이다.

1. **재귀 Resource 모델**: `ConditionClause` base + `StateCondition`/`ConditionGroup` subclass가 Godot
   Inspector와 `.tres` 왕복에서 안정적인가. 불안정하면 평탄화된 node list + child index 모델을 쓸지 결정한다.
2. **NOT 표현**: unary NOT group을 둘지, leaf/group 공통 `negated` 필드로 단순화할지 결정한다.
3. **평가 전략**: 전체 trace를 위한 non-short-circuit를 기본으로 확정할지, trace mode와 fast mode를 나눌지 결정한다.
4. **오류 정책**: missing provider/key/type mismatch를 모두 invalid+false로 둘지, 일부를 정상 false로 둘지 결정한다.
5. **비교 범위**: ordered comparison을 숫자에만 허용할지 String/StringName lexical 비교도 허용할지 결정한다.
6. **규모 보호**: depth/node 한계값과 초과 오류 code를 확정한다.
7. **Schema-aware authoring**: DT-007에서 선택적 StateSchema validation까지 할지, runtime provider 검사만 두고
   editor key picker/Schema 연동을 Dialogue node 후속 Task로 미룰지 결정한다.

### Resolutions (Step 0 Design Review, 2026-06-14)

1. **재귀 Resource 모델 — 유지.** `@abstract class_name ConditionClause`(Godot 4.6 `@abstract` 지원)로 base를
   비인스턴스화한다. 평탄화 node list는 거부. `Array[StateDefinition]`이 같은 방식으로 직렬화되고
   (`world_state_schema.tres`), `Variant` export의 StringName이 왕복 보존됨(`default_value = &"dev"`)이
   근거다. malformed `.tres` 방어용 runtime `clause_unknown`은 유지. Step 1 착수 시 Inspector 왕복 spike로
   확인한다.
2. **NOT — unary NOT group 유지.** `logic=NOT` + child 정확히 1개, 위반은 `not_arity_invalid`. per-clause
   `negated` 필드는 거부(trace/De Morgan 모호).
3. **평가 전략 — non-short-circuit 단일 모드.** fast mode는 측정 후에만 추가(Risk 4).
4. **오류 정책 — 전면 fail-closed.** `provider_missing`/`state_missing`/`actual_type_mismatch`는 모두
   `errors[]`에 적재되어 `valid=false`, `passed=false`다. ANY가 논리적으로 true여도 error가 있으면
   `passed=false`다. NOT/ANY는 errored child를 pass로 바꾸지 않는다.
5. **비교 범위 — 숫자 ordered만.** equality는 5개 scalar에서 strict 동일 `typeof()`, ordered는 int↔int /
   float↔float만. String/StringName lexical ordering·암시적 int↔float는 거부. int vs float literal 불일치는
   numeric끼리의 별도 메시지(`actual_type_mismatch` message에 int/float 구분)로 surface한다.
6. **규모 보호 — strict tree.** depth 64 / node 4096을 iterative(explicit-stack) traversal에서 강제하며,
   한계 초과·cycle·alias는 어떤 provider read보다 먼저 거부한다(structural reject 시 `read_count==0`).
   tree 내 instance 공유(aliasing)는 금지하고 identity visited-set으로 cycle과 share를 함께 잡는다
   (`cycle_detected` 또는 `clause_aliased`). node-count는 instance당 1회.
7. **Schema-aware authoring — 후속 Task로 미룸.** DT-007은 runtime provider 검증만. 단 5번의 int/float
   authoring 마찰을 문서에 명시한다.

#### 계약 정의 고정

- `valid := errors.is_empty()` (structural + runtime 오류 모두 포함). `valid==false`면 `passed`는 항상 `false`.
- 2단계 평가: (1) structural validation은 값을 읽지 않는다(`read_count==0`). 통과해야 (2) evaluation이
  provider를 읽는다. structural 실패 시 evaluation을 시작하지 않는다.
- runtime 오류 leaf의 trace node는 `{passed:false, error:<code>, actual:null}`로 남기되 형제 leaf는 계속
  평가한다(non-short-circuit).
- 모든 trace node와 모든 error는 `path`를 갖는다(root = `[]`).
- `read_count`는 provider를 실제로 읽은 unique key 수다. miss(`has_state==false`)도 cache되어 같은 key를
  다시 probe하지 않는다.
- operator/logic trace 문자열은 안정 계약이다: operator = `equal|not_equal|less|less_equal|greater|
  greater_equal`, logic = `all|any|not`.
- report/trace/errors는 호출별 deep copy다. 반환값을 변조해도 Resource·provider·다음 evaluate에 영향이 없다.

## Error Codes

- `condition_set_null`
- `root_null`
- `clause_unknown`
- `group_empty`
- `not_arity_invalid`
- `logic_invalid` (Step 1 additive, 코드 리뷰 비준됨 2026-06-14 — 손상된 logic enum fail-closed)
- `cycle_detected`
- `clause_aliased`
- `depth_limit_exceeded`
- `node_limit_exceeded`
- `key_empty`
- `key_invalid_format`
- `operator_invalid`
- `expected_type_invalid`
- `ordered_type_invalid`
- `provider_missing`
- `provider_contract_invalid`
- `state_missing`
- `actual_type_mismatch`

오류는 `{code, path, key, message}` 구조로 반환한다. `path`는 root에서 child index 배열이며 trace와
validation 오류가 같은 위치 표현을 공유한다.

## Steps

## Step 0: Design Review and Contract Decision

목표:

- 실제 Resource/Inspector/provider 구조와 대조해 데이터 모델, 오류 정책, trace 계약을 확정한다.

작업 범위:

- `StateSchema`, `WorldStateStore`, `DialoguePlayer`, Branch/Variable/Expression 구조 검토
- 위 Open Decisions 1~7 결정
- [[ADR-008-Structured-Condition-Evaluation]] 승인 또는 수정
- Step 1~4 API와 완료 조건 보정

제외 범위:

- 제품 코드, `.tscn`, `.tres` 수정

완료 조건:

- 설계 리뷰 판정 Approved 또는 Approved after design fixes → **Approved after design fixes (2026-06-14)**
- Open Decisions에 미결정 항목 없음 → Resolutions 1~7로 확정
- 저장/평가/trace 계약이 구현 가능한 수준으로 고정됨 → 계약 정의 고정 절 참조

검증 방법:

- [[Design-Review-Prompt]]로 Task/ADR/실제 코드 대조

선행 조건: DT-005/DT-006 완료

## Step 1: Condition Resource Model and Validation

목표:

- 조건 leaf/group/set을 안전하게 작성하고 저장·재로드할 수 있다.

작업 범위:

- 승인된 Condition Resource 클래스
- 구조·key·operator·expected type validation
- cycle/depth/node-count 방어
- 구조화 validation result와 deep-copy 반환

제외 범위:

- provider 읽기와 실제 true/false 평가
- Dialogue editor node

완료 조건:

- nested ALL/ANY/NOT Resource가 validation을 통과한다.
- null child, unknown clause, 빈 group, NOT arity, cycle, limit 초과가 정확한 path/code로 거부된다.
- `.tres` 저장→cache 무시 재로드에서 tree 순서, key, operator, expected `typeof()`, metadata가 보존된다.
- invalid tree의 부분 compiled lookup/evaluation data를 공개하지 않는다.

검증 방법:

- Step 1 headless validation matrix
- ResourceSaver/ResourceLoader 왕복
- Godot headless editor load

선행 조건: Step 0 승인

### Step 1 구현 결과 (2026-06-14)

**Spike 선행 확인 (Resolution 1 / Risk 1)**

제품 구현 전에 recursive typed Resource의 생성·저장·재로드를 spike로 확인했다
(`tests/dt007_spike_resource_roundtrip`). 결과: Godot 4.6.3에서 안정. 구현 진행.

- `@abstract class_name ConditionClause`가 `script.is_abstract() == true`로 인식된다. 정적
  `ConditionClause.new()`는 컴파일 단계에서 거부되고 에디터 "New Resource" 피커는 abstract 타입을
  후보에서 제외한다(authoring 보호).
- 실측: 동적 `load(...).new()`는 base 스크립트가 붙은 인스턴스를 만들 수 있으나 구체 clause 타입
  (StateCondition/ConditionGroup)이 아니다 → validator의 `clause_unknown`이 런타임 backstop으로
  잡는다(ADR-008이 유지하기로 한 malformed-tree 방어와 일치).
- `Array[ConditionClause]` 재귀 트리가 `.tres`로 저장되고 `CACHE_MODE_IGNORE` 재로드에서 자식 순서,
  각 노드의 구체 subtype, enum, StringName/int/bool `expected_value`의 `typeof()`, metadata가 모두
  보존된다. Design Deviation 없음.

**변경 파일**

- `Assets/Script/gds/world_state/condition/condition_clause.gd` — `@abstract` base Resource.
- `Assets/Script/gds/world_state/condition/state_condition.gd` — leaf. `key`, `operator`(enum),
  `expected_value`(Variant). static `is_known_operator`/`is_ordered_operator`/`operator_to_string`
  (안정 trace 문자열).
- `Assets/Script/gds/world_state/condition/condition_group.gd` — recursive group. `logic`(ALL/ANY/NOT),
  `children: Array[ConditionClause]`. static `is_known_logic`/`logic_to_string`.
- `Assets/Script/gds/world_state/condition/condition_set.gd` — top-level asset. `root`, `description`,
  `tags`.
- `Assets/Script/gds/world_state/condition/condition_validator.gd` — stateless static 구조 검증기.
- `Assets/Script/gds/world_state/condition/tests/dt007_step1_validation_test.{gd,tscn}` — 검증 매트릭스.
- `Assets/Script/gds/world_state/condition/tests/dt007_spike_resource_roundtrip.{gd,tscn}` — spike.

**구현 내용 / 설계 판단**

- 검증은 `ConditionValidator.validate(condition_set) -> Dictionary` static API로 분리했다. Resource는
  순수 데이터로 남기고(데이터 모델이 검증/UI를 모름), Step 2 evaluator가 2단계 평가의 1단계로 재사용한다.
  provider 인자가 없어 구조 검증은 본질적으로 `read_count==0`이다.
- 한계 검사는 ADR-008대로 iterative(explicit-stack) DFS다. cycle/alias는 identity `visited`+`on_path`
  두 set으로 함께 잡는다(조상 재진입=`cycle_detected`, 다른 경로의 공유 인스턴스=`clause_aliased`).
- cycle/alias/depth/node 초과는 구조 거부로 즉시 traversal을 중단하고, leaf/group 내용 오류는
  non-short-circuit으로 모두 수집한다.
- depth는 root=1 기준, leaf depth 64 통과 / 65 거부. node-count는 인스턴스당 1회(null child 제외),
  4096 통과 / 4097 거부.
- 결과는 `{valid, errors[{code,path,key,message}], error_codes[], node_count}`이며 `res.duplicate(true)`
  deep copy로 반환한다. path는 root=`[]`에서 child index 배열.
- key 형식은 단일 source of truth로 `StateSchema.KEY_PATTERN`(DT-005 canonical key 문법)을 런타임에
  재사용한다. Step 1은 형식만 검사하고 schema lookup은 하지 않는다(Resolution 7).

**Design Deviation (additive, 리뷰 승인 필요)**

- `logic_invalid` 오류 코드를 추가했다. ADR-008 Error Codes 목록에는 없지만, 손상된 logic enum
  (예: `.tres` 수정으로 범위 밖 값)을 그냥 통과시키면 ADR-008 D3의 malformed-tree fail-closed 원칙을
  위반한다. StateSchema의 `value_type_invalid`/`lifetime_invalid`, 본 Task의 `operator_invalid`와
  대칭이다. 코드 목록 확정은 리뷰에서 비준한다.

**검증**

- `godot --headless --path <proj> --import`: 전역 클래스 ConditionClause/StateCondition/
  ConditionGroup/ConditionSet/ConditionValidator 등록, parse 오류 없음(에디터 import 성공).
- `dt007_spike_resource_roundtrip.tscn`: ALL PASS(@abstract 인식 + 재귀 `.tres` 왕복).
- `dt007_step1_validation_test.tscn`: ALL PASS(24개 시나리오, 아래 행렬). `exit 0`.
- 회귀: `dt005_step1_schema_test`, `dt006_step1_bootstrap_test` ALL PASS(새 클래스가 기존 부팅/검증
  경로를 깨지 않음).

**테스트 행렬 (dt007_step1_validation_test)**

| 그룹 | 사례 | 결과 |
| --- | --- | --- |
| 구조 valid | nested ALL/ANY/NOT(node 7), leaf-as-root | pass |
| null | condition_set null, root null, null child(clause_unknown@[1]) | pass |
| unknown | 동적 base 인스턴스(clause_unknown@[0]) | pass |
| group | empty ALL/ANY(group_empty), NOT 0/2 child(not_arity_invalid), NOT 1 valid | pass |
| graph | self cycle, indirect cycle(A→B→A), aliased 공유(clause_aliased@[1,0]) | pass |
| 한계 | depth 64 통과 / 65 거부, node 4096 통과 / 4097 거부 | pass |
| key | key_empty, key_invalid_format(5종) | pass |
| operator/logic | operator 범위 밖, logic 범위 밖 | pass |
| expected 타입 | null, Array, Vector2 → expected_type_invalid | pass |
| ordered | String/bool/StringName → ordered_type_invalid, int/float 통과 | pass |
| equality | 다섯 state 타입 EQUAL/NOT_EQUAL 통과 | pass |
| path | 깊은 leaf 오류 path [1,1,0] | pass |
| non-short-circuit | 형제 3개 서로 다른 오류 모두 수집(error_count 3) | pass |
| 불변성 | 반환 결과 변조 후 재검증 불변(deep copy) | pass |
| 저장 | `.tres` 왕복: 순서/operator/expected typeof/StringName/bool/metadata 보존 + 재검증 valid | pass |

**코드 리뷰 처리 (2026-06-14)**

판정: **수정 후 완료**.

- [P2] cycle fixture 실제 Resource 누수 — **수정**. self/indirect cycle 테스트(I/J)가 단언 후 순환
  참조를 해제하지 않아 종료 시 `ObjectDB instances leaked`/`2 resources still in use`가 재현됐다.
  단언 직후 `g.children.clear()`(I), `a.children.clear()`/`b.children.clear()`(J)로 순환을 끊었다.
  재실행에서 ALL PASS이며 종료 경고가 사라졌다(양성 노이즈가 아니라 fixture가 만든 실제 순환이었음).
- `logic_invalid` 추가 — **비준**(malformed-tree fail-closed 계약과 일치).
- null child를 `clause_unknown`으로 분류 — **허용**(고정 코드 목록과 일치, path/message로 구분).

**남은 위험 / 다음 Step 입력**

- Inspector에서 `Array[ConditionClause]`에 subclass를 직접 추가하는 UX는 headless로 검증 불가하다.
  spike는 직렬화 backbone(`.tres` 왕복)만 보장한다. 실제 에디터 클릭 검증은 후속 Dialogue node Task로
  미룬다(Step 1 범위 밖).
- Step 2(evaluator)는 이 validator를 1단계로 호출한 뒤에만 provider를 읽는다. provider read, strict
  comparison, trace, key cache는 Step 2 범위로 남는다(이번 Step에서 미구현).

## Step 2: Pure Read ConditionEvaluator

목표:

- fake provider에서 조건 트리를 결정론적으로 평가하고 전체 trace를 얻는다.

작업 범위:

- strict typed comparison
- ALL/ANY/NOT 평가
- evaluation-local key cache
- non-short-circuit complete trace
- provider/key/type 실패의 fail-closed report

제외 범위:

- 실제 WorldState autoload
- Dialogue runtime 소비

완료 조건:

- 모든 operator와 허용 타입의 true/false 경계가 검증된다.
- 중첩 truth table과 NOT 결과가 정확하다.
- 같은 key가 여러 번 사용돼도 provider read는 1회다.
- invalid tree/provider/key/type에서는 `valid=false`, `passed=false`, mutation 없음이다.
- trace 순서/path/actual/expected/operator가 입력 tree와 일치한다.

검증 방법:

- fake provider unit test
- provider call count와 mutation method 미호출 검증
- Step 1 회귀와 editor load

선행 조건: Step 1 완료 및 리뷰 승인

### Step 2 구현 결과 (2026-06-14)

**변경 파일**

- `Assets/Script/gds/world_state/condition/condition_evaluator.gd` — `ConditionEvaluator`. static
  `evaluate(condition_set, read_provider) -> {passed, valid, errors, trace, read_count}`.
- `Assets/Script/gds/world_state/condition/tests/dt007_step2_evaluator_test.{gd,tscn}` — fake provider
  매트릭스(23 시나리오 그룹).

**구현 내용 / 설계 판단**

- 2단계 평가다. 먼저 `ConditionValidator.validate()`(Step 1)를 1단계로 호출한다. 구조 실패면 provider를
  건드리지 않고 `{passed:false, valid:false, errors:<구조 오류>, trace:{}, read_count:0}`을 반환한다
  (structural reject 시 provider read 0 — 테스트 N에서 fake `has_calls==0` 확인).
- 통과하면 주입 provider의 `has_state`/`read_state`만으로 트리를 재귀 평가한다(구조 검증이 cycle 부재와
  depth≤64를 보장하므로 재귀가 안전). mutation/`try_read_state`/signal/autoload는 접근하지 않는다
  (테스트 O에서 mutation_calls==0 확인).
- strict comparison: leaf의 `typeof(actual) == typeof(expected)`가 아니면 `actual_type_mismatch`.
  int↔float, String↔StringName, bool↔int 암시적 변환 없음. expected/operator의 정적 타당성은 1단계가
  이미 보장하므로 2단계는 actual 타입만 본다.
- evaluation-local key cache: `key -> {has, value}`. 같은 key는 한 evaluation에서 한 번만 probe/read
  하고 miss(`has_state==false`)도 cache한다. `read_count`는 읽은 unique key 수.
- fail-closed via errored 전파: leaf가 provider/state/type 오류면 `errored=true, passed=false`. group은
  자식 중 하나라도 errored면 group도 errored→passed=false다. 따라서 `NOT(errored child)`,
  `ANY(true, errored)`가 pass로 바뀌지 않는다. 정상 child의 leaf trace `passed`는 그대로 유지된다.
- 오류 정책: `provider_missing`(null)/`provider_contract_invalid`(has_state·read_state 미보유)는 평가
  시작 시 1회 전역 오류로 적재하고 트리를 걸어 leaf를 errored로 채우되 어떤 값도 읽지 않는다(read_count 0).
  `state_missing`/`actual_type_mismatch`는 per-leaf 오류이며 `{code,path,key,message}`로 적재된다.
- `valid := errors.is_empty()`, `passed := valid and root.passed`. errored leaf의 trace 노드는
  `{kind:"state", path, key, operator, expected, actual:null, passed:false, error:<code>}`. 정상 leaf는
  `error` 필드가 없고 `actual`에 실제값을 담는다. group 노드는 `{kind:"group", logic, path, passed,
  children}`. operator/logic 문자열은 안정 계약(`equal..greater_equal`, `all|any|not`).
- report/trace/errors는 `duplicate(true)` deep copy로 반환한다(테스트 P: 반환값·Resource 변조 후 재평가 불변).

**검증**

- `--import`: `ConditionEvaluator` 전역 클래스 등록, parse 오류 없음(에디터 import 성공).
- `dt007_step2_evaluator_test.tscn`: **ALL PASS**(23 그룹, exit 0, 종료 시 누수 경고 없음).
- 회귀: `dt007_step1_validation_test`, `dt007_spike_resource_roundtrip`, `dt005_step1_schema_test`,
  `dt006_step1_bootstrap_test` ALL PASS.

**테스트 행렬 (dt007_step2_evaluator_test)**

| 그룹 | 사례 | 결과 |
| --- | --- | --- |
| 타입 equality | bool/int/float/String/StringName EQUAL·NOT_EQUAL true/false 경계 | pass |
| ordering | int/float LESS/LESS_EQUAL/GREATER/GREATER_EQUAL 경계 | pass |
| strict mismatch | String-vs-INT, FLOAT-vs-int(메시지 typeof 2·3), bool-vs-int, SN-vs-String | pass |
| truth table | ALL TT/TF, ANY FF/FT, NOT T/F | pass |
| 중첩/순서 | 3단계 트리 passed + trace kind/logic/path/actual/expected/순서 | pass |
| provider | null→provider_missing(read 0); 계약 누락/비-Object(int·float·String·Array·Dict·bool)/arity 위반/arg 타입 위반(int)/has_state 선언 반환 타입 위반→provider_contract_invalid(read 0, SCRIPT ERROR 없음); 미선언 has_state가 런타임 non-bool 반환→provider_contract_invalid(read 1, truthy로 안 샘); 미선언이지만 bool 정상 반환은 동작 | pass |
| state | missing key→state_missing(read 1, actual null) | pass |
| cache/count | 같은 key 3 leaf→read 1/has 1/read_call 1, 다른 key 2→read 2 | pass |
| missing probe | 같은 missing key 2 leaf→has 1, read_count 1, state_missing 2 | pass |
| changing provider | 값 변경 후 재평가 반영 | pass |
| fail-closed | NOT(missing) 미통과, ANY(true,errored) 미통과(group passed false, true leaf 유지) | pass |
| structural reject | empty group→read_count 0, trace {}, provider 미접촉 | pass |
| mutation | set_state/apply_state_batch 미호출(mutation_calls 0) | pass |
| 불변성 | 반환 report·Resource 변조 후 재평가 불변 | pass |
| trace 필드 | 성공 leaf(error 필드 없음)/에러 leaf(error+actual null) 형태 | pass |

**코드 리뷰 처리 (2026-06-14)**

- [P1] 잘못된 provider가 fail-closed되지 않고 SCRIPT ERROR 발생 — **수정**. 기존 검사는 `read_provider`가
  비-Object(예: `42`)일 때 `has_method()` 호출 자체에서 실패했고, 메서드 이름만 보고 arity/반환 타입을
  확인하지 않아 평가 중 호출이 깨지며 빈 Dictionary가 반환됐다. provider 사전 검사를 reflection 기반으로
  강화했다(`_read_provider_contract_error`): (1) `typeof==TYPE_OBJECT` + `is_instance_valid`, (2)
  `has_state`/`read_state` 존재, (3) 각 메서드가 positional 인자 1개로 호출 가능(`get_method_list`의
  args/default_args로 required<=1<=total), (4) `has_state` 선언 반환 타입이 bool(미선언/Variant는 정적
  판단 불가라 허용, 구체적 비-bool은 거부). 어느 하나라도 실패하면 provider를 한 번도 호출하지 않고
  `provider_contract_invalid`로 fail-closed한다(`read_count==0`). 회귀 테스트 G2(비-Object 6종)/
  G3(arity)/G4(반환 타입) 추가, 전부 SCRIPT ERROR 없이 ALL PASS.
- [P1 재검토] arg 타입/미선언 반환 런타임 누수 — **수정**. (a) 사전 검사가 인자 *개수*만 보고 *타입*을
  안 봐서 `has_state(key: int)`가 통과한 뒤 StringName key 호출에서 SCRIPT ERROR가 났다. `get_method_list`의
  첫 인자 `type`을 검사해 `StringName` 또는 미선언/Variant(TYPE_STRING_NAME/TYPE_NIL)만 허용하고 그 외
  (예: int)는 호출 전에 `provider_contract_invalid`로 거부한다(read 0). (b) 미선언 반환 `has_state()`가
  `1`을 돌려주면 `if not entry.has`에서 truthy로 암시 변환돼 `valid/passed=true`로 샜다. `has_state` 반환을
  Variant로 받아 `typeof()==TYPE_BOOL`을 런타임에 확인하고, non-bool이면 per-leaf `provider_contract_invalid`로
  fail-closed한다. **read_count 정책**: 런타임 contract 위반도 has_state를 *호출*했으므로 해당 unique key를
  read 1회로 카운트하고 cache해 재-probe하지 않는다(정상/누락/contract 위반 모두 key당 1회). 회귀 테스트
  G5(arg 타입)/G6(미선언 non-bool 반환, read_count 1·passed false)/G7(미선언이지만 bool 정상 반환은 동작)
  추가, 전부 SCRIPT ERROR 없이 ALL PASS.

**남은 위험 / 다음 Step 입력**

- 실제 `WorldStateStore`(autoload) 연동과 set/batch/reset/snapshot restore 뒤 재평가는 Step 3 범위다.
  fake provider는 `has_state`/`read_state` 시그니처를 실제 facade와 동일하게 맞췄으므로 Step 3에서 Store를
  그대로 주입할 수 있다.
- Dialogue runtime 소비(Condition node/조건부 Choice)는 후속 Task로 남는다.

## Step 3: WorldState Provider Integration

목표:

- 실제 `WorldStateStore`를 provider로 사용해 저장 상태 변화가 조건 결과에 즉시 반영된다.

작업 범위:

- 주입 Store 통합 테스트
- bootstrap의 INT/FLOAT/STRING/STRING_NAME/BOOL 평가
- set/batch/reset/snapshot restore 뒤 재평가
- read-only와 SAVE/SESSION이 평가 의미에 불필요하게 결합되지 않음을 확인

제외 범위:

- `/root` 직접 조회
- Dialogue Condition node와 editor UI
- mutation

완료 조건:

- evaluator가 Store의 read facade만으로 동작한다.
- Store mutation 후 새 evaluate 호출은 새 값을 읽고, 한 호출 내부 결과는 일관된다.
- snapshot restore와 SESSION reset 후 조건 결과가 실제 최종 상태와 일치한다.
- evaluator가 Store signal이나 mutation API에 의존하지 않는다.

검증 방법:

- `WorldStateStore.new()` + schema 주입 통합 테스트
- DT-005/006 회귀

선행 조건: Step 2 완료 및 리뷰 승인

### Step 3 구현 결과 (2026-06-14)

**제품 코드 변경 없음**

Step 3는 통합 검증 단계다. `ConditionEvaluator`는 이미 주입 provider의 `has_state`/`read_state`만
사용하고, 실제 `WorldStateStore`가 그 read facade(`has_state(key: StringName) -> bool`,
`read_state(key) -> Variant`)를 제공한다. Store는 Step 2의 강화된 provider 계약 검사(Object/arity/
StringName arg 타입/`has_state` 반환 bool)를 그대로 통과하므로 evaluator/Store 어느 쪽도 수정하지 않았다.

**변경 파일**

- `Assets/Script/gds/world_state/condition/tests/dt007_step3_store_integration_test.{gd,tscn}` —
  실제 Store 주입 통합 테스트(11 시나리오).

**구현 내용 / 설계 판단**

- `WorldStateStore.new()` + 실제 bootstrap schema(`world_state_schema.tres`, `CACHE_MODE_IGNORE` 로드) +
  `initialize()`로 ready Store를 만들고, 그 Store를 evaluator의 read provider로 그대로 주입한다.
- mutation은 테스트가 Store native API(`set_value`/`apply_batch`/`reset_value`/`reset_lifetime`/
  `import_snapshot`)로 수행하고, 그 뒤 새 `evaluate` 호출이 변경된 값을 읽는지 확인한다. evaluator는
  mutation/signal에 의존하지 않는다.
- Store가 Node이므로 각 시나리오 끝에서 `store.free()`로 정리해 종료 시 누수 경고가 없다.

**테스트 행렬 (dt007_step3_store_integration_test)**

| 그룹 | 사례 | 결과 |
| --- | --- | --- |
| A 계약/default | 실제 Store가 provider로 수락(provider 오류 없음), default 평가 | pass |
| B 다섯 타입 | INT/FLOAT/STRING/STRING_NAME/BOOL default ALL 평가(read_count 5) + FLOAT ordered | pass |
| C set_value | stage 0→5 후 `>=3`이 false→true | pass |
| D apply_batch | affinity=10/health=40 적용(applied, diff 2) 후 ANY true | pass |
| E reset_value | set 후 reset로 default 복귀, 결과 true→false | pass |
| F reset_lifetime | SESSION seen=true→reset_lifetime(SESSION)→default false | pass |
| G snapshot | export→변경→import_snapshot 복원 후 결과 false→true | pass |
| H read-only/SESSION | read-only `world.build.channel`·SESSION `session.intro.seen` 정상 read | pass |
| I pure read | evaluate 중 value_changed 미발행, Store 값 불변 | pass |
| J fail-closed | 미등록 key→state_missing, INT key vs String expected→actual_type_mismatch | pass |
| K read_count | 같은 key 3회+다른 key 1회 → unique 2 | pass |

**검증**

- `--import`: parse 오류 0(에디터 import 성공).
- `dt007_step3_store_integration_test.tscn`: **ALL PASS**(11 그룹, exit 0, 누수 경고 없음).
- 회귀: `dt007_step1/step2/spike`, `dt005_step1/step2`, `dt006_step1/step5`(통합) ALL PASS.

**완료 조건 충족**

- evaluator가 Store의 read facade만으로 동작한다(A: provider 오류 없음).
- Store mutation 후 새 evaluate가 새 값을 읽고(C/D/E/F/G), 한 호출 내부는 cache로 일관된다(K).
- snapshot restore·SESSION reset 후 결과가 실제 최종 상태와 일치한다(F/G).
- evaluator가 Store signal/mutation API에 의존하지 않는다(I: pure read, 값/시그널 불변).

**남은 위험 / 다음 Step 입력**

- Step 4(완료 판정)는 복합 RPG 시나리오 end-to-end + `.tres` 재로드 + 성능 sanity + 전체 회귀와 문서
  완료 갱신이다. 후속 State Condition Dialogue node Task의 입력 계약을 문서화한다.

## Step 4: Integration Regression and Completion Review

목표:

- ConditionSet/Evaluator를 독립 공용 계층으로 완료 판정하고 후속 Dialogue 소비 Task에 안정된 계약을 넘긴다.

작업 범위:

- 복합 RPG 시나리오 통합 테스트
- Resource round-trip + Store lifecycle + trace end-to-end
- 성능 sanity(최대 허용 node, unique key read count)
- System/User Guide/Current-State/Open-Tasks/Review 갱신

대표 시나리오:

```text
ALL
  quest.main.stage >= 3
  ANY
    actor.example.affinity >= 10
    NOT
      session.intro.seen == true
```

제외 범위:

- Dialogue graph node와 조건부 Choice UI

완료 조건:

- `.tres` 조건을 재로드해 실제 Store에서 동일 결과·trace를 낸다.
- malformed/missing/type mismatch가 fail-closed이며 값과 Store가 불변이다.
- DT-004/005/006 회귀와 headless editor load가 성공한다.
- P0/P1 없음, 후속 State Condition Dialogue node Task 입력 계약이 문서화된다.

검증 방법:

- Step 1~4 전체 matrix
- DT-004/005/006 전체 회귀
- Godot headless editor load

선행 조건: Step 3 완료 및 리뷰 승인

### Step 4 구현 결과 (2026-06-14)

**변경 파일**

- `Assets/Script/gds/world_state/condition/tests/dt007_step4_e2e_test.{gd,tscn}` — end-to-end 완료 테스트.
- 리뷰: [[DT-007-Condition-Review]] 신규. System/User Guide/Current-State/Open-Tasks 갱신.

제품 코드 변경 없음(완료 판정·통합 검증 단계).

**테스트 행렬 (dt007_step4_e2e_test)**

| 그룹 | 사례 | 결과 |
| --- | --- | --- |
| A report parity | 대표 RPG set `.tres` 왕복 후, 4개 store 상태에서 in-memory vs 재로드 set의 **전체 report**(passed/valid/errors/trace/read_count 문자열 표현) 일치 | pass |
| B lifecycle | default(ALL false)/gate open/affinity path/closed 결과 + trace path/actual/logic 정확성 | pass |
| C load lifecycle | `restore_world_state`(coordinator)로 SAVE 복원 + **SESSION seen=false 직접 단언**; affinity 낮춰 gate가 SESSION에 의존하게 하고 음성 대조(seen=true→닫힘)로 false-green 제거 | pass |
| D 성능 sanity | node 4096(경계) valid+passed, 같은 key 4095 leaf의 read_count 1; node 4097 초과→read 0+node_limit_exceeded | pass |
| E fail-closed/불변 | 미등록→state_missing, 타입 불일치→actual_type_mismatch, malformed→read 0; evaluate 동안 value_changed 0·Store 값 불변 | pass |

**검증 (전체 회귀)**

- `--import`: parse 오류 0(editor load 성공).
- DT-007: `dt007_step1`(24)/`step2`(23)/`step3`(11)/`step4`(5 그룹)/`spike` ALL PASS.
- DT-005: `dt005_step1~6` ALL PASS. DT-006: `dt006_step1~5` ALL PASS.
- DialogueTool DT-004: `dt004_step1~4`+`pipeline` ALL PASS.
- 합계 21 headless 테스트 ALL PASS, 종료 시 누수/SCRIPT ERROR 없음.

**완료 조건 충족**

- `.tres` 조건 재로드가 실제 Store에서 동일 report(A) — passed/valid/errors/trace/read_count 문자열 표현 일치.
- malformed/missing/type mismatch fail-closed이며 Store·값 불변(E).
- DT-004/005/006 회귀와 headless editor load 성공.
- 후속 State Condition Dialogue node Task 입력 계약은 [[DT-007-Condition-Review]]에 문서화.
- P0/P1 없음(구현자 자가평가). 최종 완료 판정은 리뷰 대기.

## Verification Matrix

| 범주 | 필수 사례 |
| --- | --- |
| 구조 | null root/child, unknown clause, base `ConditionClause` 직접 인스턴스, empty group, NOT 0/2 child, self/indirect cycle, aliased(공유) clause |
| 한계 | depth 경계/초과, node 경계/초과 |
| 타입 | 다섯 State 타입 equality, numeric ordering, strict mismatch, null/unsupported expected |
| 논리 | ALL/ANY/NOT truth table, 다중 중첩, child 순서 |
| provider | null, 계약 method 누락, missing key, call count, changing fake provider |
| trace | 성공/실패 leaf, group 결과, path, actual/expected, 전체 평가 순서 |
| 저장 | `.tres` 왕복, StringName expected 보존, shared/cyclic Resource 처리 |
| 통합 | set/batch/reset/import 뒤 재평가, SAVE/SESSION, read-only |
| 회귀 | DT-004, DT-005, DT-006, editor import |
| fail-closed | NOT(missing leaf) 미통과, ANY(true, errored) 미통과, errored child를 pass로 변환 안 함 |
| 오류 분류 | structural reject 시 `read_count==0`, EQUAL 타입 불일치(String vs INT) runtime 오류, FLOAT state vs int literal 메시지, 반복 missing key probe 1회 |
| 불변성 | 반환 report/trace 변조 후 재평가 결과 불변, cyclic Resource graph 비크래시(iterative) |

## Risks

1. Godot Inspector가 recursive typed Resource array의 subclass 생성을 불편하게 다룰 수 있다.
2. Resource graph cycle은 naive recursive validation/evaluation을 stack overflow시킬 수 있다.
3. trace가 actual 값을 포함하므로 향후 민감 정보 redaction 정책이 필요할 수 있다.
4. 모든 leaf 평가와 full trace는 short-circuit보다 비싸다. 조건 set 규모를 측정하고 fast mode는 필요할 때 추가한다.
5. runtime provider만으로는 authoring 시 key typo를 즉시 발견하기 어렵다. Schema-aware picker는 후속 editor 통합이 필요하다.

## Follow-ups

- State Condition/Read Dialogue data node와 Branch 연결
- 조건부 Choice와 Response Selector
- Set/Add/Multiply State Effect와 mutation provider 주입
- Condition trace inspector와 DialogueHistory
- schema-aware key picker와 operator/type filtering

## Related

- [[ADR-008-Structured-Condition-Evaluation]]
- [[ADR-006-Typed-World-State]]
- [[DT-005-StateSchema-WorldStateStore]]
- [[DT-006-WorldState-Runtime-Integration]]
- [[World-State-System]]
- [[DialogueTool]]
