@tool
extends LineEdit

# Godot FileSystem에서 Texture 리소스 하나를 드롭하면 res:// 경로를 입력한다.
# 잘못된 드롭은 거부하므로 기존 텍스트를 변경하지 않는다.


func _can_drop_data(_at_position: Vector2, data: Variant) -> bool:
	var path := _get_single_file_path(data)
	return not path.is_empty() and _is_texture_path(path)


func _drop_data(_at_position: Vector2, data: Variant) -> void:
	var path := _get_single_file_path(data)
	if path.is_empty() or not _is_texture_path(path):
		return
	text = path
	text_changed.emit(text)


func _get_single_file_path(data: Variant) -> String:
	if not data is Dictionary or data.get("type", "") != "files":
		return ""
	var files: Variant = data.get("files", [])
	if not (files is Array or files is PackedStringArray) or files.size() != 1:
		return ""
	return str(files[0])


func _is_texture_path(path: String) -> bool:
	if not path.begins_with("res://") or not ResourceLoader.exists(path):
		return false
	return ResourceLoader.load(path) is Texture2D
