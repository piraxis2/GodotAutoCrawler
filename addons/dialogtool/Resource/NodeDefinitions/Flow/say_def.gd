@tool
class_name SayDef extends FlowDefinition

@export_file("*.png", "*.jpg") var portrait: String = "empty"
@export var speaker: String = "empty"
@export var say_text: String = "empty"


func _node_init(node: DialogueNode) -> void:
	definition_node = load("res://addons/dialogtool/Node/DefinitionNode/say_node.tscn").instantiate()
	definition_node.get_node("PanelContainer/PortraitPath").text = portrait	
	definition_node.get_node("SpeakerTargetTextEdit").text = speaker	
	definition_node.get_node("SayTextEdit2").text = say_text 
	node.add_child(definition_node)
	node.set_slot(1, true, node.port_type.flow, Color.WHITE, true, node.port_type.flow, Color.WHITE)
	
func _capture() -> void:
	portrait = definition_node.get_node("PanelContainer/PortraitPath").text
	speaker = definition_node.get_node("SpeakerTargetTextEdit").text
	say_text = definition_node.get_node("SayTextEdit2").text	
	pass

func _run() -> void:
	pass
