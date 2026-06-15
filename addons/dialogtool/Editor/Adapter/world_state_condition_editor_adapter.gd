@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# state_condition 노드의 에디터 UI 어댑터 (DT-008 Step 2).
# - boolean output 포트 하나를 slot 1(HBoxContainer 행)에 둔다. Branch의 boolean 조건 입력과
#   같은 타입이고, editor.gd가 등록한 data↔boolean 호환으로 data 입력에도 연결된다.
# - picker로 현재 ConditionSet을 표시/선택한다. capture는 picker가 보관한 Resource를 반환한다.


func apply_params(node: DialogueNode, params: Dictionary) -> void:
	node.set_slot(
		1,
		false, DialogueNode.port_type.data, Color.WHITE,
		true, DialogueNode.port_type.boolean, DialogueNode.color_dic["boolean"])
	if node.has_method("set_condition_set"):
		node.set_condition_set(params.get("condition_set"))


func capture_params(node: DialogueNode) -> Dictionary:
	var cs = null
	if node.has_method("get_condition_set"):
		cs = node.get_condition_set()
	return {"condition_set": cs}
