@tool
extends NodeDefinition
class_name StartDef


func _node_init(node: dialogue_node) -> void:
	node.delete_button.queue_free()
	node.title = "start"
	node.clear_all_slots()
	node.set_slot(0, false, 0, Color.WHITE, true, 0, Color.WHITE)
	node.self_modulate = Color(0.8, 1.0, 0.8)
	pass
