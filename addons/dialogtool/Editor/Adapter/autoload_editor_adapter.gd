@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# AutoLoad 노드의 캡처 (AutoLoadDef._capture에서 추출). UI는 autoload_node.tscn이
# 제공한다. (에디터 _get_data_output가 노드 참조를 쓰므로, 노드 캐싱은 정의 쪽
# _node_init에 남겨 둔다 — 이는 UI 코드가 아니라 데이터 출력용 참조다.)

func capture_params(node: DialogueNode) -> Dictionary:
	var a_idx: int = node.autoload_option.get_selected_id()
	var p_idx: int = node.property_option.get_selected_id()
	return {
		"auto_load_idx": a_idx,
		"property_idx": p_idx,
		"autoload_name": node.autoload_option.get_item_text(a_idx),
		"property_name": node.property_option.get_item_text(p_idx),
	}
