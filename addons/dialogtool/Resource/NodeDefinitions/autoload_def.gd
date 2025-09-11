@tool
extends NodeDefinition
class_name AutoLoadDef

var edit: CodeEdit
var build_button: Button
var execute_button: Button

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
	on_autoload_option_selected(node.autoload_option.get_selected_id())
	
func _capture() -> void:
	return
	
func on_autoload_option_selected(idx: int) -> void:
	definition_node.property_option.clear()
	var autoload: Node = definition_node.autoload_option.get_item_metadata(idx)
	for pro in DialogToolUtility.get_script_properties(autoload):
		definition_node.property_option.add_item(pro["name"])
		definition_node.property_option.set_item_metadata(definition_node.property_option.item_count - 1, autoload.get(pro["name"]))
		print(autoload.get(pro["name"]))
	return
	
func on_property_option_selected(idx: int) -> void:
	return
	
