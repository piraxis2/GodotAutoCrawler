@tool
class_name StartDef extends FlowDefinition


func get_runtime_type() -> StringName:
	return &"start"


func _is_done() -> bool:
	return true

func execute(dialogue_player: Node) -> FlowDefinition:
	dialogue_player.dialogue_resource
	return get_next_flow()

func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {})

func _capture(node: DialogueNode) ->void:
	pass
