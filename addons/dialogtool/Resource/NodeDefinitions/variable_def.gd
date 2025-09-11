@tool
extends NodeDefinition
class_name VariableDef


func _node_init(node: DialogueNode) -> void:
	var test = load("res://addons/dialogtool/Editor/property_popup.tscn").instantiate()
	node.add_child(test)
	test.popup_for_object(self)
