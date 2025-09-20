@tool
class_name AutoLoadDef extends DataDefinition

var edit: CodeEdit
var build_button: Button
var execute_button: Button

@export var auto_load_idx: int = 0
@export var property_idx: int = 0
@export var autoload_name: String
@export var property_name: String

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/autoload_node.tscn"
	
func _node_init(node: DialogueNode) -> void:
	return
	
func _capture() -> void:
	auto_load_idx = definition_node.autoload_option.get_selected_id()
	property_idx = definition_node.property_option.get_selected_id()
	autoload_name = definition_node.autoload_option.get_item_text(auto_load_idx)
	property_name = definition_node.property_option.get_item_text(property_idx)
	
func _get_data_output(port: int) -> Variant:
	match port:
		0:
			if Engine.is_editor_hint():
				return definition_node.autoload_option.get_selected_metadata().get(property_name)
			else:
				return DialogueToolUtil.get_node_or_null("/root/" + autoload_name).get(property_name)
	return null
	
