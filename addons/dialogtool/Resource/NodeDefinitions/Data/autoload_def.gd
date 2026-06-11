@tool
class_name AutoLoadDef extends DataDefinition

var edit: CodeEdit
var build_button: Button
var execute_button: Button

@export var auto_load_idx: int = 0
@export var property_idx: int = 0
@export var autoload_name: String
@export var property_name: String

var dialogue_node: DialogueNode

func get_runtime_type() -> StringName:
	return &"autoload"

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/autoload_node.tscn"

func _node_init(node: DialogueNode) -> void:
	# 노드 캐싱은 에디터 _get_data_output에서 쓰는 데이터 참조라 정의에 남긴다.
	dialogue_node = node
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {})

func _capture(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	auto_load_idx = params.get("auto_load_idx", auto_load_idx)
	property_idx = params.get("property_idx", property_idx)
	autoload_name = params.get("autoload_name", autoload_name)
	property_name = params.get("property_name", property_name)
	
func _get_data_output(port: int) -> Variant:
	match port:
		0:
			if Engine.is_editor_hint():
				return dialogue_node.autoload_option.get_selected_metadata().get(property_name)
			else:
				return DialogueToolUtil.get_node_or_null("/root/" + autoload_name).get(property_name)
	return null
	
