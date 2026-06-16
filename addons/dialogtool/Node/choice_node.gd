@tool
class_name ChoiceNode extends DialogueNode
	
@onready var slider: HSlider = $HBoxContainer/HSlider
@onready var choice_item = load("res://addons/dialogtool/Node/Sub/choice_item.tscn")
@export var min_node_size: Vector2 = Vector2(200, 50)

var output_port_data: Dictionary = {"slot_position": 1, "port_type": DialogueNode.port_type.flow, "color": DialogueNode.color_dic["flow"]}

func _ready() -> void:
	super._ready()
	slider.value_changed.connect(update_item)
	if definition:
		var count: int = definition.choices.size()
		if count == 0:
			count = definition.choice_dic.size()
		update_item(count)

func _process(delta: float) -> void:
	set_deferred("size", get_combined_minimum_size())
	
func update_item(value: float) -> void:
	var count := int(value)
	# 리빌드 동안 이 노드의 연결을 모두 떼어, GraphEdit이 잠깐 사라진 슬롯을
	# 조회하지 않게 한다(right_port_cache 범위 초과 스팸 방지). 동기적으로 처리한다 —
	# 연결이 붙어 있는 상태에서 슬롯을 비우는 것이 에러의 원인이므로, 리빌드 중에는
	# 아무 연결도 붙어 있으면 안 된다.
	var saved := _detach_connections()
	# 재구성 시 텍스트가 사라지지 않도록: 현재 항목의 텍스트를 먼저 보존하고,
	# (로드 직후처럼) 항목이 아직 없으면 definition에 저장된 선택지에서 복원한다.
	var texts := _current_texts()
	if texts.is_empty():
		texts = _saved_texts()
	var old_count := _item_count()
	clear_item()
	set_slot(1, true, port_type.flow, color_dic["flow"], false, 0, Color.WHITE)
	for i in range(count):
		var item: ChoiceItem = choice_item.instantiate()
		item.label.text = char(i + 65)
		if i < texts.size():
			item.text_edit.text = str(texts[i])
		add_child(item)
		set_slot(i + 2, true, port_type.data, color_dic["input"], true, port_type.flow, color_dic["flow"])
	# 항목별 Effect 출력 포트(ADR-010 Step 3b). flow 출력(0..count-1) 다음에 effect 출력(count..2*count-1)을
	# 둔다 — flow/data 포트 index를 보존해 기존 리소스/연결과 호환된다. 선택 시 해당 항목 Effect만 실행된다.
	for i in range(count):
		var effect_label := Label.new()
		effect_label.name = "EffectOut%d" % i
		effect_label.text = "effect %s" % char(i + 65)
		effect_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		effect_label.tooltip_text = "선택지 %s 전용 비대기 Effect 출력(주황): 이 선택지를 고를 때만 실행됩니다." % char(i + 65)
		add_child(effect_label)
		set_slot(count + 2 + i, false, 0, Color.WHITE, true, port_type.effect, EFFECT_PORT_COLOR)
	# 공통 Effect 출력 포트(항목별 포트 뒤, 출력 port 2*count). choice_index 없이 연결돼 어느 선택지에서도
	# 실행되는 Effect용이다. 항목별과 분리된 전용 포트라 capture/recapture에서 choice_index가 부여되지 않는다
	# (공통이 저장 후 항목0 전용으로 변하던 문제 방지 — Step 3b 리뷰 P1).
	var common_label := Label.new()
	common_label.name = "EffectOutCommon"
	common_label.text = "effect (공통)"
	common_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	common_label.tooltip_text = "공통 비대기 Effect 출력(주황): 어느 선택지를 골라도 실행됩니다(choice_index 없음)."
	add_child(common_label)
	set_slot(2 * count + 2, false, 0, Color.WHITE, true, port_type.effect, EFFECT_PORT_COLOR)
	_reattach_connections(saved, count, old_count)


# 현재 ChoiceItem 행(= 선택지) 개수.
func _item_count() -> int:
	var n := 0
	for child in get_children():
		if child is ChoiceItem:
			n += 1
	return n


# capture: effect 출력 포트 index → 선택지 index. 항목별 포트(n..2n-1)만 0..n-1을 돌려주고,
# 공통 포트(2n)나 effect 아닌 포트는 -1(= choice_index 없음 → 공통)을 돌려준다. editor.gd 캡처가 사용.
func effect_choice_index_for_port(port: int) -> int:
	var n := _item_count()
	if port >= n and port < 2 * n:
		return port - n
	return -1


# load: 선택지 index → 항목별 effect 출력 포트 index(범위 밖이면 -1, 호출부가 오류 처리). editor.gd 사용.
func effect_port_for_choice_index(ci: int) -> int:
	var n := _item_count()
	if ci >= 0 and ci < n:
		return n + ci
	return -1


# load: 공통(choice_index 없는) Effect 연결을 잇는 전용 포트 index(= 2n). editor.gd 정규화가 사용.
func common_effect_port() -> int:
	return 2 * _item_count()


# 현재 살아있는 ChoiceItem들의 텍스트(편집 중 보존용).
func _current_texts() -> Array:
	var out: Array = []
	for child in get_children():
		if child is ChoiceItem:
			out.append(child.text_edit.text)
	return out


# definition에 저장된 선택지 텍스트(로드 시 복원용). choices가 비면 구 리소스의
# choice_dic 키로 폴백한다.
func _saved_texts() -> Array:
	if definition == null:
		return []
	if not definition.choices.is_empty():
		return definition.choices.duplicate()
	var keys: Array = []
	for k in definition.choice_dic.keys():
		keys.append(str(k))
	return keys


# 이 노드에 닿은 모든 연결을 끊고, 재연결을 위해 그 목록을 반환한다.
func _detach_connections() -> Array:
	var graph = get_parent()
	if graph == null or not graph.has_method("get_connection_list"):
		return []
	var saved: Array = []
	for c in graph.get_connection_list():
		if c.from_node == name or c.to_node == name:
			saved.append(c)
			graph.disconnect_node(c.from_node, c.from_port, c.to_node, c.to_port)
	return saved


# `count`개로 리사이즈한 뒤에도 포트가 여전히 존재하는 연결만 다시 잇는다.
# 출력: flow(0..count-1) + effect(count..2*count-1). effect 출력은 항목 개수(base)에 의존하므로
#   old_count base에서 new count base로 remap한다(선택지별 Effect 연결을 resize에도 보존, ADR-010 Step 3b).
# 입력: flow-in 0 + 선택지별 data 1..count.
func _reattach_connections(saved: Array, count: int, old_count: int) -> void:
	var graph = get_parent()
	if graph == null:
		return
	for c in saved:
		var from_port: int = c.from_port
		var to_port: int = c.to_port
		if c.from_node == name:
			if from_port < old_count:
				# flow 출력(선택지 from_port). 항목이 남아 있을 때만 유지(index 불변).
				if from_port >= count:
					continue
			elif from_port < 2 * old_count:
				# 항목별 effect 출력(선택지 from_port - old_count). 항목이 남아 있으면 새 base로 remap.
				var item_i := from_port - old_count
				if item_i >= count:
					continue
				from_port = count + item_i
			else:
				# 공통 effect 출력(old base 2*old_count) → 새 base 2*count로 remap(resize에도 보존).
				from_port = 2 * count
		if c.to_node == name and to_port > count:
			continue
		graph.connect_node(c.from_node, from_port, c.to_node, to_port)


func clear_item() -> void:
	clear_all_slots()
	for child in get_children():
		if child is ChoiceItem or (child is Label and str(child.name).begins_with("EffectOut")):
			remove_child(child)
			child.queue_free()
