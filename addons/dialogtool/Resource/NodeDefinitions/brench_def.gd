@tool
extends NodeDefinition
class_name BrenchDef

func _node_init(node: DialogueNode) -> void:
	node.set_slot(0, true, 0, Color.WHITE, false, 0, Color.WHITE)
	node.self_modulate = Color(0.8, 1.0, 0.8)
	pass
