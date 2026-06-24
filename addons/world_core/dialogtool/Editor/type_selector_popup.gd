@tool
extends PopupPanel

signal type_selected(type_name: String)

@onready var search_box: LineEdit = $VBoxContainer/LineEdit
@onready var type_tree: Tree = $VBoxContainer/Tree

var is_tree_built: bool = false
var class_name_to_item: Dictionary = {}

func _ready() -> void:
	search_box.text_changed.connect(_on_search_text_changed)
	type_tree.item_selected.connect(_on_item_selected)
	popup_hide.connect(search_box.clear)
	about_to_popup.connect(_build_tree_if_needed)
	popup_selector()
	
func popup_selector() -> void:
	popup()
	search_box.grab_focus()
	
func _build_tree_if_needed() -> void:
	if is_tree_built:
		return
	
	type_tree.clear()
	class_name_to_item.clear()
	
	var root = type_tree.create_item()
	type_tree.hide_root = true
	
	var variant_category = type_tree.create_item(root)
	variant_category.set_text(0, "Variant Types")
	variant_category.set_selectable(0, false)
	variant_category.set_custom_color(0, Color.BLUE_VIOLET)
	
	for i in range(Variant.Type.TYPE_MAX):
		var type_name = type_string(i)
		var item = type_tree.create_item(variant_category)
		item.set_metadata(0, type_name)
		item.set_text(0, type_name)
				
		if Engine.is_editor_hint():
			if EditorInterface.get_editor_theme().has_icon(type_name,"EditorIcons"):
				item.set_icon(0, EditorInterface.get_editor_theme().get_icon(type_name, "EditorIcons"))
		
	var object_category = type_tree.create_item(root)
	object_category.set_text(0, "Object Types")
	object_category.set_selectable(0, false)
	object_category.set_custom_color(0, Color.ORANGE)
		
	var class_list = ProjectSettings.get_global_class_list()

	for cdic in class_list:
		var c_name = cdic["class"]
		var item = type_tree.create_item() 
		item.set_text(0, c_name) 
		item.set_metadata(0, c_name)
		class_name_to_item[c_name] = item

	for cdic in class_list:
		var c_name = cdic["class"]
		var parent_name = cdic["base"]
		if class_name_to_item.has(parent_name):
			var parent_item = class_name_to_item[parent_name]
			var child_item = class_name_to_item[c_name]
			parent_item.add_child(child_item)
		else:
			object_category.add_child(class_name_to_item[c_name])
		
	
	is_tree_built = true
	

func _on_search_text_changed(new_text: String):
	_filter_tree(type_tree.get_root(), new_text.to_lower())

func _filter_tree(current_item: TreeItem, filter: String) -> bool:
	if not current_item:
		return false
		
	var any_child_visible = false
	
	var child = current_item.get_first_child()
	
	while child:
		if _filter_tree(child, filter):
			any_child_visible = true
		child = child.get_next()
		
	var self_is_isvisible = filter in current_item.get_text(0).to_lower()
	
	var should_be_visible = any_child_visible or self_is_isvisible
	current_item.set_visible(should_be_visible)
	
	if current_item.get_parent() == type_tree.get_root():
		current_item.set_visible(true)
		
	return should_be_visible
	
func _on_item_selected():
	var item = type_tree.get_selected()
	if item and item.is_selectable(0):
		var type_name = item.get_metadata(0)
		emit_signal("type_selected", type_name)
		hide()
