@tool
extends NodeDefinition
class_name AutoLoadDef

var edit: CodeEdit
var build_button: Button
var execute_button: Button

@export var auto_load_idx: int = 0
@export var property_idx: int = 0
@export var autoload_name: String
@export var property_name: String

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/autoload_node.tscn"

func _get_autoloads() -> Dictionary:
	var autoloads: Dictionary = {}
	if autoloads.is_empty():
		var properties: Array = ProjectSettings.get_property_list()
		
		for prop in properties:
			var prop_name: String = prop.name
			if prop_name.begins_with("autoload/"):
				
				var autoload_name: String = prop_name.trim_prefix("autoload/")
				var node_instance: Node = definition_node.get_node_or_null("/root/" + autoload_name)
				
				if node_instance:
					autoloads[autoload_name] = node_instance
	
	return autoloads

func _node_init(node: DialogueNode) -> void:
	definition_node = node
	node.add_right_port_info(1, node.port_type.data, Color.DEEP_PINK)
	node.autoload_option.item_selected.connect(on_autoload_option_selected)
	node.property_option.item_selected.connect(on_property_option_selected)
	var autoloadsdic = _get_autoloads()
	for autoload in autoloadsdic:
		node.autoload_option.add_item(autoload)
		node.autoload_option.set_item_metadata(node.autoload_option.item_count - 1, autoloadsdic[autoload])
	
	node.autoload_option.select(auto_load_idx)
	on_autoload_option_selected(auto_load_idx)
	
func _capture() -> void:
	auto_load_idx = definition_node.autoload_option.get_selected_id()
	property_idx = definition_node.property_option.get_selected_id()
	autoload_name = definition_node.autoload_option.get_item_text(auto_load_idx)
	property_name = definition_node.property_option.get_item_text(property_idx)
	
func on_autoload_option_selected(idx: int) -> void:
	definition_node.property_option.clear()
	var auto_load_instance = definition_node.autoload_option.get_item_metadata(idx)
	autoload_name = definition_node.autoload_option.get_item_text(idx)
	for pro in DialogToolUtility.get_script_properties(auto_load_instance):
		definition_node.property_option.add_item(pro["name"])
		definition_node.property_option.set_item_metadata(definition_node.property_option.item_count - 1, auto_load_instance.get(pro["name"]))
		print(auto_load_instance.get(pro["name"]))
	if	definition_node.property_option.item_count > 0:
		definition_node.property_option.select(property_idx)
	on_property_option_selected(property_idx)	
	
	
func on_property_option_selected(idx: int) -> void:
	property_idx = idx
	if definition_node.property_option.item_count > 0:
		property_name = definition_node.property_option.get_item_text(idx)
	return
	
func _get_data_output(port: int) -> Variant:
	match port:
		0: return Engine.get_singleton(autoload_name).get(property_name)
	return null
	
