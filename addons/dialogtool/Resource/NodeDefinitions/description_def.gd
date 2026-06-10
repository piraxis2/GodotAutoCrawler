@tool
class_name DescriptionDef extends DialogueDefinition

@export var description: String
@export var node_color: Color = Color(0.925, 0.667, 0.444, 0.8)


func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/description_node.tscn"

func _node_init(node: DialogueNode) -> void:
	node.color_picker.color = node_color
	node.modulate = node_color
	node.description_label.text = description
	
func _capture(node: DialogueNode) ->void:
	node_color = node.color_picker.color
	description = node.description_label
