@tool
extends Control
class_name ExpressionInputNode

@onready var edit: TextEdit = $TextEdit
@onready var hold_button: Button = $Hold
signal on_hold(text: String)
signal on_delete(text: String)

func _on_hold_pressed() -> void:
	edit.editable = false
	on_hold.emit(edit.text)
	$Hold.queue_free()

func _on_delete_pressed() -> void:
	on_hold.emit(edit.text)
	get_parent().call_deferred("update_input_slot")
	get_parent().remove_child(self)
	queue_free()
