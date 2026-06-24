---
id: ADR-005
type: decision
status: accepted
date: 2026-06-11
system: DialogueTool
---

# Nonblocking Effect Connections

## Context

현재 Dialogue runtime은 `(from_node_id, from_port)`에서 첫 번째 연결 하나만 선택한다.
따라서 같은 Flow 출력에서 Portrait와 Say를 나란히 연결하면 한쪽만 실행되며, 사용자가 기대한
"Portrait 상태를 적용하면서 본 대화를 진행"하는 그래프를 표현할 수 없다.

일반 Flow fan-out을 그대로 허용하면 Say와 Choice처럼 입력을 기다리는 노드가 동시에 실행되거나,
여러 Branch가 서로 다른 `current_node_id`를 덮어쓰는 문제가 생긴다. DialoguePlayer의 현재 실행
모델은 단일 실행 커서와 단일 wait state를 전제로 하므로 일반적인 병렬 Flow와 맞지 않는다.

## Decision

DialogueTool에 일반 병렬 Flow가 아닌 **비대기 Effect 연결**을 도입한다.

- Flow 연결은 기존처럼 실행 커서가 이동하는 주 경로이며 한 시점에 하나만 선택한다.
- Effect 연결은 실행 커서를 소유하지 않고, 연결된 비대기 명령들을 결정적인 순서로 실행한다.
- 한 실행 지점의 Effect들을 모두 처리한 뒤 주 Flow 하나를 실행한다.
- Portrait Show/Hide/Expression을 첫 Effect 노드 유형으로 지원한다.
- Say, Choice, Branch, End와 Data 노드는 Effect 경로에 연결할 수 없다.
- Effect 경로에는 wait state를 만드는 노드를 허용하지 않는다.

개념적 실행 순서:

```text
현재 Flow 노드 실행
  -> 연결된 Effect들을 저장된 연결 순서로 실행
  -> 주 Flow 연결 하나로 이동
  -> Say/Choice라면 입력 대기
```

예시:

```text
Start
  effect -> PortraitShow(left)
  effect -> PortraitShow(right)
  flow   -> Say
```

이 모델은 동시에 스레드나 coroutine을 실행하는 진짜 병렬 처리가 아니다. Effect 요청들을
순차적이고 결정적으로 적용한 뒤 주 Flow를 계속하는 fan-out이다.

## Compatibility

- 기존 `runtime_connections` 항목의 필드와 기존 Flow 포트 index를 변경하지 않는다.
- 새 연결 종류는 포트 타입 또는 명시적인 connection kind로 저장하되, kind가 없는 이전 연결은
  기존 Flow/Data 포트 규칙으로 해석한다.
- 기존 직렬 그래프 `PortraitShow -> Say`는 계속 실행돼야 한다.
- 기존 Portrait Definition 필드와 `portrait_state` 요청 계약은 변경하지 않는다.
- 기존 리소스를 단순 로드/저장할 때 연결 의미나 포트 순서가 바뀌지 않아야 한다.

## Guardrails

- 한 Flow 출력에서 주 Flow 대상이 둘 이상이면 저장 validation 오류로 처리한다.
- Effect 대상이 아닌 노드가 Effect 연결에 있으면 저장 validation 오류로 처리한다.
- Effect 순환 연결은 저장 validation 또는 런타임 visited 방어로 차단한다.
- Effect 하나의 실패가 주 Flow를 멈추지 않는 정책을 기본으로 하되 오류를 경고한다.
- Effect 실행 callback에서 대화 교체가 발생하면 기존 DialogueManager source guard를 유지한다.

## Consequences

### Positive

- Portrait와 Say를 강하게 결합하지 않고 같은 실행 시점에 배치할 수 있다.
- 좌우 Portrait 여러 개를 한 번에 설정할 수 있다.
- 향후 Sound, Emit Event 등 비대기 연출 명령에 같은 모델을 재사용할 수 있다.
- 일반 Flow는 단일 커서 규칙을 유지하므로 대기 노드 동시 실행을 피한다.

### Negative

- 에디터 포트 타입, validation과 runtime 연결 조회를 함께 확장해야 한다.
- 기존 Portrait 노드의 직렬 Flow 사용과 새 Effect 사용을 모두 지원하는 동안 계약이 복잡해진다.
- Effect 실행 순서와 순환 정책을 명시적으로 유지해야 한다.

## Alternatives Rejected

- **모든 Flow fan-out 실행**: Say/Choice/Branch 동시 실행과 wait state 충돌 때문에 제외한다.
- **Portrait를 Say 필드로 통합**: Say 없는 Portrait 변경을 막고 강결합을 만든다.
- **연결 순서의 첫 Portrait만 특별 처리**: 다중 Portrait와 향후 Effect 확장성이 부족하다.

## Related

- [[DT-004-Nonblocking-Effect-Flow]]
- [[ADR-001-Runtime-Snapshot]]
- [[DT-002-Portrait-State]]

