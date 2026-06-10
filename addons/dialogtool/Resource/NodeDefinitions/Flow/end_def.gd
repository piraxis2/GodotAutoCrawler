@tool
class_name EndDef extends FlowDefinition


func get_runtime_type() -> StringName:
	return &"end"


func _node_init(node: DialogueNode) -> void:
	node.set_slot(0, true, 0, Color.WHITE, false, 0, Color.WHITE)
	node.self_modulate = Color(0.8, 1.0, 0.8)
	pass

func _is_done() -> bool:	
	return true

func execute(dialogue_player: Node) -> FlowDefinition:
	return null

func _capture(node: DialogueNode) ->void:
	pass
