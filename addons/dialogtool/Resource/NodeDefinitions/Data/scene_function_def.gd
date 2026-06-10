@tool
class_name SceneFunctionDef extends DataDefinition

@export var func_name: String

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/scene_function_node.tscn"

func _node_init(node: DialogueNode) -> void:
	return

func _capture(node: DialogueNode) ->void:
	var Option: OptionButton = node.option_button 
	func_name = Option.get_selected_metadata() 
	return

func _get_data_output(port: int) -> Variant:
	return null
