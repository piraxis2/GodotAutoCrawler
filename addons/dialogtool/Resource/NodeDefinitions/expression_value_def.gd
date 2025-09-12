@tool
extends NodeDefinition
class_name ExpressionValueDef

@export var expression_string: String = ""
@export var inputs: Dictionary
var expression: Expression = Expression.new()
var dialogue_node: DialogueNode
var built_value

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/expression_node.tscn"

func _node_init(node: DialogueNode) -> void:
	node.resizable = true
	node.code_edit.text = expression_string
	node.code_edit.text_changed.connect(text_changed)
	dialogue_node = node
 
func _capture() -> void:
	expression_string = dialogue_node.code_edit.text
	build()
	
func text_changed() -> void:
	expression_string = dialogue_node.code_edit.text
	
func temp_property(property_path: String) -> void:
	print("property: ", property_path)
	return
	
func build() -> Variant:
	if expression_string.is_empty():
		return null
		
	var error = expression.parse(expression_string, inputs.keys())
	if error != OK:
		print("expression parse error: " + expression.get_error_text())
		return null
	
	var result = expression.execute(inputs.values(), self)
	if expression.has_execute_failed():
		print("expression failed: " + expression.get_error_text())
		return null
		
	built_value = result
	return result

func get_data_output(port: int) -> Variant:
	match port:
		0: return built_value
	return null
	
