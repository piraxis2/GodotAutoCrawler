@tool
class_name SayDef extends FlowDefinition

@export_file("*.png", "*.jpg") var portrait: String = "empty"
@export var speaker: String
@export var say_text: String  

var read_the_entire_article: bool = false

var definition_node: Node #only use in editor


func get_runtime_type() -> StringName:
	return &"say"


func get_runtime_params() -> Dictionary:
	return {
		"portrait": portrait,
		"speaker": speaker,
		"text": say_text,
	}


func _is_done() -> bool:
	return read_the_entire_article

func execute(dialogue_player: Node) -> FlowDefinition:
	var request_data = {
		"type": "display_text",
		"speaker": speaker,
		"say": say_text
					   }
	dialogue_player.ui_request.emit(request_data)
	return null

func _run(player: DialoguePlayer) -> void:
	return

func _node_init(node: DialogueNode) -> void:
	definition_node = load("res://addons/dialogtool/Node/say_block.tscn").instantiate()
	definition_node.get_node("SpeakerTargetTextEdit").text = speaker	
	definition_node.get_node("SayTextEdit2").text = say_text 
	node.add_child(definition_node)
	node.set_slot(1, true, node.port_type.flow, Color.WHITE, true, node.port_type.flow, Color.WHITE)
	
func _capture(node: DialogueNode) -> void:
	speaker = definition_node.get_node("SpeakerTargetTextEdit").text
	say_text = definition_node.get_node("SayTextEdit2").text	
