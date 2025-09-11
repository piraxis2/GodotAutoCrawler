@tool
extends Control
class_name DeleteButton

signal on_click_delete


func _on_delete_button_pressed() -> void:
	print(1)
	on_click_delete.emit()
