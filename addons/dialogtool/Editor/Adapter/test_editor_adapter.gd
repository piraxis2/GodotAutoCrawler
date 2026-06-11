@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# Test(디버그) 노드의 에디터 UI (TestDef._node_init에서 추출). 캡처할 필드는 없다.

func apply_params(node: DialogueNode, _params: Dictionary) -> void:
	node.set_slot(0, true, DialogueNode.port_type.data, DialogueNode.color_dic["input"], false, 0, Color.WHITE)
	var test_button := Button.new()
	test_button.text = "test"
	test_button.pressed.connect(func(): print(node.get_connected_node(true, 0).definition._get_data_output(0)))
	node.add_child(test_button)
