---
id: ADR-008
type: decision
status: accepted
date: 2026-06-14
system: WorldState
---

# Structured Condition Evaluation

## Context

World State는 타입 안전 read provider를 제공하지만 이를 복잡한 Dialogue/퀘스트 조건으로 조합하는 공용
모델은 없다. 기존 Dialogue `Expression`은 그래프 내부 계산에는 유용하나, 문자열 식은 schema-aware
검증, 위치 기반 오류, 평가 trace, migration, 제작 UI에 불리하다.

활협전·폴아웃식 대화는 많은 상태를 조합하면서도 "왜 이 응답이 열렸는가"를 설명할 수 있어야 한다.
조건 평가는 Dialogue에 종속되지 않고 퀘스트와 Response Selector도 공유해야 한다.

## Decision

### D1. 구조화된 Resource tree

조건은 임의 코드나 Expression 문자열이 아니라 직렬화 가능한 Resource tree로 표현한다.

- `StateCondition`: state key, operator, literal expected value
- `ConditionGroup`: ALL/ANY/NOT과 ordered children
- `ConditionSet`: reusable top-level asset과 metadata

첫 버전 operand는 state-to-literal만 지원한다. state-to-state, random, time, scene function은 후속 operand
설계 없이는 추가하지 않는다.

### D2. 독립적인 pure-read evaluator

`ConditionEvaluator`는 WorldState/Dialogue autoload를 직접 조회하지 않고 주입된 read provider의
`has_state`/`read_state`만 사용한다. mutation API, signal, save file, UI를 모른다.

### D3. Strict comparison and fail-closed

- equality는 DT-005의 다섯 scalar type에서 같은 `typeof()`끼리만 허용한다.
- ordered comparison은 첫 버전에서 같은 numeric type끼리만 허용한다(String/StringName lexical 거부).
- int↔float, String↔StringName 암시적 변환은 하지 않는다.
- `valid := errors.is_empty()`(structural + runtime). `valid==false`면 `passed`는 항상 `false`다.
- malformed tree, provider/key/type 오류는 모두 error이며 `valid=false`, `passed=false`다. ANY가
  논리적으로 true여도 error가 있으면 fail-closed이고, NOT/ANY는 errored child를 pass로 바꾸지 않는다.
- 빈 group과 잘못된 NOT arity는 authoring 오류이며 true로 해석하지 않는다.

### D4. Complete deterministic trace

평가는 child 저장 순서를 따른다. 기본 모드는 short-circuit하지 않고 모든 leaf를 평가해 path, key,
operator, expected, actual, passed가 포함된 전체 trace를 반환한다. 같은 key는 한 evaluation 안에서 한 번만
읽고 cache해 일관성과 비용을 고정한다(miss도 cache). 모든 trace node와 error는 `path`를 가지며(root=`[]`),
operator/logic trace 문자열(`equal..greater_equal`, `all|any|not`)은 안정 계약이다. report/trace는 호출별
deep copy다.

### D5. Bounded graph validation

평가는 2단계다: structural validation은 값을 읽지 않고(`read_count==0`), 통과해야 evaluation이 provider를
읽는다. validation은 null/unknown/cycle/alias/depth/node-count를 iterative(explicit-stack) traversal에서
검증한다. 첫 버전은 strict tree로 instance 공유(aliasing)를 금지하고 identity visited-set으로 cycle과 share를
함께 잡는다. 제안 한계는 depth 64, node 4096이며, 초과·cycle·alias에서는 provider를 읽지 않는다.

## Alternatives

- **Godot Expression 문자열**: 빠르지만 type/schema 검증, 안전성, trace, migration이 약해 공용 조건 포맷으로 거부.
- **DialoguePlayer 내부 전용 evaluator**: 구현은 작지만 퀘스트/Response Selector와 중복돼 거부.
- **평탄한 조건 목록만 지원**: 단순하지만 복합 RPG 조건 표현이 불편해 재귀 group을 우선 제안.
- **항상 short-circuit**: 빠르지만 실패 원인 전체 trace가 없어 제작·디버깅 비용이 커 기본 정책으로 거부.
- **missing key를 정상 false로 처리**: typo와 schema drift를 숨기므로 거부하고 invalid로 확정.

## Consequences

### Positive

- 조건이 타입 검사, 저장, diff, migration 가능한 데이터가 된다.
- Dialogue·퀘스트·응답 선택이 동일한 evaluator와 trace를 공유한다.
- 제작자가 false 결과의 정확한 leaf와 실제값을 확인할 수 있다.
- provider 주입 경계를 유지해 테스트와 시스템 분리가 쉽다.

### Negative

- recursive Resource의 Inspector UX와 cycle 방어 구현이 필요하다.
- complete trace는 short-circuit보다 비용이 크다.
- StateSchema-aware key picker가 없으면 authoring typo는 runtime validation에서 발견된다.

## Review Gate

[[DT-007-ConditionSet-ConditionEvaluator]] Step 0 설계 리뷰(2026-06-14)에서 아래를 확인해 **accepted**로
전환한다. 판정: Approved after design fixes(계약 정의는 Task의 Resolutions로 확정됨).

- recursive Resource의 Godot Inspector/`.tres` 적합성 → 유지. Godot 4.6 `@abstract` base + 4.6 typed-array
  Inspector. `StateDefinition` 배열·`Variant`(StringName) 왕복이 근거. Step 1 착수 시 Inspector spike로 확인.
- NOT 모델과 comparison 범위 → unary NOT group(arity 1), ordered는 numeric만.
- complete trace 정책과 규모 한계 → non-short-circuit 단일 모드, depth 64/node 4096, iterative + strict tree.
- provider/key/type 오류 분류 → 전면 fail-closed, `valid := errors.is_empty()`, 2단계 평가.

## Related

- [[DT-007-ConditionSet-ConditionEvaluator]]
- [[ADR-006-Typed-World-State]]
- [[World-State-System]]
- [[DialogueTool]]
