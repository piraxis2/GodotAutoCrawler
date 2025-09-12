@tool
extends DialogueNode
class_name ExpressionGrapheNode
var left_slot_count: int = 0

@onready var code_edit: CodeEdit = $CodeEdit

@export var input: Dictionary = {}

func _ready() -> void:
	super._ready()
	update_input_slot()
	return

func _on_add_input_button_up() -> void:
	var label = load("res://addons/dialogtool/Node/Sub/expression_input_node.tscn").instantiate()
	label.on_hold.connect(_on_input_hold)
	label.on_delete.connect(_on_input_deleted)
	$HBoxContainer.add_sibling(label)
	update_input_slot()
	
func _on_input_hold(text: String) -> void:
	input[text] = null
	return
	
func _on_input_deleted(text: String) -> void:
	input.erase(text)
	return

func _on_build_button_up() -> void:
	print(definition.build())
	
func update_input_slot() -> void:
	clear_all_slots()
	set_slot(0, false, port_type.data, Color.WHITE, true, port_type.data, Color.DEEP_PINK)
	for i in range(get_child_count() - 4):
		set_slot(i + 2, true, port_type.data, Color.AQUAMARINE, false, 0, Color.WHITE)
