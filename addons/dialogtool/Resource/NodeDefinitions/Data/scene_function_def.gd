@tool
class_name SceneFunctionDef extends DataDefinition

@export var func_name: String

func get_runtime_type() -> StringName:
	return &"scene_function"

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/scene_function_node.tscn"

func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {})

func _capture(node: DialogueNode) ->void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	if params.has("func_name"):
		func_name = params["func_name"]

func _get_data_output(port: int) -> Variant:
	return null
