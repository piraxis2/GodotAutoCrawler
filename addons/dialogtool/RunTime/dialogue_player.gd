class_name DialoguePlayer extends Node


@export var dialogue_resource: DialogueGraphResource

signal dialogue_started
signal dialogue_end
signal ui_request(request_data: Dictionary)
# 실행 중인 노드가 바뀔 때마다 그 node_id를 알린다(디버깅/하이라이트용).
signal current_node_changed(node_id: int)
# 조건 평가 결과 seam (DT-008 Step 1, ADR-009 D3).
# state_condition Data 노드를 평가할 때마다(구조 invalid 포함) 평가 1회당 정확히 1회 발행한다.
# - condition_node_id: 평가한 state_condition Data 노드 id.
# - consumer_node_id: 이 Data 노드의 입력 포트를 직접 소유한 소비 노드 id
#   (Branch=branch id, expression 중첩=expression id, 에디터 미리보기=-1).
# - report: ConditionEvaluator가 deep copy로 반환한 detached 사본(변조해도 다음 평가에 영향 없음).
# UI/Branch는 report를 재평가하거나 변조하지 않는다. 후속 trace inspector/DialogueHistory의 seam이다.
signal condition_evaluated(condition_node_id: int, consumer_node_id: int, report: Dictionary)

var current_node_id: int = -1
var waiting_for: StringName = &"none"
var selected_choice: int = -1

# 조건부 Choice 대기 동안의 visible index -> 원래 항목 index(= 원래 flow 출력 port) mapping (DT-008 Step 4).
# Choice 진입 시 한 번 구성하고, select_choice가 visible index를 원래 Flow port로 되돌린다.
# start_dialogue/_end_dialogue/Choice 재진입에서 초기화한다. 빈 배열이면 활성 조건부 Choice 대기가 없다.
var _choice_visible_map: Array = []

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
	_choice_visible_map = []
	dialogue_started.emit()
	_execute_until_waiting()


func advance() -> void:
	if dialogue_resource == null or current_node_id == -1:
		return

	if waiting_for == &"text":
		waiting_for = &"none"
		_go_to_next_node(0)
		_execute_until_waiting()


func select_choice(visible_index: int) -> void:
	if dialogue_resource == null or current_node_id == -1:
		return

	if waiting_for != &"choice":
		return

	# F5(ADR-009): visible index를 mapping 범위로 *먼저* 검증한다. 범위 밖이면 경고 후 대기를 유지하고
	# waiting_for/selected_choice/effects/Flow를 전혀 건드리지 않는다(잘못된 입력이 대화를 종료시키거나
	# 엉뚱한 Flow로 진행하지 못하게 함).
	if visible_index < 0 or visible_index >= _choice_visible_map.size():
		push_warning("DialoguePlayer: invalid visible choice index %d (visible count %d); keeping wait." % [visible_index, _choice_visible_map.size()])
		return

	# 검증을 통과한 경우에만 상태를 커밋한다. visible index를 원래 항목 index(= 원래 flow 출력 port)로
	# 되돌린다 — 중간 항목이 숨겨져 있어도 사용자가 고른 항목의 원래 Flow로 정확히 진행한다.
	var original_port: int = _choice_visible_map[visible_index]
	selected_choice = original_port
	waiting_for = &"none"
	_choice_visible_map = []

	# 이 Choice 노드를 떠나기 전에 연결된 Effect를 실행한 뒤 주 Flow로 이동한다(ADR-005).
	_run_effects(current_node_id)

	var next_id = dialogue_resource.get_runtime_next_node_id(current_node_id, original_port)
	if next_id == -1:
		push_warning("DialoguePlayer: choice port %d has no connection; ending dialogue." % original_port)
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
		_choice_visible_map = []
		_end_dialogue()
		return

	# 항목별 Data 입력(port i+1)을 조건으로 평가해 visible list와 visible->original output port mapping을
	# 구성한다(ADR-009 D5). 항목 i의 조건 노드 = get_runtime_input_node_id(choice_id, i+1).
	# - Data 입력이 없는 항목(cond_id == -1)은 unconditional로 항상 표시한다(레거시 Choice 호환).
	# - 조건은 Choice 진입 시 한 번만 평가한다. 대기 중 외부 상태가 바뀌어도 현재 목록/mapping은 고정되고,
	#   Choice에 다시 진입할 때만 재평가한다(재진입 시 이 함수가 mapping을 새로 구성).
	# - invalid/error 조건은 _to_bool이 false로 처리해 숨긴다(state_condition은 fail-closed로 passed=false).
	# consumer는 이 Choice 노드다(입력 포트를 직접 소유) → state_condition signal에 choice id가 전달된다.
	var visible_choices: Array = []
	var visible_map: Array = []   # visible_index -> 원래 항목 index(= 원래 flow 출력 port)
	for i in range(choices.size()):
		var cond_id = dialogue_resource.get_runtime_input_node_id(current_node_id, i + 1)
		var visible := true
		if cond_id != -1:
			# errored(조건/구조 오류, 중첩 Expression 포함)는 fail-closed로 숨긴다(단순 false와 구분).
			var result := _eval_data(cond_id, current_node_id)
			visible = false if result["errored"] else _to_bool(result["value"])
		if visible:
			visible_choices.append(choices[i])
			visible_map.append(i)

	# 모든 항목이 숨겨지면 기존 empty-choice와 같은 종료 정책을 쓴다(ADR-009 D6).
	if visible_choices.is_empty():
		push_warning("DialoguePlayer: all choices hidden at node %d; ending dialogue." % current_node_id)
		_choice_visible_map = []
		_end_dialogue()
		return

	_choice_visible_map = visible_map
	waiting_for = &"choice"
	selected_choice = -1
	ui_request.emit({
		"type": "offer_choice",
		"choices": visible_choices,
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

	# consumer는 이 Branch 노드다(입력 포트 0을 직접 소유). state_condition signal에 전달된다.
	# errored(조건/구조 오류)는 단순 false와 구분해 fail-closed로 항상 false 분기다
	# (ADR-008 error-dominance / ADR-009): Expression이 오류 조건을 true로 뒤집지 못한다.
	var result := _eval_data(input_node_id, current_node_id)
	var condition: bool = false if result["errored"] else _to_bool(result["value"])
	_go_to_next_node(0 if condition else 1)


# 외부 호환 래퍼(에디터 미리보기 expression_node.gd 등): 평가 값만 반환한다.
# 런타임 소비자(Branch/Choice/중첩 Expression)는 _eval_data로 {value, errored}를 받아 fail-closed한다.
func _get_data_value(node_id: int, consumer_node_id: int = -1, visited: Array = []) -> Variant:
	return _eval_data(node_id, consumer_node_id, visited)["value"]


# consumer_node_id는 이 Data 노드의 입력 포트를 직접 소유한 소비 노드 id다(state_condition signal에
# 전달). Branch/Expression 같은 직접 소비 노드가 명시적으로 넘긴다. 에디터 미리보기 등 consumer가
# 없는 호출은 -1을 쓴다.
#
# 반환 {value, errored}: errored는 조건/구조 오류 전파용이다(DT-008 Step 4 P1 수정).
# state_condition의 invalid report, 중첩 Expression 입력 중 하나라도 errored, 순환/미상 노드/
# parse·execute 실패가 모두 errored=true다. errored는 *단순 false와 구분*되어, Expression
# (`not c`/`c or true` 등)이 오류 조건을 true로 뒤집지 못하게 한다(ADR-008 error-dominance /
# ADR-009 fail-closed). 직접 소비자(Branch/Choice)는 errored면 무조건 false/숨김 처리한다.
func _eval_data(node_id: int, consumer_node_id: int = -1, visited: Array = []) -> Dictionary:
	if node_id == -1:
		# 입력 미연결: 값 null, 구조 오류는 아니다(미연결 처리 정책은 소비자에 둔다).
		return {"value": null, "errored": false}

	# 경로 기반 visited 셋으로 순환 data 의존성을 방어한다(순환은 구조 오류 -> errored).
	if node_id in visited:
		push_warning("DialoguePlayer: circular data dependency at node %d; failing closed." % node_id)
		return {"value": null, "errored": true}

	var node_data = dialogue_resource.get_runtime_node(node_id)
	if node_data.is_empty():
		return {"value": null, "errored": true}

	var params = node_data.get("params", {})
	match node_data.get("type", &"unknown"):
		&"variable":
			if params.get("random", false):
				return {"value": randi_range(int(params.get("random_min", 0)), int(params.get("random_max", 0))), "errored": false}
			return {"value": params.get("value"), "errored": false}
		&"expression":
			# expression 입력으로 중첩된 state_condition의 consumer는 이 expression 노드다.
			return _evaluate_expression(node_id, params, visited + [node_id])
		&"state_condition":
			return _evaluate_state_condition(node_id, consumer_node_id, params)
		_:
			push_warning("DialoguePlayer: data node %d type '%s' is not evaluable; failing closed." % [node_id, str(node_data.get("type", &"unknown"))])
			return {"value": null, "errored": true}


# state_condition Data 노드를 평가한다(DT-008 Step 1, ADR-009 D2/D3).
# 주입된 원본 read provider(_read_state_provider)를 ConditionEvaluator에 그대로 전달한다.
# DialoguePlayer.has_state facade를 provider로 다시 감싸지 않아, provider 미지정이
# state_missing이 아니라 evaluator의 provider_missing으로 fail-closed되게 한다.
# 평가 1회당 condition_evaluated를 정확히 1회 발행하고 {value: passed, errored: not valid}를 반환한다.
# null/invalid ConditionSet, missing key, 타입 오류는 valid=false -> errored=true(fail-closed 지배).
# 정상이지만 논리상 false인 조건은 valid=true -> errored=false(이 경우의 false는 Expression이 다룰 수 있음).
func _evaluate_state_condition(node_id: int, consumer_node_id: int, params: Dictionary) -> Dictionary:
	# 잘못된 타입/누락 값은 null로 좁혀 evaluator가 condition_set_null로 fail-closed하게 한다
	# (런타임 snapshot이 손상돼도 크래시 없이 false).
	var raw_set: Variant = params.get("condition_set")
	var condition_set: ConditionSet = raw_set if raw_set is ConditionSet else null

	var report: Dictionary = ConditionEvaluator.evaluate(condition_set, _read_state_provider)
	# 동기 signal listener가 report를 변조해 분기 결과를 뒤집지 못하도록, 발행 전에 passed/valid를
	# 캡처하고 signal에는 별도 deep copy를 넘긴다.
	var passed := bool(report.get("passed", false))
	var errored: bool = not bool(report.get("valid", false))   # invalid report -> errored 전파
	condition_evaluated.emit(node_id, consumer_node_id, report.duplicate(true))
	return {"value": passed, "errored": errored}


# expression data 노드를 평가한다. 각 입력 포트 i는 변수 keys[i]에 바인딩되고,
# 그 값은 해당 포트로 들어오는 런타임 연결을 따라가 해결한다.
# {value, errored}를 반환한다: 입력 중 하나라도 errored이거나 빈 식/parse/execute 실패면 errored=true다.
# 이로써 errored 조건이 `not c`/`c or true` 같은 식을 통해 true로 새지 않는다(error-dominance 전파).
func _evaluate_expression(node_id: int, params: Dictionary, visited: Array) -> Dictionary:
	var expr_string: String = params.get("expression", "")
	if expr_string.is_empty():
		push_warning("DialoguePlayer: expression node %d has empty expression; failing closed." % node_id)
		return {"value": null, "errored": true}

	var keys: Array = params.get("inputs", [])
	var values: Array = []
	var inputs_errored := false
	for port in range(keys.size()):
		var src_id = dialogue_resource.get_runtime_input_node_id(node_id, port)
		# 이 expression 노드가 입력 포트를 직접 소유하므로 consumer는 node_id다.
		var sub := _eval_data(src_id, node_id, visited)
		values.append(sub["value"])
		if sub["errored"]:
			inputs_errored = true

	var expr := Expression.new()
	var err := expr.parse(expr_string, PackedStringArray(keys))
	if err != OK:
		push_warning("DialoguePlayer: expression parse failed at node %d: %s" % [node_id, expr.get_error_text()])
		return {"value": null, "errored": true}

	var result = expr.execute(values, null)
	if expr.has_execute_failed():
		push_warning("DialoguePlayer: expression execute failed at node %d: %s" % [node_id, expr.get_error_text()])
		return {"value": null, "errored": true}

	# 입력 중 하나라도 errored면 결과도 errored로 전파한다(오류 조건이 식으로 뒤집히지 않게).
	return {"value": result, "errored": inputs_errored}


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
	_choice_visible_map = []
	dialogue_end.emit()
