@tool
extends PanelContainer
@export var panel_type: String

func _can_drop_data(at_position: Vector2, data: Variant) -> bool:
	var file_path = data.files[0]
	match panel_type:
		"NodePath": 
			if file_path.ends_with(".tres") and data.files.size() == 1:
				return true
		"ScenePath":
			if file_path.ends_with(".tscn") and data.files.size() == 1:
				return true
			return false
	return false

func _drop_data(at_position: Vector2, data: Variant) -> void:
	match panel_type:
		"NodePath": $"../../../../HSplitContainer/GraphEdit".load_resource_action(data.files[0])
		"ScenePath": 1
	
