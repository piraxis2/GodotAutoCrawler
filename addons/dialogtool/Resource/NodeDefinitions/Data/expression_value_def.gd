@tool
class_name ExpressionValueDef extends DataDefinition

@export var expression_string: String = ""
@export var inputs: Dictionary
var expression: Expression = Expression.new()
var built_value

var output_port_data: Dictionary = {"slot_position": 1, "port_type": DialogueNode.port_type.data, "color": DialogueNode.color_dic["output"]}

func get_runtime_type() -> StringName:
	return &"expression"


func get_runtime_params() -> Dictionary:
	# 런타임은 원본 식과 순서가 있는 입력 키(포트 i -> keys[i])가 필요하다.
	# 값은 런타임에 연결을 통해 해결되므로 Callable은 저장하지 않는다.
	return {
		"expression": expression_string,
		"inputs": inputs.keys(),
	}


func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/expression_node.tscn"

func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {"expression": expression_string})

func _capture(node: DialogueNode) -> void:
	# 평가는 런타임 evaluator(및 Build 미리보기)가 수행하므로 원본 식 텍스트만
	# 저장한다. 어댑터가 code_edit에서 텍스트를 읽어온다.
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	if params.has("expression"):
		expression_string = params["expression"]
	
func text_changed(node: DialogueNode) -> void:
	expression_string = node.code_edit.text
	
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
