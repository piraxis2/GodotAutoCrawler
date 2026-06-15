---
id: ADR-009
type: decision
status: accepted
date: 2026-06-14
system: DialogueTool, WorldState
---

# State Condition Dialogue Consumption

## Context

DT-007은 재사용 가능한 `ConditionSet`과 pure-read `ConditionEvaluator`를 완료했다. DialogueTool은 이미
read provider를 주입받고 Branch/Choice Data port를 갖지만, runtime Data 평가기는 Variable/Expression만
알고 Choice의 항목별 Data 입력은 사용하지 않는다.

활협전·폴아웃식 대화에서는 같은 조건 자산이 Flow 분기와 응답 노출을 제어해야 한다. 이 통합은 기존
Dialogue Resource를 깨지 않고 evaluator의 strict/fail-closed/trace 계약을 보존해야 한다.

## Proposed Decision

### D1. Condition은 Branch 전용 로직이 아니라 boolean Data 노드다

`WorldStateConditionDef`(`runtime type: state_condition`)가 `ConditionSet`을 보유하고 boolean output을 낸다.
Branch는 기존처럼 Data 입력만 소비한다. evaluator 호출을 Branch 안에 직접 넣지 않아 향후 Choice와 다른
Data 소비자가 같은 노드를 재사용할 수 있게 한다.

### D2. 원본 injected read provider를 evaluator에 전달한다

DialoguePlayer는 `/root/WorldState`를 조회하지 않는다. Manager/UI가 주입한 `_read_state_provider`를
`ConditionEvaluator.evaluate()`에 직접 전달한다. provider가 없을 때 DialoguePlayer facade를 경유해
`state_missing`으로 바꾸지 않고 evaluator의 `provider_missing` 계약을 유지한다.

### D3. 평가 결과는 bool과 report signal로 분리한다

Data Flow에는 `report.passed`만 전달한다. 전체 report는
`condition_evaluated(condition_node_id, consumer_node_id, report)` signal로 노출한다. Branch/Choice/UI는
report를 변조하거나 재평가하지 않는다. 이는 후속 trace inspector와 DialogueHistory의 안정된 seam이다.

### D4. 조건부 Choice는 기존 항목별 Data 입력을 사용한다

Choice Resource에 별도 `conditions` parallel array를 추가하지 않는다. 각 ChoiceItem의 기존 Data input에
Condition/Variable/Expression을 연결한다. 입력이 없으면 unconditional로 간주해 레거시 Choice를 보존한다.

### D5. Choice는 표시 목록과 원래 Flow port mapping을 함께 스냅샷한다

Choice 진입 시 각 항목 조건을 원래 순서로 한 번 평가하고, visible text와 original output port mapping을
함께 만든다. UI는 visible list만 받고, `select_choice()`가 mapping을 통해 원래 Flow port를 선택한다.
대기 중 외부 상태 변경은 현재 목록을 바꾸지 않으며 재진입 때 다시 평가한다.

### D6. 오류는 fail-closed다

- State Condition invalid/provider missing/state missing/type mismatch: Branch false, Choice 항목 숨김.
- evaluator report는 항상 signal로 관찰 가능하다.
- 모든 Choice가 숨겨지면 warning 후 기존 empty-choice 종료 정책을 사용한다.
- 범위를 벗어난 visible index는 Flow를 바꾸지 않고 대기를 유지한다.

### D7. 저장 호환은 additive다

새 runtime node type과 연결만 추가한다. 기존 Choice의 text/output port 순서와 연결 형식은 변경하지 않는다.
Data 입력이 없는 기존 Choice와 Variable/Expression Branch의 runtime 동작은 그대로 유지한다.

## Alternatives

- **Branch에 ConditionSet 필드를 직접 추가**: 빠르지만 Choice/다른 소비자가 재사용하지 못하고 Branch가
  World State 평가 책임을 갖게 되어 거부한다.
- **ChoiceDef에 `Array[ConditionSet]` 추가**: 직렬화는 단순하지만 동적 항목 reorder/resize와 parallel
  array가 쉽게 어긋나며 기존 Data 입력이 중복돼 거부한다.
- **Choice 표시 중 조건을 계속 재평가**: 상태 변화 반영은 빠르지만 UI index와 Flow mapping이 다른 시점을
  볼 수 있어 거부한다.
- **조건 오류를 정상 false와 완전히 동일하게 숨김**: 플레이는 가능하지만 authoring 오류를 찾기 어려워
  report signal 없이 사용하는 방식은 거부한다.
- **조건 오류 선택지를 disabled로 표시**: 이유 표시 UX와 UI 계약이 추가로 필요하므로 첫 버전에서는 숨김을
  선택하고 disabled/reason UI는 후속으로 둔다.

## Consequences

### Positive

- Branch와 Choice가 하나의 ConditionSet/evaluator/trace 계약을 공유한다.
- 기존 Choice Resource를 migration 없이 유지한다.
- 필터링된 Choice에서도 선택 Flow가 원래 포트와 정확히 대응한다.
- Dialogue runtime의 provider 주입 경계와 pure-read 성질이 유지된다.

### Negative

- DialoguePlayer가 Choice 대기 동안 visible-port mapping 상태를 추가로 소유한다.
- 조건 오류는 플레이어에게 보이지 않고 제작자 signal/debugger에서 확인해야 한다.
- inline 조건 편집이 없어 첫 버전 제작 UX는 외부 ConditionSet Resource 지정 중심이다.

## Review Gate

[[DT-008-State-Condition-Dialogue-Integration]] Step 0에서 다음을 실제 코드와 대조해 확정한다.

- `condition_evaluated` signal의 node/consumer 식별과 수명 주기
- Choice Data input port 번호와 output port mapping의 저장/재로드 안정성
- all-hidden/invalid-index 정책
- external/subresource ConditionSet의 runtime snapshot 보존
- 기존 Variable/Expression Branch와 no-input Choice 하위 호환

### Resolutions (Step 0 Design Review, 2026-06-15)

판정: **Approved after design fixes**. 코드 대조로 D1~D7이 구현 가능함을 확인하고 아래를 확정했다.
상세는 [[DT-008-State-Condition-Dialogue-Integration]] Step 0 결과(F1~F5) 참조.

- **consumer 식별.** `consumer_node_id`는 해당 Data 노드의 입력 포트를 **직접 소유한** 노드 id다.
  `_get_data_value(node_id, consumer_node_id, visited)`로 전달하고, `state_condition` 분기에서
  signal을 1회 발행한다(Branch=branch id, Choice=choice id, expression 중첩=expression id). 구조
  invalid(`read_count==0`)도 1회 발행한다.
- **Choice 포트 번호.** `choice_node.gd` 슬롯 구조상 항목 i의 data 입력 port = `i+1`(port 0=flow 입력),
  flow 출력 port = `i`다. `select_choice(index)`의 index는 그대로 from_port다. mapping은
  `original_output_port == original_item_index`.
- **runtime snapshot 보존.** ConditionSet은 `{"condition_set": <Resource>}`로 `runtime_nodes`
  Dictionary에 중첩 저장된다. DT-007 spike 범위 밖이므로 Step 2 착수 시 중첩 Resource `.tres` 왕복
  spike(external + inline)로 먼저 검증한다.
- **provider 직접 전달.** `DialoguePlayer._read_state_provider`를 `ConditionEvaluator.evaluate`에 그대로
  넘긴다(facade `has_state` 재포장 금지). null이면 evaluator가 `provider_missing`으로 fail-closed.
- **addon 결합 수용.** DialogueTool addon이 `ConditionSet`/`ConditionEvaluator` 전역 클래스를 참조해
  world_state condition 모듈에 의존하게 된다. 단일 게임 repo에서 수용하고 시스템 문서에 기록한다.
- **invalid-index.** 범위 밖 visible index는 Flow를 바꾸지 않고 대기를 유지한다(현재 코드의
  "미연결 포트 → 대화 종료"와 다르므로 Step 4에서 `select_choice` 검증 순서를 재배치한다).

status: proposed → **accepted**.

## Related

- [[DT-008-State-Condition-Dialogue-Integration]]
- [[DT-007-ConditionSet-ConditionEvaluator]]
- [[ADR-008-Structured-Condition-Evaluation]]
- [[DialogueTool]]
- [[World-State-System]]
