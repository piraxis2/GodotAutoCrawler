@tool
# 경로 기반 extends: 전역 class_name 캐시에 의존하지 않아 캐시가 낡아도 로드된다.
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# Portrait 노드(show/hide/expression) 공통 에디터 UI.
# 노드별 상태를 갖지 않는다: 위젯은 node meta로 다시 찾으므로 인스턴스 하나가
# 모든 Portrait 노드를 처리한다(registry에서 세 type에 같은 인스턴스를 등록).
#
# 노출 필드는 노드 type으로 결정한다. Hide는 slot/transition만 노출하고,
# Show/Expression은 slot/texture_path/actor/expression/transition을 노출한다.
# capture는 위젯에 실제 존재하는 필드 키만 반환하므로, Hide에서 노출하지 않는
# Definition 필드는 _capture에서 덮어써지지 않고 보존된다.

const WIDGET_META := &"portrait_widget"
const VALUE_NAME := "value"
const SLOT_OPTIONS := ["left", "center", "right"]
const TEXT_FIELDS := ["texture_path", "actor", "expression", "transition"]
const TEXTURE_PATH_EDIT_SCRIPT := preload("res://addons/dialogtool/Editor/Adapter/portrait_texture_path_edit.gd")


func _fields_for(node: DialogueNode) -> Array:
	var type: StringName = &""
	if node.definition:
		type = node.definition.get_runtime_type()
	if type == &"portrait_hide":
		return ["slot", "transition"]
	return ["slot", "texture_path", "actor", "expression", "transition"]


func apply_params(node: DialogueNode, params: Dictionary) -> void:
	var widget := VBoxContainer.new()
	widget.custom_minimum_size = Vector2(280, 0)
	for field in _fields_for(node):
		if field == "slot":
			widget.add_child(_make_slot_row(str(params.get("slot", "center"))))
		else:
			var fallback := "none" if field == "transition" else ""
			widget.add_child(_make_text_row(field, str(params.get(field, fallback))))
	node.add_child(widget)
	# Flow 입력 1개(왼쪽) + Flow 출력 1개(오른쪽). delete_button이 row 0이므로 위젯은 row 1.
	node.set_slot(1, true, DialogueNode.port_type.flow, Color.WHITE, true, DialogueNode.port_type.flow, Color.WHITE)
	node.set_meta(WIDGET_META, widget)


func capture_params(node: DialogueNode) -> Dictionary:
	if not node.has_meta(WIDGET_META):
		return {}
	var widget = node.get_meta(WIDGET_META)
	if widget == null or not is_instance_valid(widget):
		return {}

	var result := {}
	if widget.has_node("slot"):
		var option: OptionButton = widget.get_node("slot").get_node(VALUE_NAME)
		result["slot"] = option.get_item_text(option.selected) if option.selected >= 0 else "center"
	for field in TEXT_FIELDS:
		if widget.has_node(field):
			var line: LineEdit = widget.get_node(field).get_node(VALUE_NAME)
			result[field] = line.text
	return result


func _make_slot_row(current: String) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.name = "slot"
	row.add_child(_make_label("slot"))
	var option := OptionButton.new()
	option.name = VALUE_NAME
	for i in SLOT_OPTIONS.size():
		option.add_item(SLOT_OPTIONS[i], i)
	var idx := SLOT_OPTIONS.find(current)
	if idx < 0:
		# 알 수 없는 저장 값은 임시 항목으로 추가해 그대로 보존한다(저장/재로드 값 보존).
		# 런타임 정규화(center 대체)와 달리 원본 리소스를 조용히 바꾸지 않는다.
		# 사용자가 유효한 slot을 명시적으로 선택하면 그때 교체된다.
		idx = option.item_count
		option.add_item(current)
	option.selected = idx
	option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(option)
	return row


func _make_text_row(field: String, current: String) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.name = field
	row.add_child(_make_label(field))
	var line: LineEdit = TEXTURE_PATH_EDIT_SCRIPT.new() if field == "texture_path" else LineEdit.new()
	line.name = VALUE_NAME
	line.text = current
	line.custom_minimum_size = Vector2(170, 0)
	line.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	if field == "texture_path":
		line.placeholder_text = "Drop Texture2D or enter res:// path"
		line.tooltip_text = "Drop one Texture2D resource from FileSystem"
	row.add_child(line)
	return row


func _make_label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.custom_minimum_size = Vector2(95, 0)
	return label
