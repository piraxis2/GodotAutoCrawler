@tool
class_name ExpressionValueDef extends DataDefinition

@export var expression_string: String = ""
@export var inputs: Dictionary
var expression: Expression = Expression.new()
var dialogue_node: DialogueNode
var built_value

var output_port_data: Dictionary = {"slot_position": 1, "port_type": DialogueNode.port_type.data, "color": DialogueNode.color_dic["output"]}

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/expression_node.tscn"

func _node_init(node: DialogueNode) -> void:
	node.resizable = true
	node.code_edit.text = expression_string
	node.code_edit.text_changed.connect(text_changed)
	dialogue_node = node
	node.set_slot(output_port_data["slot_position"], false, output_port_data["port_type"], Color.WHITE, true, output_port_data["port_type"], output_port_data["color"])
 
func _capture() -> void:
	expression_string = dialogue_node.code_edit.text
	build()
	
func text_changed() -> void:
	expression_string = dialogue_node.code_edit.text
	
func temp_property(property_path: String) -> void:
	print("property: ", property_path)
	return
	
func update_input(node_input: Dictionary) -> void:
	inputs = node_input
	
func build() -> Variant:
	if expression_string.is_empty():
		return null
	
	var regex = RegEx.new()	
	regex.compile("[A-Z]")
	var matches = regex.search_all(expression_string)
	var result_string = expression_string
	
	for i in range(matches.size() -1, -1, -1):
		var regexmatch: RegExMatch = matches[i]
		result_string = result_string.substr(0, regexmatch.get_start()) + (regexmatch.get_string() + ".call().value") + result_string.substr(regexmatch.get_end())
		
	print(result_string)
	
	var error = expression.parse(result_string, inputs.keys())

	if error != OK:
		print("expression parse error: " + expression.get_error_text())
		return null
	
	var result = expression.execute(inputs.values(), self)
	if expression.has_execute_failed():
		print("expression failed: " + expression.get_error_text())
		return null
		
	built_value = result
	return result

func _get_data_output(port: int) -> Variant:
	match port:
		0: return expression.execute(inputs.values(), self)
	return null
