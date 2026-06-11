---
id: ADR-001
type: decision
status: accepted
date: 2026-06-10
system: DialogueTool
---

# Runtime Snapshot

## Context

초기 DialogueDefinition은 에디터 UI 생성, 값 캡처, 런타임 실행을 함께 담당했다. 이 구조는 GraphNode와 런타임 규칙을 결합해 변경과 테스트를 어렵게 했다.

## Decision

DialogueGraphResource에 순수 런타임 표현을 추가한다.

- `runtime_nodes`: id, type, params
- `runtime_connections`: node id와 port 연결

DialoguePlayer는 runtime snapshot만 해석한다. 기존 `nodes/connections`는 에디터 복원과 하위 호환에 사용한다.

## Consequences

### Positive

- 런타임이 에디터 위젯과 분리된다.
- 저장 결과를 독립적으로 validation할 수 있다.
- 노드 실행을 type 기반 evaluator로 테스트할 수 있다.

### Negative

- 에디터 표현과 런타임 snapshot 사이 동기화가 필요하다.
- 한동안 두 데이터 표현을 함께 유지해야 한다.

## Related

- [[DialogueTool-Architecture]]
- [[Runtime-Data-Flow]]

