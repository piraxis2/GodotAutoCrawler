@tool
extends NodeDefinition
class_name EndDef

func _node_init(node: dialogue_node) -> void:
	node.title = "end"
	node.clear_all_slots()
	node.set_slot(0, true, 0, Color.WHITE, false, 0, Color.WHITE)
	node.self_modulate = Color(0.8, 1.0, 0.8)
	pass
