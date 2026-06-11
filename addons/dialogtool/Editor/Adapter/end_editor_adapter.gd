@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# End 노드의 에디터 UI (EndDef._node_init에서 추출). 캡처할 필드는 없다.

func apply_params(node: DialogueNode, _params: Dictionary) -> void:
	node.set_slot(0, true, 0, Color.WHITE, false, 0, Color.WHITE)
	node.self_modulate = Color(0.8, 1.0, 0.8)
