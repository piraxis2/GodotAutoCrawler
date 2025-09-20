@tool
class_name StartDef extends FlowDefinition

func _node_init(node: DialogueNode) -> void:
	node.id = 0
	node.delete_button.queue_free()
	node.title = "start"
	node.add_child(Control.new())
	node.add_right_port_info(0, DialogueNode.port_type.flow, Color.WHITE)
	node.self_modulate = Color(0.8, 1.0, 0.8)

func _capture() ->void:
	pass

func _run() -> void:
	pass
