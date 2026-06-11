@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# Say 노드의 에디터 UI. SayDef._node_init/_capture에서 추출한 것.
# 노드별 상태를 갖지 않는다: say_block 위젯은 node meta로 다시 찾으므로,
# 어댑터 인스턴스 하나가 모든 Say 노드를 처리할 수 있다.

const WIDGET_SCENE := "res://addons/dialogtool/Node/say_block.tscn"
const WIDGET_META := &"say_widget"
const SPEAKER_PATH := "SpeakerTargetTextEdit"
const SAY_PATH := "SayTextEdit2"


func apply_params(node: DialogueNode, params: Dictionary) -> void:
	var widget = load(WIDGET_SCENE).instantiate()
	widget.get_node(SPEAKER_PATH).text = params.get("speaker", "")
	widget.get_node(SAY_PATH).text = params.get("say_text", "")
	node.add_child(widget)
	node.set_slot(1, true, DialogueNode.port_type.flow, Color.WHITE, true, DialogueNode.port_type.flow, Color.WHITE)
	node.set_meta(WIDGET_META, widget)


func capture_params(node: DialogueNode) -> Dictionary:
	if not node.has_meta(WIDGET_META):
		return {}
	var widget = node.get_meta(WIDGET_META)
	if widget == null or not is_instance_valid(widget):
		return {}
	return {
		"speaker": widget.get_node(SPEAKER_PATH).text,
		"say_text": widget.get_node(SAY_PATH).text,
	}
