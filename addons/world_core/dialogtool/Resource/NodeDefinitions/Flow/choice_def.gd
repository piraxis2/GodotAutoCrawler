@tool
class_name ChoiceDef extends FlowDefinition

@export var choice_dic: Dictionary = { "A": {}, "B": {}}
# 순서가 보장되는 선택지 텍스트의 권위 있는 목록. 인덱스 == 출력 포트 인덱스.
# `choice_dic`은 기존에 저장된 리소스와의 하위 호환을 위해서만 유지한다.
@export var choices: Array[String] = []

func _get_dialogue_node() -> String:
	return "res://addons/world_core/dialogtool/Node/choice_node.tscn"

var choice: int = -1


func get_runtime_type() -> StringName:
	return &"choice"


func get_runtime_params() -> Dictionary:
	# 선언 순서대로 선택지를 반환해 index -> from_port 매핑이 일관되게 유지되도록 한다.
	var ordered: Array = choices.duplicate()
	if ordered.is_empty() and not choice_dic.is_empty():
		# `choices`가 없던 시절에 저장된 리소스를 위한 폴백.
		for key in choice_dic.keys():
			ordered.append(str(key))
	return {
		"choices": ordered,
	}

func _is_done() -> bool:
	return choice != -1 

func execute(dialogue_player: Node) -> FlowDefinition:
	return null

func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {})

func _capture(node: DialogueNode) ->void:
	# 어댑터가 자식 순서대로 선택지 텍스트를 읽어온다(중복/빈 텍스트도 안전).
	# choice_dic은 하위 호환을 위해 그 목록에서 다시 만든다.
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var captured: Array = adapter.capture_params(node).get("choices", [])
	choices = []
	choice_dic = {}
	for text in captured:
		choices.append(text)
		choice_dic[text] = {}
	
func get_next_flow() -> FlowDefinition:
	var flow = get_connected(false, choice)
	if flow is FlowDefinition:
		return flow
	
	return null
