@tool
class_name EndDef extends FlowDefinition

func _node_init(node: DialogueNode) -> void:
	node.set_slot(0, true, 0, Color.WHITE, false, 0, Color.WHITE)
	node.self_modulate = Color(0.8, 1.0, 0.8)
	pass
	
func _capture() ->void:
	pass

func _run() -> void:
	pass
