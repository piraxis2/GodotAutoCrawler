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
	clear_item()
	set_slot(1, true, port_type.flow, color_dic["flow"], false, 0, Color.WHITE)
	for i in range(count):
		var item: ChoiceItem = choice_item.instantiate()
		item.label.text = char(i + 65)
		if i < texts.size():
			item.text_edit.text = str(texts[i])
		add_child(item)
		set_slot(i + 2, true, port_type.data, color_dic["input"], true, port_type.flow, color_dic["flow"])
	_reattach_connections(saved, count)


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
# 유효 포트: 출력 0..count-1 (flow), 입력 0(flow) + 1..count(선택지별 data).
func _reattach_connections(saved: Array, count: int) -> void:
	var graph = get_parent()
	if graph == null:
		return
	for c in saved:
		if c.from_node == name and c.from_port >= count:
			continue
		if c.to_node == name and c.to_port > count:
			continue
		graph.connect_node(c.from_node, c.from_port, c.to_node, c.to_port)


func clear_item() -> void:
	clear_all_slots()
	for child in get_children():
		if child is ChoiceItem:
			remove_child(child)
			child.queue_free()
