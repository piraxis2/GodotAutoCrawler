@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# Variable 노드의 캡처 (VariableDef._capture에서 추출). UI는 variable_node.gd가
# 직접 구성하므로 apply_params는 비워 둔다. node.get_value()는 {name, type, variable}.

func capture_params(node: DialogueNode) -> Dictionary:
	return node.get_value()
