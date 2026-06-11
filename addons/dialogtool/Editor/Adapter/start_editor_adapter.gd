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
	# Effect 출력(비대기, ADR-005). flow 출력 아래 row에 두어 flow는 port 0,
	# effect는 port 1로 배치한다(기존 flow port index 불변).
	var effect_label := Label.new()
	effect_label.text = "effect"
	effect_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	effect_label.tooltip_text = "비대기 Effect 출력(주황): 연결된 Portrait 명령을 실행한 뒤 주 Flow로 진행합니다. 일반 Flow 포트와 다릅니다."
	node.add_child(effect_label)
	node.add_right_port_info(1, DialogueNode.port_type.effect, DialogueNode.EFFECT_PORT_COLOR)
	node.self_modulate = Color(0.8, 1.0, 0.8)
