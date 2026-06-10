@tool
class_name DescriptionNode extends DialogueNode
var color_picker: ColorPickerButton: 
	get: return $HBoxContainer/ColorPickerButton
var description_label: Label:
	get: return $Label

func _on_color_picker_button_color_changed(color: Color) -> void:
	modulate = color_picker.color

func _on_node_deselected() -> void:
	get_parent().move_child(self, 0)
	
func _on_node_selected() -> void:
	get_parent().move_child(self, 0)

func _on_button_button_up() -> void:
	var confirm_popup =	ConfirmationDialog.new()
	var text_edit = TextEdit.new()
	text_edit.custom_minimum_size = Vector2(500, 200) 
	text_edit.text = description_label.text
	confirm_popup.add_child(text_edit)
	var confirmed_func = func(): description_label.text = text_edit.text 
	confirm_popup.confirmed.connect(confirmed_func)
	add_child(confirm_popup)
	confirm_popup.popup_centered(confirm_popup.get_contents_minimum_size())
