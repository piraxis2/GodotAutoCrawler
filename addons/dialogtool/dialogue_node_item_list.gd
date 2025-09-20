@tool
extends ItemList
class_name DialogueNodeItemList
@export var dialogue_dic : Dictionary = { "end": EndDef, "say": SayDef}

func _ready() -> void:
	var index: int = 0
	clear()
	var file_list = find_definitions("res://addons/dialogtool/Resource/NodeDefinitions/")
	
	for elem in file_list:
		var script: Script = load(elem)
		var name = script.get_global_name().left(-3)
		if name == "Start":
			continue
		add_item(name)
		set_item_metadata(index, script)
		index += 1

func find_definitions(dir_path: String, file_list: Array[String] = []) -> Array[String]:
	var dir = DirAccess.open(dir_path)
	var item_name
	if dir:
		dir.list_dir_begin()
		item_name = dir.get_next()
		while item_name != "":
			if item_name == "." or item_name == ".." or item_name == "Abstract":
				item_name = dir.get_next()
				continue
			var full_path = dir_path.path_join(item_name)
			
			if dir.current_is_dir():
				find_definitions(full_path, file_list)
			elif item_name.ends_with(".gd"):
				file_list.append(full_path)
			
			item_name = dir.get_next()
	else:
		push_error(dir_path + "경로가 존재하지 않습니다")
		return []
	
	return file_list
func _get_drag_data(at_position: Vector2) -> Variant:
	var item_index: int = get_item_at_position(at_position, true)
	if item_index < 0 : return null
	
	var preview = GraphNode.new()
	preview.title = get_item_text(item_index)
	set_drag_preview(preview)
	var item_meta = get_item_metadata(item_index).new()
	return item_meta
