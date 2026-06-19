@tool
extends LineEdit

# state_condition 노드의 ConditionSet picker.
# Godot FileSystem에서 ConditionSet .tres 하나를 드롭하면 그 Resource를 보관하고 경로를 표시한다.
# 잘못된 드롭(다중 파일/비-.tres/ConditionSet 아님)은 거부해 기존 선택을 유지한다.
# Step 2는 외부 .tres 참조 중심이다(inline ConditionSet tree editor는 범위 밖). 단, 인스펙터 등으로
# 이미 지정된 inline ConditionSet 참조도 그대로 보관·표시한다(왕복 보존).

signal condition_set_changed(cs)

var condition_set: ConditionSet = null:
	set(value):
		condition_set = value
		_refresh_text()


func _ready() -> void:
	editable = false
	_refresh_text()


func _refresh_text() -> void:
	if condition_set == null:
		text = ""
		placeholder_text = "(drop ConditionSet .tres)"
	elif condition_set.resource_path != "":
		text = condition_set.resource_path
	else:
		text = "(inline ConditionSet)"


func _can_drop_data(_at_position: Vector2, data: Variant) -> bool:
	var path := _single_tres_path(data)
	return not path.is_empty() and _loads_as_condition_set(path)


func _drop_data(_at_position: Vector2, data: Variant) -> void:
	var path := _single_tres_path(data)
	if path.is_empty() or not _loads_as_condition_set(path):
		return
	condition_set = ResourceLoader.load(path)
	condition_set_changed.emit(condition_set)


func _single_tres_path(data: Variant) -> String:
	if not data is Dictionary or data.get("type", "") != "files":
		return ""
	var files: Variant = data.get("files", [])
	if not (files is Array or files is PackedStringArray) or files.size() != 1:
		return ""
	var path := str(files[0])
	return path if path.to_lower().ends_with(".tres") else ""


func _loads_as_condition_set(path: String) -> bool:
	if not path.begins_with("res://") or not ResourceLoader.exists(path):
		return false
	return ResourceLoader.load(path) is ConditionSet
