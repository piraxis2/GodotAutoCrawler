@tool
extends DialogueNode
class_name ExpressionGrapheNode
var left_slot_count: int = 0

@onready var code_edit: CodeEdit = $CodeEdit

func _ready() -> void:
	super._ready()
	set_slot(0, false, 0, Color.WHITE, true, 0, Color.DEEP_PINK)
	return

func _on_add_input_button_up() -> void:
	var label = load("res://addons/dialogtool/Node/Sub/expression_input_node.tscn").instantiate()
	$HBoxContainer.add_sibling(label)
	update_input_slot()

func _on_build_button_up() -> void:
	print(definition.build())
	
func update_input_slot() -> void:
	clear_all_slots()
	set_slot(0, false, 0, Color.WHITE, true, port_type.data, Color.DEEP_PINK)
	for i in range(get_child_count() - 4):
		set_slot(i + 2, true, port_type.data, Color.AQUAMARINE, false, 0, Color.WHITE)
