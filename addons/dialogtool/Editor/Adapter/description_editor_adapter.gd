@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# Description 노드의 에디터 UI (DescriptionDef._node_init/_capture에서 추출).
# 캡처에서 description은 Label의 .text를 읽는다(기존 코드의 `= node.description_label`
# 누락 버그를 바로잡음).

const DEFAULT_COLOR := Color(0.925, 0.667, 0.444, 0.8)


func apply_params(node: DialogueNode, params: Dictionary) -> void:
	var col: Color = params.get("node_color", DEFAULT_COLOR)
	node.color_picker.color = col
	node.modulate = col
	node.description_label.text = params.get("description", "")


func capture_params(node: DialogueNode) -> Dictionary:
	return {
		"node_color": node.color_picker.color,
		"description": node.description_label.text,
	}
