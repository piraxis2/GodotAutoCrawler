@tool
extends Node
class_name DialogueToolUtility
static var int_max: int = 9223372036854775807

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
