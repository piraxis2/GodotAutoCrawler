@tool
extends Control
class_name ExpressionInputNode

@onready var edit: TextEdit = $TextEdit

func _on_hold_pressed() -> void:
	edit.editable = false
	$Hold.queue_free()

func _on_delete_pressed() -> void:
	get_parent().call_deferred("update_input_slot")
	get_parent().remove_child(self)
	queue_free()
