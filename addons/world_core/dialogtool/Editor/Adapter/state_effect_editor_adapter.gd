@tool
# 경로 기반 extends: 전역 class_name 캐시에 의존하지 않아 캐시가 낡아도 로드된다.
extends "res://addons/world_core/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# State Set/Add Effect 노드(state_set/state_add) 공통 에디터 UI (DT-009 Step 3).
# 노드별 상태를 갖지 않는다: 위젯은 node meta로 다시 찾으므로 인스턴스 하나가 두 type을 처리한다.
#
# UI: key(LineEdit) + type(OptionButton) + value/delta(LineEdit) + Effect 입력 포트(주황, row 2).
# - state_set: 5타입(bool/int/float/String/StringName), 값 필드 "value".
# - state_add: INT/FLOAT만(다른 타입 literal을 만들 수 없음 — ADR-010 D7), 값 필드 "delta".
# capture는 선택 타입으로 텍스트를 coerce해 typeof를 확정한다(StateEffectDef.coerce_text).

const WIDGET_META := &"state_effect_widget"
const VALUE_NAME := "val"
# 허용 타입 목록은 StateEffectDef에 단일 정의를 둔다(Definition validate_literal과 공유).
const SET_TYPES: Array = StateEffectDef.SET_VALUE_TYPES
const ADD_TYPES: Array = StateEffectDef.ADD_VALUE_TYPES


func _is_add(node: DialogueNode) -> bool:
	return node.definition != null and node.definition.get_runtime_type() == &"state_add"


func apply_params(node: DialogueNode, params: Dictionary) -> void:
	var add := _is_add(node)
	var vkey := "delta" if add else "value"
	var allowed: Array = ADD_TYPES if add else SET_TYPES
	var cur_type: int = int(params.get(vkey + "_type", allowed[0]))
	if not (cur_type in allowed):
		cur_type = allowed[0]

	var widget := VBoxContainer.new()
	widget.custom_minimum_size = Vector2(280, 0)
	widget.add_child(_make_text_row("key", str(params.get("key", "")), "state key (e.g. player.gold)"))
	widget.add_child(_make_type_row(allowed, cur_type))
	widget.add_child(_make_text_row(vkey, _stringify(params.get(vkey)), ""))
	node.add_child(widget)

	# Effect 입력(비대기, ADR-005/010). state 노드는 leaf이므로 Flow/Effect 출력은 없다.
	# row 0=delete_button, row 1=widget, row 2=effect_label → Effect 입력은 row 2.
	var effect_label := Label.new()
	effect_label.text = "effect"
	effect_label.tooltip_text = "비대기 Effect 입력(주황): Start/Say/Choice의 Effect 출력에 연결합니다."
	node.add_child(effect_label)
	node.set_slot(2, true, DialogueNode.port_type.effect, DialogueNode.EFFECT_PORT_COLOR, false, 0, Color.WHITE)
	node.set_meta(WIDGET_META, widget)


func capture_params(node: DialogueNode) -> Dictionary:
	if not node.has_meta(WIDGET_META):
		return {}
	var widget = node.get_meta(WIDGET_META)
	if widget == null or not is_instance_valid(widget):
		return {}

	var add := _is_add(node)
	var vkey := "delta" if add else "value"
	var allowed: Array = ADD_TYPES if add else SET_TYPES
	var result := {}
	if widget.has_node("key"):
		result["key"] = StringName((widget.get_node("key").get_node(VALUE_NAME) as LineEdit).text)
	var sel_type: int = allowed[0]
	if widget.has_node("type"):
		var opt: OptionButton = widget.get_node("type").get_node(VALUE_NAME)
		if opt.selected >= 0:
			sel_type = int(opt.get_selected_metadata())
	result[vkey + "_type"] = sel_type
	if widget.has_node(vkey):
		var line: LineEdit = widget.get_node(vkey).get_node(VALUE_NAME)
		result[vkey] = StateEffectDef.coerce_text(line.text, sel_type)
	return result


func _stringify(v: Variant) -> String:
	if v == null:
		return ""
	if typeof(v) == TYPE_BOOL:
		return "true" if v else "false"
	return str(v)


func _make_text_row(field: String, current: String, placeholder: String) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.name = field
	row.add_child(_make_label(field))
	var line := LineEdit.new()
	line.name = VALUE_NAME
	line.text = current
	line.custom_minimum_size = Vector2(170, 0)
	line.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	if not placeholder.is_empty():
		line.placeholder_text = placeholder
	row.add_child(line)
	return row


func _make_type_row(allowed: Array, current: int) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.name = "type"
	row.add_child(_make_label("type"))
	var opt := OptionButton.new()
	opt.name = VALUE_NAME
	var sel := 0
	for i in allowed.size():
		opt.add_item(type_string(allowed[i]), i)
		opt.set_item_metadata(i, allowed[i])
		if allowed[i] == current:
			sel = i
	opt.selected = sel
	opt.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(opt)
	return row


func _make_label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.custom_minimum_size = Vector2(70, 0)
	return label
