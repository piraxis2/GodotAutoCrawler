@tool
class_name DescriptionDef extends DialogueDefinition

@export var description: String
@export var node_color: Color = Color(0.925, 0.667, 0.444, 0.8)


func get_runtime_type() -> StringName:
	return &"description"

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/description_node.tscn"

func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {"description": description, "node_color": node_color})

func _capture(node: DialogueNode) ->void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	if params.has("node_color"):
		node_color = params["node_color"]
	if params.has("description"):
		description = params["description"]
