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

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/variable_node.tscn"

func _node_init(node: DialogueNode) -> void:
	definition_node = node

func _capture() ->void:
	var node_value = definition_node.get_value()
	variable_name = node_value["name"]
	variable_type = node_value["type"]
	variable = node_value["variable"]

func _get_data_output(port: int) -> Variant:
	if variable_type == AllowedVariables.RANDOM:
		return variable[2].call()
	else:
		return variable
