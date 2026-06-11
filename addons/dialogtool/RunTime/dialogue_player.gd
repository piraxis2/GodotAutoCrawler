class_name DialoguePlayer extends Node


@export var dialogue_resource: DialogueGraphResource

signal dialogue_started
signal dialogue_end
signal ui_request(request_data: Dictionary)
# 실행 중인 노드가 바뀔 때마다 그 node_id를 알린다(디버깅/하이라이트용).
signal current_node_changed(node_id: int)

var current_node_id: int = -1
var waiting_for: StringName = &"none"
var selected_choice: int = -1


func _ready() -> void:
	if DialogueToolUtil.is_dialogue_debug_hint():
		# 디버그 실행 모드: 실행 노드를 콘솔 로그 + (원격 디버거를 통해) 에디터로 전송.
		current_node_changed.connect(_log_current_node)
		dialogue_end.connect(_on_debug_end)
		var resource_path = DialogueToolUtil.cmd_arguments.get("dialogue_resource", "")
		if not resource_path.is_empty():
			# 자식 _ready가 부모 UI의 _ready보다 먼저 실행되므로, 여기서 바로
			# 시작하면 DialogueUI가 핸들러를 연결하기 전에 첫 ui_request가 emit되어
			# 첫 노드를 놓친다. 모든 노드(UI 포함)의 _ready가 끝난 뒤 시작하도록 미룬다.
			start_dialogue.call_deferred(ResourceLoader.load(resource_path))


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

	var next_id = dialogue_resource.get_runtime_next_node_id(current_node_id, index)
	if next_id == -1:
		push_warning("DialoguePlayer: choice port %d has no connection; ending dialogue." % index)
		_end_dialogue()
		return

	current_node_id = next_id
	_execute_until_waiting()


func _execute_until_waiting() -> void:
	while current_node_id != -1 and waiting_for == &"none":
		var node_data = dialogue_resource.get_runtime_node(current_node_id)
		if node_data.is_empty():
			_end_dialogue()
			return

		# 이 반복에서 실행할 노드를 알린다(매 노드 실행 시 1회).
		current_node_changed.emit(current_node_id)

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


# 디버그 실행 모드에서 current_node_changed에 연결되어 실행 노드를 로그로 출력하고,
# 원격 디버거가 활성이면 에디터(DialogueDebuggerPlugin)로 현재 노드 id를 전송한다.
func _log_current_node(node_id: int) -> void:
	var node_type := &"?"
	if dialogue_resource:
		node_type = dialogue_resource.get_runtime_node(node_id).get("type", &"?")
	print("[DialogueDebug] -> node %d (type: %s)" % [node_id, str(node_type)])
	if EngineDebugger.is_active():
		EngineDebugger.send_message("dialogue:current_node", [node_id])


# 대화 종료 시 에디터의 하이라이트를 해제하도록 -1을 전송.
func _on_debug_end() -> void:
	if EngineDebugger.is_active():
		EngineDebugger.send_message("dialogue:current_node", [-1])


func _execute_say(params: Dictionary) -> void:
	waiting_for = &"text"
	ui_request.emit({
		"type": "display_text",
		"speaker": params.get("speaker", ""),
		"say": params.get("text", ""),
		"portrait": params.get("portrait", ""),
	})


func _execute_choice(params: Dictionary) -> void:
	var choices = params.get("choices", [])
	if choices.is_empty():
		push_warning("DialoguePlayer: choice node %d has no choices; ending dialogue." % current_node_id)
		_end_dialogue()
		return

	waiting_for = &"choice"
	selected_choice = -1
	ui_request.emit({
		"type": "offer_choice",
		"choices": choices,
	})


func _execute_branch() -> void:
	# Branch: data 입력 포트 0 -> 조건. true면 출력 포트 0, false면 포트 1로 이동.
	var input_node_id = dialogue_resource.get_runtime_input_node_id(current_node_id, 0)
	if input_node_id == -1:
		push_warning("DialoguePlayer: branch node %d has no data input; treating condition as false." % current_node_id)
		_go_to_next_node(1)
		return

	var input_value = _get_data_value(input_node_id)
	var condition := _to_bool(input_value)
	_go_to_next_node(0 if condition else 1)


func _get_data_value(node_id: int, visited: Array = []) -> Variant:
	if node_id == -1:
		return null

	# 경로 기반 visited 셋으로 순환 data 의존성을 방어한다.
	if node_id in visited:
		push_warning("DialoguePlayer: circular data dependency at node %d; returning null." % node_id)
		return null

	var node_data = dialogue_resource.get_runtime_node(node_id)
	if node_data.is_empty():
		return null

	var params = node_data.get("params", {})
	match node_data.get("type", &"unknown"):
		&"variable":
			if params.get("random", false):
				return randi_range(int(params.get("random_min", 0)), int(params.get("random_max", 0)))
			return params.get("value")
		&"expression":
			return _evaluate_expression(node_id, params, visited + [node_id])
		_:
			push_warning("DialoguePlayer: data node %d type '%s' is not evaluable." % [node_id, str(node_data.get("type", &"unknown"))])
			return null


# expression data 노드를 평가한다. 각 입력 포트 i는 변수 keys[i]에 바인딩되고,
# 그 값은 해당 포트로 들어오는 런타임 연결을 따라가 해결한다.
func _evaluate_expression(node_id: int, params: Dictionary, visited: Array) -> Variant:
	var expr_string: String = params.get("expression", "")
	if expr_string.is_empty():
		push_warning("DialoguePlayer: expression node %d has empty expression; returning null." % node_id)
		return null

	var keys: Array = params.get("inputs", [])
	var values: Array = []
	for port in range(keys.size()):
		var src_id = dialogue_resource.get_runtime_input_node_id(node_id, port)
		values.append(_get_data_value(src_id, visited))

	var expr := Expression.new()
	var err := expr.parse(expr_string, PackedStringArray(keys))
	if err != OK:
		push_warning("DialoguePlayer: expression parse failed at node %d: %s" % [node_id, expr.get_error_text()])
		return null

	var result = expr.execute(values, null)
	if expr.has_execute_failed():
		push_warning("DialoguePlayer: expression execute failed at node %d: %s" % [node_id, expr.get_error_text()])
		return null

	return result


# 분기를 위해 런타임 data 값을 일관되게 bool로 변환한다.
# 애매하거나 비어 있는 값은 경고 후 false로 처리한다.
func _to_bool(value: Variant) -> bool:
	match typeof(value):
		TYPE_NIL:
			push_warning("DialoguePlayer: branch condition is null; treating as false.")
			return false
		TYPE_BOOL:
			return value
		TYPE_INT, TYPE_FLOAT:
			return value != 0
		TYPE_STRING, TYPE_STRING_NAME:
			return not String(value).is_empty()
		_:
			push_warning("DialoguePlayer: branch condition type %d is ambiguous; treating as false." % typeof(value))
			return false


func _go_to_next_node(port: int) -> void:
	current_node_id = dialogue_resource.get_runtime_next_node_id(current_node_id, port)
	if current_node_id == -1:
		_end_dialogue()


func _end_dialogue() -> void:
	current_node_id = -1
	waiting_for = &"none"
	dialogue_end.emit()
