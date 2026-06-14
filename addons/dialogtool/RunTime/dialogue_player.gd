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

# --- 상태 provider seam (DT-005 Step 5) ---
# Dialogue runtime은 save file/PlayerData/전역 singleton을 직접 알지 않는다. 조건 평가 계층이
# 사용할 read 상태는 외부에서 주입된 read provider를 통해서만 접근한다(/root를 직접 조회하지 않음).
# read provider 계약(duck-typed): has_state(key) / read_state(key) / try_read_state(key, fallback).
# mutation provider는 이 Step에서 주입하지 않는다(소비하는 노드/Effect가 아직 없음 — 후속 Task).
var _read_state_provider = null


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


# --- read 상태 provider seam (DT-005 Step 5) -------------------------
# DialogueUI/DialogueManager 또는 테스트가 read provider를 주입한다. start_dialogue 전에 호출한다.
func set_read_state_provider(provider) -> void:
	_read_state_provider = provider


func get_read_state_provider():
	return _read_state_provider


func has_read_state_provider() -> bool:
	return _read_state_provider != null


# 조건 평가 계층(후속 ConditionEvaluator/노드)이 사용할 read seam. provider가 없으면 안전 기본값을
# 반환한다(현재 이 메서드를 소비하는 Dialogue 노드는 없다 — 경계만 만든다).
func has_state(key: StringName) -> bool:
	if _read_state_provider == null:
		return false
	return _read_state_provider.has_state(key)


func read_state(key: StringName) -> Variant:
	if _read_state_provider == null:
		push_warning("DialoguePlayer: read_state('%s') without a read state provider; returning null." % key)
		return null
	return _read_state_provider.read_state(key)


func try_read_state(key: StringName, fallback: Variant = null) -> Variant:
	if _read_state_provider == null:
		return fallback
	return _read_state_provider.try_read_state(key, fallback)


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

	# 이 Choice 노드를 떠나기 전에 연결된 Effect를 실행한 뒤 주 Flow로 이동한다(ADR-005).
	_run_effects(current_node_id)

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
			&"portrait_show", &"portrait_hide", &"portrait_expression":
				_execute_portrait(node_data.get("type", &"unknown"), node_data.get("params", {}))
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


# Portrait 명령은 Say와 독립된 비대기 Flow 명령이다.
# DialoguePlayer는 Portrait 상태를 보관하거나 렌더링하지 않고, 정규화된 UI 상태
# 변경 요청만 발행한 뒤 즉시 출력 포트 0의 다음 Flow 노드로 진행한다.
# 실제 상태 소유와 렌더링은 이후 Step의 DialogueUI 책임이다.
const PORTRAIT_SLOTS := ["left", "center", "right"]
const PORTRAIT_DEFAULT_SLOT := "center"

# Effect 대상 허용 타입은 DialogueGraphResource에 단일 정의를 둔다(에디터 validation과 공유).

func _execute_portrait(node_type: StringName, params: Dictionary) -> void:
	# waiting_for를 만들지 않는다: 요청을 발행하고 같은 실행 루프에서 다음 노드로 진행.
	ui_request.emit(_build_portrait_request(node_type, params))
	_go_to_next_node(0)


# 세 Portrait 노드 타입을 공통 "portrait_state" 요청 형식으로 정규화한다.
# actor/expression은 향후 resolver를 위한 메타데이터로 그대로 통과시킨다.
func _build_portrait_request(node_type: StringName, params: Dictionary) -> Dictionary:
	var action := _portrait_action_from_type(node_type)
	var slot := _normalize_portrait_slot(params.get("slot", PORTRAIT_DEFAULT_SLOT))
	var texture_path := String(params.get("texture_path", ""))

	# show MVP는 texture_path를 직접 저장/전달한다(ADR-004 참조).
	# texture_path가 비어 있어도 Flow를 중단하지 않는다: 경고만 남기고 요청은 그대로
	# 발행한다. actor/expression이 이후 Step의 resolver에서 텍스처를 해결할 수 있다.
	if action == "show" and texture_path.is_empty():
		push_warning("DialoguePlayer: portrait_show node %d has empty texture_path; emitting request anyway (actor/expression may resolve it later)." % current_node_id)

	return {
		"type": "portrait_state",
		"action": action,
		"slot": slot,
		"texture_path": texture_path,
		"actor": String(params.get("actor", "")),
		"expression": String(params.get("expression", "")),
		"transition": String(params.get("transition", "none")),
	}


func _portrait_action_from_type(node_type: StringName) -> String:
	match node_type:
		&"portrait_show":
			return "show"
		&"portrait_hide":
			return "hide"
		&"portrait_expression":
			return "expression"
		_:
			# 디스패치 match가 타입을 보장하므로 도달하지 않지만, 방어적으로 처리한다.
			push_warning("DialoguePlayer: unexpected portrait node type '%s'; defaulting action to 'show'." % str(node_type))
			return "show"


# slot이 잘못됐을 때 크래시하지 않고 기본 slot으로 일관되게 대체한다.
func _normalize_portrait_slot(raw: Variant) -> String:
	var slot := String(raw)
	if slot in PORTRAIT_SLOTS:
		return slot
	push_warning("DialoguePlayer: portrait node %d has invalid slot '%s'; falling back to '%s'." % [current_node_id, slot, PORTRAIT_DEFAULT_SLOT])
	return PORTRAIT_DEFAULT_SLOT


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
	# 이 노드를 떠나기 직전에 연결된 비대기 Effect들을 실행한 뒤 주 Flow 하나로 이동한다(ADR-005).
	_run_effects(current_node_id)
	current_node_id = dialogue_resource.get_runtime_next_node_id(current_node_id, port)
	if current_node_id == -1:
		_end_dialogue()


# 한 노드(from_node_id)에 연결된 Effect들을 저장 순서대로 실행한다.
# Effect는 실행 커서를 옮기지 않고 wait state를 만들지 않으며, 정규화된 비대기
# UI 요청만 발행한다. Effect 노드가 다시 Effect를 연결하면 체인을 따라가되,
# visited 셋으로 순환을 차단하고 Portrait 외 대상은 경고 후 건너뛴다.
func _run_effects(from_node_id: int) -> void:
	var queue: Array = dialogue_resource.get_runtime_effect_node_ids(from_node_id)
	if queue.is_empty():
		return

	var visited: Array = []
	while not queue.is_empty():
		var effect_id: int = queue.pop_front()
		if effect_id == -1:
			continue

		# 순환 방어: 이미 실행한 Effect 노드는 다시 실행하지 않는다.
		if effect_id in visited:
			push_warning("DialoguePlayer: effect cycle detected at node %d; skipping." % effect_id)
			continue
		visited.append(effect_id)

		var node_data = dialogue_resource.get_runtime_node(effect_id)
		if node_data.is_empty():
			push_warning("DialoguePlayer: effect target node %d not found; skipping." % effect_id)
			continue

		var node_type = node_data.get("type", &"unknown")
		if not DialogueGraphResource.is_effect_target_type(node_type):
			# 잘못된 Effect 대상(Say/Choice/Branch/End/Data 등): Flow를 멈추지 않고 건너뛴다.
			push_warning("DialoguePlayer: node %d type '%s' is not a valid effect target; skipping." % [effect_id, str(node_type)])
			continue

		ui_request.emit(_build_portrait_request(node_type, node_data.get("params", {})))

		# Effect-to-Effect 체인: 이 Effect 노드에 연결된 Effect들을 저장 순서대로 잇는다.
		for child in dialogue_resource.get_runtime_effect_node_ids(effect_id):
			queue.append(child)


func _end_dialogue() -> void:
	current_node_id = -1
	waiting_for = &"none"
	dialogue_end.emit()
