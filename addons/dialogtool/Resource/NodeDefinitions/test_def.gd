@tool
class_name TestDef extends DialogueDefinition

func get_runtime_type() -> StringName:
	return &"test"

func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {})

func _capture(node: DialogueNode) ->void:
	pass
