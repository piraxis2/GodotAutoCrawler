class_name DialoguePlayer extends Node


@export var dialogue_resource: DialogueGraphResource

signal dialogue_started
signal dialogue_end
signal ui_request(request_data: Dictionary)

var current_node_id: int = -1
var waiting_for: StringName = &"none"
var selected_choice: int = -1


func _ready() -> void:
	if DialogueToolUtil.is_dialogue_debug_hint():
		var resource_path = DialogueToolUtil.cmd_arguments.get("dialogue_resource", "")
		if not resource_path.is_empty():
			start_dialogue(ResourceLoader.load(resource_path))


func init_dialogue(dialogue: DialogueGraphResource) -> void:
	start_dialogue(dialogue)


func start_dialogue(dialogue: DialogueGraphResource) -> void:
	dialogue_resource = dialogue
	if dialogue_resource == null:
		push_error("DialoguePlayer: dialogue_resource is null.")
		return

	current_node_id = dialogue_resource.get_runtime_start_node_id()
	waiting_for = &"none"
	selected_choice = -1
	dialogue_started.emit()
	_execute_until_waiting()


func advance() -> void:
	if dialogue_resource == null or current_node_id == -1:
		return

	if waiting_for == &"text":
		waiting_for = &"none"
		_go_to_next_node(0)
		_execute_until_waiting()


func select_choice(index: int) -> void:
	if dialogue_resource == null or current_node_id == -1:
		return

	if waiting_for != &"choice":
		return

	selected_choice = index
	waiting_for = &"none"
	_go_to_next_node(index)
	_execute_until_waiting()


func _execute_until_waiting() -> void:
	while current_node_id != -1 and waiting_for == &"none":
		var node_data = dialogue_resource.get_runtime_node(current_node_id)
		if node_data.is_empty():
			_end_dialogue()
			return

		match node_data.get("type", &"unknown"):
			&"start":
				_go_to_next_node(0)
			&"say":
				_execute_say(node_data.get("params", {}))
			&"choice":
				_execute_choice(node_data.get("params", {}))
			&"branch":
				_execute_branch()
			&"end":
				_end_dialogue()
			_:
				push_warning("DialoguePlayer: unknown node type '%s'." % node_data.get("type", &"unknown"))
				_go_to_next_node(0)


func _execute_say(params: Dictionary) -> void:
	waiting_for = &"text"
	ui_request.emit({
		"type": "display_text",
		"speaker": params.get("speaker", ""),
		"say": params.get("text", ""),
		"portrait": params.get("portrait", ""),
	})


func _execute_choice(params: Dictionary) -> void:
	waiting_for = &"choice"
	selected_choice = -1
	ui_request.emit({
		"type": "offer_choice",
		"choices": params.get("choices", []),
	})


func _execute_branch() -> void:
	var input_node_id = dialogue_resource.get_runtime_input_node_id(current_node_id, 0)
	var input_value = _get_data_value(input_node_id)
	var output_port = 0 if bool(input_value) else 1
	_go_to_next_node(output_port)


func _get_data_value(node_id: int) -> Variant:
	if node_id == -1:
		return null

	var node_data = dialogue_resource.get_runtime_node(node_id)
	if node_data.is_empty():
		return null

	var params = node_data.get("params", {})
	match node_data.get("type", &"unknown"):
		&"variable":
			return params.get("value")
		_:
			return null


func _go_to_next_node(port: int) -> void:
	current_node_id = dialogue_resource.get_runtime_next_node_id(current_node_id, port)
	if current_node_id == -1:
		_end_dialogue()


func _end_dialogue() -> void:
	current_node_id = -1
	waiting_for = &"none"
	dialogue_end.emit()
