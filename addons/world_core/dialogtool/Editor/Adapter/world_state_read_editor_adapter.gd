@tool
# 경로 기반 extends: 전역 class_name 캐시에 의존하지 않아 캐시가 낡아도 로드된다.
extends "res://addons/world_core/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# state_read 노드의 에디터 UI 어댑터 (DT-013 Step 2).
# - generic data output 포트 하나를 slot 1(Row, key/type 위젯 행)에 둔다. editor.gd가 등록한
#   data↔boolean 호환으로 Branch/Choice의 boolean 조건 입력에도 연결된다(ADR-015 D2).
# - key/type 위젯과 summary는 WorldStateReadNode가 소유한다. 어댑터는 슬롯 설정과 params↔노드 값을 잇는다.

# 출력 포트를 두는 slot. _ready에서 delete_button이 child 0으로 이동하므로 Row는 slot 1이다.
const _OUTPUT_SLOT := 1


func apply_params(node: DialogueNode, params: Dictionary) -> void:
	# generic data output 포트(입력 없음). data↔boolean 호환으로 boolean 입력에도 연결 가능.
	node.set_slot(
		_OUTPUT_SLOT,
		false, DialogueNode.port_type.data, Color.WHITE,
		true, DialogueNode.port_type.data, DialogueNode.color_dic["output"])
	if node.has_method("set_value_type"):
		node.set_value_type(int(params.get("value_type", WorldStateReadDef.READ_VALUE_TYPES[0])))
	if node.has_method("set_key"):
		node.set_key(params.get("key", &""))


func capture_params(node: DialogueNode) -> Dictionary:
	var result := {}
	if node.has_method("get_key"):
		result["key"] = node.get_key()
	if node.has_method("get_value_type"):
		result["value_type"] = node.get_value_type()
	return result
