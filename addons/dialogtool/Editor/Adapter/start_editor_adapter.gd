@tool
# 경로 기반 extends: 전역 class_name 캐시에 의존하지 않아 캐시가 낡아도 로드된다.
# (레지스트리가 preload로 생성하므로 class_name은 불필요.)
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# Start 노드의 에디터 UI (StartDef._node_init에서 추출). 캡처할 필드는 없다.

func apply_params(node: DialogueNode, _params: Dictionary) -> void:
	node.id = 0
	node.delete_button.queue_free()
	node.title = "start"
	node.add_child(Control.new())
	node.add_right_port_info(0, DialogueNode.port_type.flow, Color.WHITE)
	node.self_modulate = Color(0.8, 1.0, 0.8)
