extends Node
class_name DialogToolUtility

static func get_script_properties(node: Object) -> Array:
	var properties = node.get_property_list()
	var target_index: int = -1
	for i in range(properties.size()):
		if properties[i].name == "script":
			target_index = i
			break
		
	if target_index != -1:
		return properties.slice(target_index + 2)
	
	return []
