@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# Expression 노드의 에디터 UI (ExpressionValueDef._node_init/_capture에서 추출).
# 식 텍스트는 capture 시점에 code_edit에서 읽으므로, 기존의 라이브 갱신용
# on_change_text_edit 시그널 연결은 제거했다(저장/Build 모두 capture를 호출함).

func apply_params(node: DialogueNode, params: Dictionary) -> void:
	node.resizable = true
	node.code_edit.text = params.get("expression", "")
	node.set_slot(1, false, DialogueNode.port_type.data, Color.WHITE, true, DialogueNode.port_type.data, DialogueNode.color_dic["output"])


func capture_params(node: DialogueNode) -> Dictionary:
	return {"expression": node.code_edit.text}
