@tool
class_name SayDef extends FlowDefinition

@export_file("*.png", "*.jpg") var portrait: String = "empty"
@export var speaker: String
@export var say_text: String  

var read_the_entire_article: bool = false


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

# 에디터 UI는 어댑터에 위임한다 (NodeTypeRegistry / SayEditorAdapter 참조).
# 위의 export 필드가 저장/로드의 단일 진실 원천(source of truth)으로 남으므로
# 기존 .tres 파일은 그대로 로드된다. 어댑터는 UI <-> params 중개만 담당한다.
func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	adapter.apply_params(node, {"speaker": speaker, "say_text": say_text, "portrait": portrait})

func _capture(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	if params.has("speaker"):
		speaker = params["speaker"]
	if params.has("say_text"):
		say_text = params["say_text"]
	if params.has("portrait"):
		portrait = params["portrait"]
