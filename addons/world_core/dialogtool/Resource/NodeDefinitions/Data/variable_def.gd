@tool
class_name VariableDef extends DataDefinition

@export var variable_name: String
@export var variable_type: AllowedVariables
@export var variable: Variant
enum AllowedVariables {
	NIL = TYPE_NIL,
	BOOL = TYPE_BOOL,
	INT = TYPE_INT,
	FLOAT = TYPE_FLOAT,
	STRING = TYPE_STRING,
	VECTOR2 = TYPE_VECTOR2,
	VECTOR3 = TYPE_VECTOR3,
	COLOR = TYPE_COLOR,
	RANDOM = 100,
}


func get_runtime_type() -> StringName:
	return &"variable"


func get_runtime_params() -> Dictionary:
	var params := {
		"name": variable_name,
		"variable_type": variable_type,
	}
	# RANDOM은 [min, max, Callable] 형태다. Callable은 직렬화도 안 되고 쓸 수 있는
	# 값도 아니므로, 범위만 저장하고 런타임에서 다시 생성하게 한다.
	if variable_type == AllowedVariables.RANDOM and variable is Array and variable.size() >= 2:
		params["random"] = true
		params["random_min"] = int(variable[0])
		params["random_max"] = int(variable[1])
		params["value"] = null
	else:
		params["value"] = variable
	return params


func _get_dialogue_node() -> String:
	return "res://addons/world_core/dialogtool/Node/variable_node.tscn"

func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {})

func _capture(node: DialogueNode) ->void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var node_value: Dictionary = adapter.capture_params(node)
	if node_value.has("name"):
		variable_name = node_value["name"]
	if node_value.has("type"):
		variable_type = node_value["type"]
	if node_value.has("variable"):
		variable = node_value["variable"]

func _get_data_output(port: int) -> Variant:
	if variable_type == AllowedVariables.RANDOM:
		return variable[2].call()
	else:
		return variable
