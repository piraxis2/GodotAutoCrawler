---
type: architecture
system: DialogueTool
updated: 2026-06-11
---

# Runtime Data Flow

## Save Path

```text
GraphNode UI
  -> Adapter.capture_params
  -> DialogueDefinition export fields
  -> editor.capture_current_graphedit
  -> DialogueGraphResource nodes/connections
  -> set_runtime_snapshot
  -> runtime_nodes/runtime_connections
  -> ResourceSaver.save
```

## Play Path

```text
Game code
  -> DialogueManager.play(resource)
  -> DialogueUI.play(resource)
  -> DialoguePlayer.start_dialogue(resource)
  -> runtime start node lookup
  -> execute until input wait or End
```

## Runtime Node Shape

```gdscript
{
    "id": 12,
    "type": &"say",
    "params": {
        "speaker": "Noabel",
        "text": "Hello",
        "portrait": "normal"
    }
}
```

## Runtime Connection Shape

```gdscript
{
    "from_node_id": 12,
    "from_port": 0,
    "to_node_id": 13,
    "to_port": 0
}
```

## Wait States

- `none`: 자동 실행 가능한 노드를 계속 처리한다.
- `text`: 사용자 advance를 기다린다.
- `choice`: 선택지 index를 기다린다.

## Signal Safety

- UI가 signal을 연결한 후 Player를 deferred start한다.
- DialogueManager는 종료된 source UI가 현재 UI인지 확인한다.
- 현재 UI를 먼저 정리한 후 `dialogue_end`를 emit한다.
- 이 순서는 종료 callback에서 다음 대화를 시작하는 재진입을 허용한다.

