@tool
class_name ChoiceDef extends FlowDefinition

@export var choice_dic: Dictionary = { "A": {}, "B": {}}

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/choice_node.tscn"

var choice: int = -1	


func get_runtime_type() -> StringName:
	return &"choice"


func get_runtime_params() -> Dictionary:
	return {
		"choices": choice_dic.keys(),
	}

func _is_done() -> bool:
	return choice != -1 

func execute(dialogue_player: Node) -> FlowDefinition:
	return null

func _node_init(node: DialogueNode) -> void:
	return
	
func _capture(node: DialogueNode) ->void:
	choice_dic = {}
	for child in node.get_children():
		if child is ChoiceItem:
			choice_dic[child.text_edit.text] = {}
	
func get_next_flow() -> FlowDefinition:
	var flow = get_connected(false, choice)
	if flow is FlowDefinition:
		return flow
	
	return null
