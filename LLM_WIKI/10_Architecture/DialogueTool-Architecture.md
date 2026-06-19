---
type: architecture
system: DialogueTool
updated: 2026-06-11
---

# DialogueTool Architecture

## Agent Brief

- 책임: 대화 그래프 편집, 직렬화, 런타임 실행, UI 표시, 게임 통합
- 에디터 진입점: `addons/world_core/dialogtool/dialoguetool.gd`
- 그래프 에디터: `addons/world_core/dialogtool/Editor/editor.gd`
- 리소스: `addons/world_core/dialogtool/Resource/dialogue_graph_resource.gd`
- 실행기: `addons/world_core/dialogtool/RunTime/dialogue_player.gd`
- 전역 API: `addons/world_core/dialogtool/RunTime/dialogue_manager.gd`
- UI: `addons/world_core/dialogtool/UI/dialogue_ui.gd`
- 알려진 위험: 편집 데이터와 런타임 스냅샷의 불일치, 포트 순서, 지연 signal, 이전 리소스 호환

## Layers

### Editor

`GraphEdit`와 `DialogueNode`를 사용해 노드를 생성하고 연결한다. 저장 전 validation을 수행하고 에디터 복원 데이터와 런타임 스냅샷을 함께 생성한다.

### Editor Adapter

노드별 UI 적용과 캡처 구현을 Definition 밖으로 옮긴다. `NodeTypeRegistry`가 runtime type에서 Adapter를 찾는다. Definition이 Adapter 호출을 중계하는 호환 계층은 아직 남아 있다.

### Resource

`DialogueGraphResource`는 두 표현을 보관한다.

- `nodes/connections`: 에디터 재구성용
- `runtime_nodes/runtime_connections`: 런타임 실행용

### Runtime

`DialoguePlayer`는 runtime snapshot의 `type`과 `params`를 해석한다. Flow 노드는 상태 전이를 만들고 Data 노드는 Branch와 Expression에 값을 제공한다.

### UI

`DialogueUI`는 `display_text`, `offer_choice` 요청을 표시하고 사용자 입력을 Player에 전달한다. 대화 규칙을 직접 판단하지 않는다.

### Integration

오토로드 `DialogueManager`가 UI를 CanvasLayer에 생성하고 signal을 외부 게임 코드로 중계한다.

## Dependency Direction

```text
Editor -> Definition/Resource
Editor -> Adapter
Runtime -> Runtime snapshot
UI -> DialoguePlayer public API
Game -> DialogueManager
```

런타임이 GraphNode, CodeEdit, EditorInterface를 참조하지 않도록 유지한다.

## Related

- [[Runtime-Data-Flow]]
- [[ADR-001-Runtime-Snapshot]]
- [[ADR-002-Editor-Adapter]]
- [[ADR-003-DialogueManager-Autoload]]

