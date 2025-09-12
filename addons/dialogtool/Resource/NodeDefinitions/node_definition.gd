@tool
extends Resource
class_name NodeDefinition 

var definition_node: Node
var node_id : int
var position: Vector2


func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/dialogue_node.tscn"
	

func _node_init(node: DialogueNode) -> void:
	pass

func _capture() ->void:
	pass
	
func _get_definition_node() -> Node:
	return definition_node

func _get_data_output(port: int) -> Variant:
	return null
