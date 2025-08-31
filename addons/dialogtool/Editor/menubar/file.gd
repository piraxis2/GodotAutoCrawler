@tool
extends PopupMenu

func _ready() -> void:
	clear()
	add_item("Save", 0)
	add_item("Load", 1)
	

func _on_id_pressed(id: int) -> void:
	pass # Replace with function body.
