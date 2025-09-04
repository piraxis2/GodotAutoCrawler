@tool
extends PanelContainer

func _can_drop_data(at_position: Vector2, data: Variant) -> bool:
	var file_path = data.files[0]
	if file_path.ends_with(".tres") and data.files.size() == 1:
		return true
	return false

func _drop_data(at_position: Vector2, data: Variant) -> void:
	$"../../HSplitContainer/GraphEdit".load_resource_action(data.files[0])
