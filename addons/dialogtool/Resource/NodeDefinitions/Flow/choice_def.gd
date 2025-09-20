@tool
class_name ChoiceDef extends FlowDefinition

@export var choice_dic: Dictionary = { "A": {}, "B": {}}

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/choice_node.tscn"

func _node_init(node: DialogueNode) -> void:
	return
	
func _capture() ->void:
	return
	
func _run() -> void:
	return
