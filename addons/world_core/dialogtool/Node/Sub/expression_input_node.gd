@tool
extends Control
class_name ExpressionInputNode

@onready var edit: Label = $Label
signal on_delete(text: String, port_idx: int, input_node: Node)

func _on_delete_pressed() -> void:
	on_delete.emit(edit.text, get_index(), self)
	get_parent().call_deferred("update_input_slot")
	get_parent().remove_child(self)
	queue_free()
	
func get_label() -> Label:
	return $Label
