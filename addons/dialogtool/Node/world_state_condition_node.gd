@tool
class_name WorldStateConditionNode extends DialogueNode

# state_condition Data 노드의 에디터 GraphNode (DT-008 Step 2).
# boolean output 포트 하나와 ConditionSet picker를 갖는다. UI 구성(슬롯)과 값 캡처는
# world_state_condition_editor_adapter에 위임한다(Definition은 UI를 모른다).

# invalid/null summary를 그래프 위에서 구분되게 표시하는 색(Step 0 D3).
const _INVALID_MODULATE := Color(1.0, 0.55, 0.55)

@onready var picker = $HBoxContainer/ConditionSetPicker
@onready var clear_button: Button = $HBoxContainer/Clear
# ConditionSet의 사람이 읽을 수 있는 summary 표시(DT-012 Step 2). picker는 path를 유지하고
# 이 label은 별도로 의미 요약을 보여 준다.
@onready var summary_label: Label = $SummaryLabel


func _ready() -> void:
	# super._ready()가 definition._node_init -> adapter.apply_params를 호출해 슬롯과 picker를 채운다.
	# @onready(picker/clear_button/summary_label)는 이 _ready 본문 직전에 이미 할당돼 있어
	# apply_params -> set_condition_set -> _refresh_summary에서 접근 가능.
	super._ready()
	picker.condition_set_changed.connect(_on_picker_changed)
	clear_button.pressed.connect(_on_clear_pressed)


func _process(_delta: float) -> void:
	set_deferred("size", get_combined_minimum_size())


func _on_picker_changed(_cs) -> void:
	# 위젯 값이 바뀌면 deferred capture로 Definition에 반영한다(VariableNode와 동일 패턴).
	_refresh_summary()
	if definition:
		definition.call_deferred("_capture", self)


func _on_clear_pressed() -> void:
	picker.condition_set = null
	_refresh_summary()
	if definition:
		definition.call_deferred("_capture", self)


# 어댑터 apply_params/capture_params가 사용하는 값 접근점.
func set_condition_set(cs) -> void:
	picker.condition_set = cs if cs is ConditionSet else null
	# adapter apply/load 시점 갱신(live external edit 구독은 하지 않음, Step 0 D8).
	_refresh_summary()


func get_condition_set() -> ConditionSet:
	return picker.condition_set


# picker가 보관한 ConditionSet을 provider 없이 요약해 summary label/tooltip에 표시한다.
# validate-first 계약은 ConditionSummary가 책임진다(invalid/null은 description보다 우선).
func _refresh_summary() -> void:
	if summary_label == null:
		return
	var result := ConditionSummary.summarize(picker.condition_set)
	summary_label.text = result["summary"]
	# tooltip에는 full summary(+ description/오류)를 싣고, 외부 .tres면 참조 path도 병기한다(Step 0 D9).
	var tip: String = result["tooltip"]
	var cs: ConditionSet = picker.condition_set
	if cs != null and cs.resource_path != "":
		tip = "%s\n\n%s" % [cs.resource_path, tip]
	summary_label.tooltip_text = tip
	summary_label.modulate = Color.WHITE if bool(result["valid"]) else _INVALID_MODULATE
