@tool
class_name WorldStateConditionNode extends DialogueNode

# state_condition Data 노드의 에디터 GraphNode (DT-008 Step 2).
# boolean output 포트 하나와 ConditionSet picker를 갖는다. UI 구성(슬롯)과 값 캡처는
# world_state_condition_editor_adapter에 위임한다(Definition은 UI를 모른다).

@onready var picker = $HBoxContainer/ConditionSetPicker
@onready var clear_button: Button = $HBoxContainer/Clear


func _ready() -> void:
	# super._ready()가 definition._node_init -> adapter.apply_params를 호출해 슬롯과 picker를 채운다.
	# @onready(picker/clear_button)는 이 _ready 본문 직전에 이미 할당돼 있어 apply_params에서 접근 가능.
	super._ready()
	picker.condition_set_changed.connect(_on_picker_changed)
	clear_button.pressed.connect(_on_clear_pressed)


func _process(_delta: float) -> void:
	set_deferred("size", get_combined_minimum_size())


func _on_picker_changed(_cs) -> void:
	# 위젯 값이 바뀌면 deferred capture로 Definition에 반영한다(VariableNode와 동일 패턴).
	if definition:
		definition.call_deferred("_capture", self)


func _on_clear_pressed() -> void:
	picker.condition_set = null
	if definition:
		definition.call_deferred("_capture", self)


# 어댑터 apply_params/capture_params가 사용하는 값 접근점.
func set_condition_set(cs) -> void:
	picker.condition_set = cs if cs is ConditionSet else null


func get_condition_set() -> ConditionSet:
	return picker.condition_set
