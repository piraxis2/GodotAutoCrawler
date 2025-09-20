@tool
extends Control
var property_selector_instance

@onready var scene_path: Label = $VSplitContainer/HBoxContainer/HBoxContainer/HBoxContainer2/PanelContainer2/ScenePathLabel

func _on_button_button_up() -> void:
	if scene_path.text == "null" or scene_path.text.is_empty():
		print("scene경로가 존재하지 않습니다")
		return
	
	var scene = load(scene_path.text)
	if not scene:
		return
