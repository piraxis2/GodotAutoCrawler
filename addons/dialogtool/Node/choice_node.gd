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
		update_item(definition.choice_dic.size())

func _process(delta: float) -> void:
	set_deferred("size", get_combined_minimum_size())
	
func update_item(value: float) -> void:
	clear_item()
	set_slot(1, true, port_type.flow, color_dic["flow"], false, 0, Color.WHITE)
	for i in range(value as int):
		var item: ChoiceItem = choice_item.instantiate()
		item.label.text = char(i + 65)
		var deffered_package = func():
			add_child(item)
			set_slot(i + 2, true, port_type.data, color_dic["input"], true, port_type.flow, color_dic["flow"])
			
		deffered_package.call_deferred()
			
		
func clear_item() -> void:
	clear_all_slots()
	var count = get_child_count()
	
	for i in range(count):
		var child = get_child(i)
		if child is ChoiceItem:
			call_deferred("remove_child", child)
			child.queue_free()
