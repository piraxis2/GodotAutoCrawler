@tool
class_name EndDef extends FlowDefinition


func get_runtime_type() -> StringName:
	return &"end"


func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {})

func _is_done() -> bool:	
	return true

func execute(dialogue_player: Node) -> FlowDefinition:
	return null

func _capture(node: DialogueNode) ->void:
	pass
