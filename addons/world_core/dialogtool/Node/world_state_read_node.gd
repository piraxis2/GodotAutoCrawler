@tool
class_name WorldStateReadNode extends DialogueNode

# state_read Data 노드의 에디터 GraphNode (DT-013 Step 2).
# key LineEdit + type OptionButton + summary label + generic data output 포트 하나를 갖는다.
# 슬롯(data output) 설정과 params 캡처는 world_state_read_editor_adapter에 위임한다(Definition은 UI를 모른다).
# summary("<key> : <TYPE>" 또는 "No State Key") 표시는 이 노드가 직접 갱신한다.

# key가 없는 invalid 상태를 그래프 위에서 구분되게 표시하는 색(WorldStateConditionNode와 동일 패턴).
const _INVALID_MODULATE := Color(1.0, 0.55, 0.55)

@onready var key_edit: LineEdit = $Row/KeyEdit
@onready var type_option: OptionButton = $Row/TypeOption
@onready var summary_label: Label = $SummaryLabel


func _ready() -> void:
	# type 옵션을 super._ready()(=adapter.apply_params → set_value_type) 전에 채워야 선택이 적용된다.
	_populate_type_options()
	super._ready()
	key_edit.text_changed.connect(_on_field_changed)
	type_option.item_selected.connect(_on_type_selected)


func _process(_delta: float) -> void:
	set_deferred("size", get_combined_minimum_size())


func _populate_type_options() -> void:
	type_option.clear()
	for i in WorldStateReadDef.READ_VALUE_TYPES.size():
		var t: int = WorldStateReadDef.READ_VALUE_TYPES[i]
		type_option.add_item(WorldStateReadDef.type_label(t), i)
		type_option.set_item_metadata(i, t)
	type_option.selected = 0


func _on_field_changed(_text: String) -> void:
	_refresh_summary()
	if definition:
		definition.call_deferred("_capture", self)


func _on_type_selected(_index: int) -> void:
	_refresh_summary()
	if definition:
		definition.call_deferred("_capture", self)


# --- 어댑터 apply_params/capture_params가 사용하는 값 접근점 ---

func set_key(k) -> void:
	key_edit.text = String(k) if (k is String or k is StringName) else ""
	_refresh_summary()


func get_key() -> StringName:
	return StringName(key_edit.text)


func set_value_type(vt: int) -> void:
	var sel := 0
	for i in type_option.item_count:
		if int(type_option.get_item_metadata(i)) == vt:
			sel = i
			break
	type_option.selected = sel
	_refresh_summary()


func get_value_type() -> int:
	if type_option.selected >= 0:
		return int(type_option.get_selected_metadata())
	return WorldStateReadDef.READ_VALUE_TYPES[0]


# summary label/tooltip 갱신. key가 비면 "No State Key"(invalid 색), 있으면 "<key> : <TYPE>".
func _refresh_summary() -> void:
	if summary_label == null:
		return
	var key_str := key_edit.text.strip_edges()
	if key_str.is_empty():
		summary_label.text = "No State Key"
		summary_label.tooltip_text = "State key를 입력하세요 (예: player.gold)."
		summary_label.modulate = _INVALID_MODULATE
		return
	var label := WorldStateReadDef.type_label(get_value_type())
	summary_label.text = "%s : %s" % [key_str, label]
	summary_label.tooltip_text = summary_label.text
	summary_label.modulate = Color.WHITE
