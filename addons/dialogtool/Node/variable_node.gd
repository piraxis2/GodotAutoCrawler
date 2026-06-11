@tool
class_name VariableNode extends DialogueNode

@onready var value_box = $HBoxContainer/MarginContainer
@onready var option_button = $HBoxContainer/OptionButton

var value_node: Node:
	get: return value_box.get_child(0)

func _ready() -> void:
	super._ready()
	option_button.clear()
	option_button.item_selected.connect(select_item)
	var selected: int = 0
	for type in VariableDef.AllowedVariables.keys():
		if VariableDef.AllowedVariables[type] == VariableDef.AllowedVariables.RANDOM:
			option_button.add_icon_item(load("res://addons/dialogtool/Icon/random_icon.png"), type)
		else: 
			var type_name = type_string(VariableDef.AllowedVariables[type])
			if Engine.is_editor_hint():
				option_button.add_icon_item(EditorInterface.get_editor_theme().get_icon(type_name, "EditorIcons"), type_name)
			else:
				option_button.add_item(type_name)

		if definition and definition.variable_type == VariableDef.AllowedVariables[type]:
			selected = option_button.item_count - 1
				
		option_button.set_item_metadata(option_button.item_count - 1, VariableDef.AllowedVariables[type])
	
	option_button.select(selected)	
	select_item(selected)	
	load_value()
	option_button.item_selected.connect(value_update)
	
	
	
func value_update(idx: int) -> void:
	# _capture(node)는 노드 인자가 필요하므로, deferred 호출에 self를 넘긴다.
	definition.call_deferred("_capture", self)

func _process(delta: float) -> void:
	set_deferred("size", get_combined_minimum_size())

func select_item(idx: int) -> void:
	var type = option_button.get_item_metadata(idx)
	clear_value_box()
	match type:
		VariableDef.AllowedVariables.BOOL: 
			value_box.add_child(CheckBox.new())
			
		VariableDef.AllowedVariables.INT: 
			var spin_box = SpinBox.new()
			spin_box.custom_minimum_size.x = 150
			spin_box.max_value = DialogueToolUtil.int_max
			value_box.add_child(spin_box)
			
		VariableDef.AllowedVariables.FLOAT: 
			var spin_box = SpinBox.new()
			spin_box.custom_minimum_size.x = 150
			spin_box.step = 0.001
			value_box.add_child(spin_box)
			
		VariableDef.AllowedVariables.STRING: 
			var text_edit = TextEdit.new()
			text_edit.custom_minimum_size.x = 150
			value_box.add_child(text_edit)
			
		VariableDef.AllowedVariables.VECTOR2: 
			var hbox = HBoxContainer.new()
			for i in range(2):
				var spin_box = EditorSpinSlider.new()
				spin_box.label = char(120 + i) # x,y
				spin_box.custom_minimum_size.x = 60
				spin_box.max_value = 10000 
				hbox.add_child(spin_box)
			value_box.add_child(hbox)
			
		VariableDef.AllowedVariables.VECTOR3: 
			var hbox = HBoxContainer.new()
			for i in range(3):
				var spin_box = EditorSpinSlider.new()
				spin_box.label = char(120 + i) # x,y,z
				spin_box.custom_minimum_size.x = 60
				spin_box.max_value = 10000
				hbox.add_child(spin_box)
			value_box.add_child(hbox)
			
		VariableDef.AllowedVariables.COLOR: 
			var color_picker = ColorPickerButton.new()
			color_picker.custom_minimum_size.x = 100
			value_box.add_child(color_picker)
			
		VariableDef.AllowedVariables.RANDOM: 
			var hbox = HBoxContainer.new()
			hbox.add_child(SpinBox.new())
			var label =	Label.new()
			label.text = " ~ "
			hbox.add_child(label)
			hbox.add_child(SpinBox.new())
			value_box.add_child(hbox)
			
	
func clear_value_box() -> void:
	for child in value_box.get_children():
		value_box.remove_child(child)

func get_value() -> Dictionary:
	var type = option_button.get_selected_metadata()
	var result: Dictionary = {}
	if type == VariableDef.AllowedVariables.RANDOM:
		result["name"] = "RANDOM"
	else:
		result["name"] = type_string(type) 
		
	result["type"] = type
	match type:
		VariableDef.AllowedVariables.NIL:
			result["variable"] = null 
		VariableDef.AllowedVariables.BOOL:
			result["variable"] = value_node.button_pressed
		VariableDef.AllowedVariables.INT:
			result["variable"] = value_node.value as int
		VariableDef.AllowedVariables.FLOAT:
			result["variable"] = value_node.value
		VariableDef.AllowedVariables.STRING:
			result["variable"] = value_node.text
		VariableDef.AllowedVariables.VECTOR2:
			result["variable"] = Vector2(value_node.get_child(0).value, value_node.get_child(1).value)
		VariableDef.AllowedVariables.VECTOR3:
			result["variable"] = Vector3(value_node.get_child(0).value, value_node.get_child(1).value, value_node.get_child(2).value)
		VariableDef.AllowedVariables.COLOR:
			result["variable"] = value_node.color
		VariableDef.AllowedVariables.RANDOM:
			var a_value = value_node.get_child(0).value as int
			var b_value = value_node.get_child(2).value as int
			var randfunc = func() -> int: return randi_range(a_value, b_value)
			result["variable"] = [a_value, b_value, randfunc]
	
	return result

func load_value() -> void:
	var type = option_button.get_selected_metadata()
	match type:
		VariableDef.AllowedVariables.BOOL:
			value_node.button_pressed = definition.variable
		VariableDef.AllowedVariables.INT:
			value_node.value = definition.variable
		VariableDef.AllowedVariables.FLOAT:
			value_node.value = definition.variable
		VariableDef.AllowedVariables.STRING:
			value_node.text = definition.variable
		VariableDef.AllowedVariables.VECTOR2:
			value_node.get_child(0).value = definition.variable.x
			value_node.get_child(1).value = definition.variable.y
		VariableDef.AllowedVariables.VECTOR3:
			value_node.get_child(0).value = definition.variable.x
			value_node.get_child(1).value = definition.variable.y
			value_node.get_child(2).value = definition.variable.z
		VariableDef.AllowedVariables.COLOR:
			value_node.color = definition.variable
		VariableDef.AllowedVariables.RANDOM:
			value_node.get_child(0).value = definition.variable[0]
			value_node.get_child(2).value = definition.variable[1]
