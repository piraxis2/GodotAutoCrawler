@tool
class_name DialogueToolUtility extends Node
static var int_max: int = 9223372036854775807

var cmd_arguments: Dictionary = {}

func is_dialogue_debug_hint() -> bool:
	if cmd_arguments.has("is_dialogue_debug_mod"):
		return cmd_arguments["is_dialogue_debug_mod"] == "true"
	return false		

func _ready() -> void:
	var cmd_args = OS.get_cmdline_args()
	for i in range(cmd_args.size()):
		var arg = cmd_args[i]
		if arg.contains("--"):
			if cmd_args.size() > i + 1:
				cmd_arguments[arg.trim_prefix("--")] = cmd_args[i + 1]
			

func get_script_properties(node: Object) -> Array:
	if not node:
		return []

	var properties = node.get_property_list()
	var target_index: int = -1
	for i in range(properties.size()):
		if properties[i].name == "script":
			target_index = i
			break

	if target_index != -1:
		return properties.slice(target_index + 2)

	return []
