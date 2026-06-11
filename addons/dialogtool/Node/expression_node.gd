@tool
class_name ExpressionGrapheNode extends DialogueNode
var left_slot_count: int = 0

@onready var code_edit: CodeEdit = $CodeEdit
@onready var slider: HSlider = $HBoxContainer/HSlider

@export var input: Dictionary = {}
@onready var sibling_position: Node = $HBoxContainer
@onready var build_button: Button = $HBoxContainer/Build


@onready var input_node = load("res://addons/dialogtool/Node/Sub/expression_input_node.tscn")

signal on_change_text_edit(node: DialogueNode)

func _ready() -> void:
	super._ready()
	slider.value_changed.connect(on_slider_update)
	if definition:
		slider.value = (definition as ExpressionValueDef).inputs.size()
	build_button.button_up.connect(_on_build_button_up)

func _on_build_button_up() -> void:
	print("Expression result: ", evaluate_preview())

# 새로 캡처한 스냅샷에 실제 런타임 evaluator를 적용해 식을 미리 본다.
# 먼저 캡처하면 현재 위젯 값(예: Variable의 SpinBox)을 읽으므로, 에디터 build()
# 경로의 낡은 definition에 의존하지 않고 살아있는 입력 값을 반영하며 런타임 동작과
# 일치한다.
func evaluate_preview() -> Variant:
	var graph = get_parent()
	if graph == null or not graph.has_method("capture_current_graphedit"):
		return definition.build() # 폴백: 기존 에디터 build()
	var snapshot = graph.capture_current_graphedit()
	var player := DialoguePlayer.new()
	player.dialogue_resource = snapshot
	var value = player._get_data_value(id)
	player.free()
	return value
	
func on_slider_update(value: float) -> void:
	var temp = input
	input.clear()
	clear_all_slots()
	set_slot(definition.output_port_data["slot_position"], false, definition.output_port_data["port_type"], Color.WHITE, true,  definition.output_port_data["port_type"],  definition.output_port_data["color"])
	var count = get_child_count()
	sibling_position = $HBoxContainer
	for i in range(count):
		var child = get_child(i)
		if child is ExpressionInputNode:
			child.queue_free()
			
	for i in range(value as int):
		var label = input_node.instantiate()
		var key = char(i + 65) #Ascii_A
		label.get_label().text = key
		var lambda_capture_definition = definition
		var lambda = func() -> Variant:
			if Engine.is_editor_hint():
				var connected = get_connected_node(true, i)
				if connected:
					return connected.definition
					
			var connect_definition = lambda_capture_definition.get_connected(true, i)
			if connect_definition:
				return connect_definition
			return null
		input[key] = lambda
		sibling_position.add_sibling(label)
		sibling_position = label
		set_slot(i + 2, true, port_type.data, Color.AQUAMARINE, false, 0, Color.WHITE)
	
	for key in input:
		input[key] = temp[key]
	
	definition.update_input(input)
	


func _on_code_edit_text_changed() -> void:
	on_change_text_edit.emit(self)
