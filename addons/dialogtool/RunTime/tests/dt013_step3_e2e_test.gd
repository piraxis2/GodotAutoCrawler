# DT-013 Step 3 검증용 헤드리스 e2e 테스트(State Read End-to-End Integration).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt013_step3_e2e_test.tscn
#
# 실제 DialogueManager -> DialogueUI -> DialoguePlayer provider 주입 경로에서 state_read Data 노드가
# 값 supplier로 동작하는지 검증한다(런타임/에디터는 Step 1/2에서 확정, 이 Step은 제품 코드 변경 없음).
# 통합 그래프:
#   State Read(INT) -> Expression(x>5) -> Branch -> Say TRUE/FALSE
#   State Read(BOOL) -> Branch -> Say TRUE/FALSE
#   State Read(BOOL) -> Choice 항목 조건 -> 가시 항목 list
# provider는 실제 WorldStateStore(example schema) 및 debug preview store를 주입한다(/root 직접 조회 없음).
#
# 검증 범위(DT-013 Step 3 Required tests):
# - State Read(INT) -> Expression 비교 -> Branch 분기.
# - State Read(BOOL) -> Branch / Choice 조건.
# - provider 누락 / unknown key / type mismatch가 fail-closed(Branch false / 항목 숨김)이고 상태 불변.
# - debug preview example schema key는 읽히고, 없는 game schema key는 state_missing으로 닫힌다.
# - SCRIPT ERROR 0.
extends Node

const SCHEMA_PATH := "res://addons/dialogtool/examples/world_state_schema_example.tres"

var _failures: int = 0
var _stores: Array = []


func _ready() -> void:
	_install_watchdog(30.0)
	await _run_all()
	for s in _stores:
		if is_instance_valid(s):
			s.free()
	if _failures == 0:
		print("[DT-013 Step3] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-013 Step3] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-013 Step3] WATCHDOG TIMEOUT after %.0fs — 미완료 종료(행 가능성). --import 선행 여부 확인." % seconds)
		get_tree().quit(2))


func _run_all() -> void:
	await _test_int_expression_branch()
	await _test_bool_branch()
	await _test_bool_choice()
	await _test_provider_missing_fail_closed()
	await _test_unknown_key_fail_closed_state_unchanged()
	await _test_type_mismatch_fail_closed_state_unchanged()
	await _test_debug_preview_provider()


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


func _make_store() -> WorldStateStore:
	var schema: StateSchema = ResourceLoader.load(SCHEMA_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var store := WorldStateStore.new()
	store.schema = schema
	store.initialize()
	_stores.append(store)
	return store


func _n(type: StringName, params: Dictionary = {}) -> Dictionary:
	return {"type": type, "params": params}


func _c(from_id: int, from_port: int, to_id: int, to_port: int) -> Dictionary:
	return {"from_node_id": from_id, "from_port": from_port, "to_node_id": to_id, "to_port": to_port}


func _resource(nodes: Dictionary, conns: Array) -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = nodes
	var typed: Array[Dictionary] = []
	for c in conns:
		typed.append(c)
	res.runtime_connections = typed
	res.start_node_id = 0
	return res


# State Read(INT) -> Expression("x > 5") -> Branch -> Say TRUE/FALSE.
func _graph_int_expr(key: StringName) -> DialogueGraphResource:
	return _resource({
		0: _n(&"start"),
		1: _n(&"branch"),
		2: _n(&"state_read", {"key": key, "value_type": TYPE_INT}),
		6: _n(&"expression", {"expression": "x > 5", "inputs": ["x"]}),
		3: _n(&"say", {"text": "TRUE"}),
		4: _n(&"say", {"text": "FALSE"}),
		5: _n(&"end"),
	}, [
		_c(0, 0, 1, 1),   # start flow -> branch flow-in(port 1)
		_c(2, 0, 6, 0),   # state_read -> expression input 0
		_c(6, 0, 1, 0),   # expression -> branch 조건 입력(port 0)
		_c(1, 0, 3, 0),
		_c(1, 1, 4, 0),
		_c(3, 0, 5, 0),
		_c(4, 0, 5, 0),
	])


# State Read(value_type) -> Branch -> Say TRUE/FALSE.
func _graph_branch(key: StringName, vtype: int) -> DialogueGraphResource:
	return _resource({
		0: _n(&"start"),
		1: _n(&"branch"),
		2: _n(&"state_read", {"key": key, "value_type": vtype}),
		3: _n(&"say", {"text": "TRUE"}),
		4: _n(&"say", {"text": "FALSE"}),
		5: _n(&"end"),
	}, [
		_c(0, 0, 1, 1),
		_c(2, 0, 1, 0),
		_c(1, 0, 3, 0),
		_c(1, 1, 4, 0),
		_c(3, 0, 5, 0),
		_c(4, 0, 5, 0),
	])


# State Read(BOOL) -> Choice 항목0 조건. 항목1은 무조건 표시.
func _graph_choice(key: StringName) -> DialogueGraphResource:
	return _resource({
		0: _n(&"start"),
		1: _n(&"choice", {"choices": ["A", "B"]}),
		2: _n(&"state_read", {"key": key, "value_type": TYPE_BOOL}),
		3: _n(&"say", {"text": "PICK-A"}),
		4: _n(&"say", {"text": "PICK-B"}),
		5: _n(&"end"),
	}, [
		_c(0, 0, 1, 0),   # start -> choice flow-in(port 0)
		_c(2, 0, 1, 1),   # state_read -> choice 항목0 조건 입력(port 1)
		_c(1, 0, 3, 0),
		_c(1, 1, 4, 0),
		_c(3, 0, 5, 0),
		_c(4, 0, 5, 0),
	])


# DialogueManager 경로로 그래프를 실행하고 첫 say / 첫 offer_choice / state_read_evaluated를 수집한다.
func _run(graph: DialogueGraphResource, read_provider) -> Dictionary:
	var says: Array = []
	var choices: Array = []
	var events: Array = []
	var ui_cb := func(req: Dictionary):
		if req.get("type") == "display_text":
			says.append(req.get("say"))
		elif req.get("type") == "offer_choice":
			choices.append(req.get("choices"))
	DialogueManager.ui_request.connect(ui_cb)

	DialogueManager.play(graph, read_provider)
	# UI/player는 play() 내부 add_child로 동기 생성된다 → 시작(deferred) 전에 signal 연결 가능.
	var player: DialoguePlayer = DialogueManager._ui.dialogue_player
	var ev_cb := func(rid: int, consumer: int, report: Dictionary):
		events.append({"rid": rid, "consumer": consumer, "report": report})
	player.state_read_evaluated.connect(ev_cb)

	await get_tree().process_frame
	await get_tree().process_frame

	DialogueManager.ui_request.disconnect(ui_cb)
	DialogueManager._dismiss()
	await get_tree().process_frame
	return {
		"say": says[0] if says.size() > 0 else null,
		"choices": choices[0] if choices.size() > 0 else null,
		"events": events,
	}


func _report0(r: Dictionary) -> Dictionary:
	if r["events"].size() > 0:
		return r["events"][0]["report"]
	return {}


# --- 시나리오 ---------------------------------------------------------

func _test_int_expression_branch() -> void:
	print("[A] State Read(INT) -> Expression(x>5) -> Branch")
	var store := _make_store()
	store.set_value(&"quest.main.stage", 7)
	var r_true := await _run(_graph_int_expr(&"quest.main.stage"), store)
	_check("A.true_say", r_true["say"], "TRUE")
	_check("A.ok", _report0(r_true).get("ok"), true)
	_check("A.value", _report0(r_true).get("value"), 7)
	# state_read의 직접 소비자는 expression 노드(6)다.
	_check("A.consumer_expression", r_true["events"][0]["consumer"], 6)

	store.set_value(&"quest.main.stage", 5)   # 5 > 5 거짓
	var r_false := await _run(_graph_int_expr(&"quest.main.stage"), store)
	_check("A.false_say", r_false["say"], "FALSE")


func _test_bool_branch() -> void:
	print("[B] State Read(BOOL) -> Branch")
	var store := _make_store()
	store.set_value(&"session.intro.seen", true)
	var r_true := await _run(_graph_branch(&"session.intro.seen", TYPE_BOOL), store)
	_check("B.true_say", r_true["say"], "TRUE")
	_check("B.consumer_branch", r_true["events"][0]["consumer"], 1)

	store.set_value(&"session.intro.seen", false)   # valid false -> Branch FALSE
	var r_false := await _run(_graph_branch(&"session.intro.seen", TYPE_BOOL), store)
	_check("B.false_say", r_false["say"], "FALSE")
	_check("B.false_ok", _report0(r_false).get("ok"), true)   # 읽기 성공(논리 false)


func _test_bool_choice() -> void:
	print("[C] State Read(BOOL) -> Choice 항목 조건")
	var store := _make_store()
	# true면 항목0 표시 -> ["A","B"].
	store.set_value(&"session.intro.seen", true)
	var r_show := await _run(_graph_choice(&"session.intro.seen"), store)
	_check("C.show_both", r_show["choices"], ["A", "B"])
	_check("C.consumer_choice", r_show["events"][0]["consumer"], 1)

	# 유효 false면 항목0 숨김 -> ["B"].
	store.set_value(&"session.intro.seen", false)
	var r_hide := await _run(_graph_choice(&"session.intro.seen"), store)
	_check("C.hide_item0", r_hide["choices"], ["B"])


func _test_provider_missing_fail_closed() -> void:
	print("[D] provider 미지정 -> fail-closed(Branch FALSE / 항목 숨김)")
	var r_branch := await _run(_graph_branch(&"session.intro.seen", TYPE_BOOL), null)
	_check("D.branch_false", r_branch["say"], "FALSE")
	_check("D.error", _report0(r_branch).get("error"), &"provider_missing")

	var r_choice := await _run(_graph_choice(&"session.intro.seen"), null)
	_check("D.choice_hidden", r_choice["choices"], ["B"])


func _test_unknown_key_fail_closed_state_unchanged() -> void:
	print("[E] unknown key -> state_missing fail-closed + 상태 불변")
	var store := _make_store()
	store.set_value(&"quest.main.stage", 9)   # 무관한 sentinel
	# 형식은 유효하지만 schema에 없는 key.
	var r := await _run(_graph_branch(&"quest.unknown.flag", TYPE_BOOL), store)
	_check("E.false", r["say"], "FALSE")
	_check("E.state_missing", _report0(r).get("error"), &"state_missing")
	# State Read는 순수 read이므로 store 값은 그대로다.
	_check("E.state_unchanged", store.get_value(&"quest.main.stage"), 9)


func _test_type_mismatch_fail_closed_state_unchanged() -> void:
	print("[F] type mismatch -> actual_type_mismatch fail-closed + 상태 불변")
	var store := _make_store()
	store.set_value(&"player.health", 42.5)   # FLOAT
	# expected INT인데 실제는 FLOAT -> actual_type_mismatch -> errored -> Branch FALSE.
	# (errored Data를 Branch에 직접 공급해 fail-closed를 본다. 비교 연산이 null 입력에 닿는 Expression 경로는
	#  error-dominance 회귀로 dt013_step1[K]에서 별도 검증한다.)
	var r := await _run(_graph_branch(&"player.health", TYPE_INT), store)
	_check("F.false", r["say"], "FALSE")
	_check("F.mismatch", _report0(r).get("error"), &"actual_type_mismatch")
	# report는 실제 읽은 값/타입을 보존하되 Data value는 errored로 전파.
	_check("F.actual_type", _report0(r).get("actual_type"), TYPE_FLOAT)
	_check("F.state_unchanged", store.get_value(&"player.health"), 42.5)


func _test_debug_preview_provider() -> void:
	print("[G] debug preview store: example key 읽힘 / 없는 game key는 state_missing")
	var preview := DialogueDebugPreviewProvider.make_preview_store()
	_check_true("G.preview_ready", preview != null and preview.is_store_ready())
	_stores.append(preview)
	# example schema key는 정상 읽힘.
	preview.set_value(&"session.intro.seen", true)
	var r_ok := await _run(_graph_branch(&"session.intro.seen", TYPE_BOOL), preview)
	_check("G.example_key_true", r_ok["say"], "TRUE")
	_check("G.example_key_ok", _report0(r_ok).get("ok"), true)

	# example schema에 없는 game schema key는 state_missing으로 닫힌다.
	var r_missing := await _run(_graph_branch(&"game.only.flag", TYPE_BOOL), preview)
	_check("G.game_key_false", r_missing["say"], "FALSE")
	_check("G.game_key_state_missing", _report0(r_missing).get("error"), &"state_missing")
