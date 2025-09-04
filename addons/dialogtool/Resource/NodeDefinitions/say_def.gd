@tool
extends NodeDefinition
class_name SayDef

var definition_node: Node

@export_file("*.png", "*.jpg") var portrait: String = "empty"
@export var speaker: String = "empty"
@export var say_text: String = "empty"


func _node_init(node: DialogueNode) -> void:
	node.title = "say"
	definition_node = load("res://addons/dialogtool/Node/DefinitionNode/say_node.tscn").instantiate()

	node.set_definition_node(definition_node)
	node.set_slot(0, true, 0, Color.WHITE, true, 0, Color.WHITE)
	
	definition_node.get_node("PanelContainer/PortraitPath").text = portrait	
	definition_node.get_node("SpeakerTargetTextEdit").text = speaker	
	definition_node.get_node("SayTextEdit2").text = say_text 

func _capture() -> void:
	portrait = definition_node.get_node("PanelContainer/PortraitPath").text
	speaker = definition_node.get_node("SpeakerTargetTextEdit").text
	say_text = definition_node.get_node("SayTextEdit2").text	
	pass
