---
id: ADR-002
type: decision
status: accepted
date: 2026-06-11
system: DialogueTool
---

# Editor Adapter

## Context

DialogueDefinition 내부의 `_node_init()`과 `_capture()`에 GraphNode, TextEdit, Button 등 에디터 UI 구현이 들어 있었다.

## Decision

노드별 UI 적용과 캡처를 `Editor/Adapter`로 이동한다. `NodeTypeRegistry`가 runtime type과 Adapter를 연결한다.

Definition은 기존 리소스 호환을 위해 export 필드와 Adapter 호출 중계를 당분간 유지한다.

## Consequences

### Positive

- UI 구현이 데이터 정의 밖으로 이동한다.
- apply/capture 동작을 노드별로 분리해 검토할 수 있다.
- 기존 `.tres` 구조를 깨지 않고 점진적으로 전환할 수 있다.

### Negative

- Definition이 아직 Editor Adapter 경로를 알고 있다.
- Registry는 노드 생성 팩토리가 아니라 조회 테이블 역할만 한다.

## Deferred

- DialogueNode 또는 Editor factory가 Adapter를 직접 구동
- `_get_dialogue_node()` 기반 생성 경로의 팩토리화

