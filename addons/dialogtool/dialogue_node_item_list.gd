@tool
extends ItemList

@export var dialogue_dic : Dictionary

func _ready() -> void:
	var index: int = 0
	clear()
	for elem in dialogue_dic:
		var name = elem
		add_item(name)
		set_item_metadata(index, dialogue_dic[elem])
		index += 1

func _get_drag_data(at_position: Vector2) -> Variant:
	var item_index: int = get_item_at_position(at_position, true)
	if item_index < 0 : return null
	
	var preview = GraphNode.new()
	preview.title = get_item_text(item_index)
	set_drag_preview(preview)
	var item_meta = get_item_metadata(item_index)
	return item_meta
