@tool
extends DialogueNode
class_name AutoloadGraphNode

@onready var autoload_option: OptionButton = $HBoxContainer/OptionButton
@onready var property_option: OptionButton = $HBoxContainer/OptionButton2


func _get_autoloads() -> Dictionary:
	var autoloads: Dictionary = {}
	if autoloads.is_empty():
		var properties: Array = ProjectSettings.get_property_list()
		
		for prop in properties:
			var prop_name: String = prop.name
			if prop_name.begins_with("autoload/"):
				
				var autoload_name: String = prop_name.trim_prefix("autoload/")
				var node_instance: Node = get_node_or_null("/root/" + autoload_name)
				
				if node_instance:
					autoloads[autoload_name] = node_instance
	
	return autoloads

func _ready() -> void:
	add_right_port_info(1, port_type.data, Color.DEEP_PINK)
	autoload_option.item_selected.connect(on_autoload_option_selected)
	property_option.item_selected.connect(on_property_option_selected)
	var autoloadsdic = _get_autoloads()
	for autoload in autoloadsdic:
		autoload_option.add_item(autoload)
		autoload_option.set_item_metadata(autoload_option.item_count - 1, autoloadsdic[autoload])
	
	super._ready()
	autoload_option.select(definition.auto_load_idx)
	on_autoload_option_selected(definition.auto_load_idx)
	
func on_autoload_option_selected(idx: int) -> void:
	property_option.clear()
	var auto_load_instance = autoload_option.get_item_metadata(idx)
	definition.autoload_name = autoload_option.get_item_text(idx)
	for pro in DialogueToolUtil.get_script_properties(auto_load_instance):
		property_option.add_item(pro["name"])
		property_option.set_item_metadata(property_option.item_count - 1, auto_load_instance.get(pro["name"]))
		print(auto_load_instance.get(pro["name"]))
	if property_option.item_count > 0:
		property_option.select(definition.property_idx)
	on_property_option_selected(definition.property_idx)	
	
	
func on_property_option_selected(idx: int) -> void:
	definition.property_idx = idx
	if property_option.item_count > 0:
		definition.property_name = property_option.get_item_text(idx)
