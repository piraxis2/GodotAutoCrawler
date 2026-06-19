# DT-008 Step 1 검증용 헤드리스 테스트(Runtime State Condition Data Node).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/dialogtool/RunTime/tests/dt008_step1_state_condition_test.tscn
#
# 목표: 직접 구성한 runtime snapshot에서 state_condition Data 노드가 ConditionEvaluator 결과를
#       boolean 값으로 제공하고, condition_evaluated signal로 report/consumer를 노출함을 검증한다.
#
# 확정 계약(ADR-009 / DT-008 Step 1):
# - consumer는 Data 입력 포트를 직접 소유한 노드다(Branch=branch id, expression 중첩=expression id).
# - 평가와 signal 발행은 평가당 정확히 1회. 반환값은 report.passed.
# - provider가 없으면 provider_missing으로 fail-closed. null/invalid set, missing key, 타입 오류는 false.
# - 구조 오류는 read_count==0(provider 미접촉). 원본 _read_state_provider를 evaluator에 직접 전달.
# - report는 evaluator deep copy라 변조해도 다음 평가 불변. 기존 Variable/Expression/Branch 유지.
#
# 제외: 에디터 GraphNode UI, ResourcePicker, Adapter/Registry 등록, Choice filtering, .tres 왕복(Step 2+).
extends Node

const OP := StateCondition.Operator
const LG := ConditionGroup.Logic
const SVT := StateDefinition.StateValueType
const SCHEMA_PATH := "res://addons/world_core/world_state/examples/world_state_schema_example.tres"

var _failures: int = 0


# duck-typed read provider 계약 구현(테스트 주입용). has/read 호출 횟수를 따로 센다.
class FakeReadProvider:
	var data: Dictionary
	var has_calls: int = 0
	var read_calls: int = 0
	func _init(d: Dictionary = {}) -> void:
		data = d
	func has_state(key: StringName) -> bool:
		has_calls += 1
		return data.has(key)
	func read_state(key: StringName) -> Variant:
		read_calls += 1
		return data.get(key)
	func try_read_state(key: StringName, fallback: Variant = null) -> Variant:
		return data.get(key, fallback)


func _ready() -> void:
	_test_true_false_set()
	_test_provider_unset()
	_test_null_and_invalid_set()
	_test_missing_key()
	_test_actual_type_mismatch()
	_test_structural_reject_no_read()
	_test_real_store_provider()
	_test_branch_consumer_id()
	_test_expression_nested_consumer_id()
	_test_signal_emitted_once()
	_test_signal_report_matches_return()
	_test_returned_report_mutation_isolated()
	_test_variable_expression_branch_regression()
	_test_circular_data_dependency()
	_test_listener_cannot_alter_branch()

	if _failures == 0:
		print("[DT-008 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-008 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


func _state(key: StringName, op: int, expected: Variant) -> StateCondition:
	var s := StateCondition.new()
	s.key = key
	s.operator = op
	s.expected_value = expected
	return s


func _group(logic: int, children: Array) -> ConditionGroup:
	var g := ConditionGroup.new()
	g.logic = logic
	var typed: Array[ConditionClause] = []
	for c in children:
		typed.append(c)
	g.children = typed
	return g


func _cset(root) -> ConditionSet:
	var cs := ConditionSet.new()
	cs.root = root
	return cs


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


func _codes(r: Dictionary) -> Array:
	var out: Array = []
	for e in r.get("errors", []):
		out.append(e["code"])
	return out


func _has_code(r: Dictionary, code: String) -> bool:
	return _codes(r).has(code)


func _make_store() -> WorldStateStore:
	var schema: StateSchema = ResourceLoader.load(SCHEMA_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var store := WorldStateStore.new()
	store.schema = schema
	store.initialize()
	return store


# 한 state_condition 노드를 직접 구성한 snapshot에 두고 _get_data_value로 평가한다.
# 반환: {value: 평가 bool, events: condition_evaluated 발행 목록}.
func _eval_condition(condition_set, provider, consumer_id: int = -1) -> Dictionary:
	var res := _resource({7: _n(&"state_condition", {"condition_set": condition_set})}, [])
	return _eval_node(res, 7, consumer_id, provider)


# 임의 Data 노드를 _get_data_value로 평가하고 발행된 signal을 수집한다(트리에 붙이지 않음).
func _eval_node(res: DialogueGraphResource, node_id: int, consumer_id: int, provider) -> Dictionary:
	var player := DialoguePlayer.new()
	player.dialogue_resource = res
	if provider != null:
		player.set_read_state_provider(provider)
	var events: Array = []
	player.condition_evaluated.connect(func(cid: int, consumer: int, report: Dictionary):
		events.append({"cid": cid, "consumer": consumer, "report": report}))
	var value = player._get_data_value(node_id, consumer_id)
	player.free()
	return {"value": value, "events": events}


# Start -> Branch(state_condition) -> Say true / Say false 흐름을 실행하고
# 첫 display_text say + condition_evaluated 발행을 함께 수집한다.
# tamper=true면 동기 listener가 발행된 report["passed"]를 true로 변조한다(분기 불변 검증용).
func _run_branch_flow(condition_set, provider, tamper: bool = false) -> Dictionary:
	# 0 start, 1 branch, 2 state_condition, 3 say-true, 4 say-false, 5 end
	var nodes := {
		0: _n(&"start"),
		1: _n(&"branch"),
		2: _n(&"state_condition", {"condition_set": condition_set}),
		3: _n(&"say", {"text": "TRUE"}),
		4: _n(&"say", {"text": "FALSE"}),
		5: _n(&"end"),
	}
	var conns := [
		_c(0, 0, 1, 1),   # start flow -> branch flow 입력(to_port=1, 데이터 입력 0과 충돌 방지)
		_c(2, 0, 1, 0),   # state_condition data -> branch 조건 입력 0
		_c(1, 0, 3, 0),   # branch true -> say TRUE
		_c(1, 1, 4, 0),   # branch false -> say FALSE
		_c(3, 0, 5, 0),
		_c(4, 0, 5, 0),
	]
	var res := _resource(nodes, conns)
	var player := DialoguePlayer.new()
	player.dialogue_resource = res
	if provider != null:
		player.set_read_state_provider(provider)
	var captured := {"say": null}
	var events: Array = []
	player.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "display_text" and captured["say"] == null:
			captured["say"] = req.get("say"))
	player.condition_evaluated.connect(func(cid: int, consumer: int, report: Dictionary):
		events.append({"cid": cid, "consumer": consumer, "report": report}))
	if tamper:
		# 동기 listener가 report를 마구 변조한다. P1: 반환값/분기는 영향받지 않아야 한다.
		player.condition_evaluated.connect(func(_cid: int, _consumer: int, report: Dictionary):
			report["passed"] = true
			report["valid"] = true
			report["read_count"] = 999)
	player.start_dialogue(res)
	var say = captured["say"]
	player.free()
	return {"say": say, "events": events}


# --- 시나리오 ---------------------------------------------------------

func _test_true_false_set() -> void:
	print("[A] true/false ConditionSet -> 정확한 bool")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	var r_true := _eval_condition(_cset(_state(&"quest.main.stage", OP.EQUAL, 5)), provider)
	_check("A.true_value", r_true["value"], true)
	_check("A.true_passed", r_true["events"][0]["report"]["passed"], true)
	_check("A.true_valid", r_true["events"][0]["report"]["valid"], true)

	var r_false := _eval_condition(_cset(_state(&"quest.main.stage", OP.EQUAL, 9)), provider)
	_check("A.false_value", r_false["value"], false)
	_check("A.false_valid", r_false["events"][0]["report"]["valid"], true)   # valid지만 논리 false
	_check("A.false_passed", r_false["events"][0]["report"]["passed"], false)


func _test_provider_unset() -> void:
	print("[B] provider 미지정 -> provider_missing fail-closed")
	var r := _eval_condition(_cset(_state(&"quest.main.stage", OP.EQUAL, 5)), null)
	_check("B.value", r["value"], false)
	_check("B.valid", r["events"][0]["report"]["valid"], false)
	_check_true("B.provider_missing", _has_code(r["events"][0]["report"], "provider_missing"))
	_check("B.read_count", r["events"][0]["report"]["read_count"], 0)


func _test_null_and_invalid_set() -> void:
	print("[C] null / invalid ConditionSet -> false")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	# null set: 잘못된 타입/누락은 evaluator가 condition_set_null로 fail-closed.
	var r_null := _eval_condition(null, provider)
	_check("C.null_value", r_null["value"], false)
	_check_true("C.null_code", _has_code(r_null["events"][0]["report"], "condition_set_null"))

	# invalid set: empty ALL group -> group_empty 구조 오류.
	var r_invalid := _eval_condition(_cset(_group(LG.ALL, [])), provider)
	_check("C.invalid_value", r_invalid["value"], false)
	_check_true("C.invalid_code", _has_code(r_invalid["events"][0]["report"], "group_empty"))


func _test_missing_key() -> void:
	print("[D] missing key -> state_missing, read_count 1")
	var provider := FakeReadProvider.new({})   # 비어 있음
	var r := _eval_condition(_cset(_state(&"quest.main.stage", OP.EQUAL, 5)), provider)
	_check("D.value", r["value"], false)
	_check_true("D.state_missing", _has_code(r["events"][0]["report"], "state_missing"))
	_check("D.read_count", r["events"][0]["report"]["read_count"], 1)


func _test_actual_type_mismatch() -> void:
	print("[E] actual type mismatch -> false")
	# expected int 5, 실제 provider 값은 String -> strict typeof 불일치.
	var provider := FakeReadProvider.new({&"quest.main.stage": "five"})
	var r := _eval_condition(_cset(_state(&"quest.main.stage", OP.EQUAL, 5)), provider)
	_check("E.value", r["value"], false)
	_check_true("E.mismatch", _has_code(r["events"][0]["report"], "actual_type_mismatch"))


func _test_structural_reject_no_read() -> void:
	print("[F] 구조 invalid -> provider read 0(미접촉)")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	var r := _eval_condition(_cset(_group(LG.ALL, [])), provider)   # empty group
	_check("F.value", r["value"], false)
	_check("F.read_count", r["events"][0]["report"]["read_count"], 0)
	_check("F.provider_has_calls", provider.has_calls, 0)
	_check("F.provider_read_calls", provider.read_calls, 0)


func _test_real_store_provider() -> void:
	print("[G] 실제 WorldStateStore provider")
	var store := _make_store()
	_check("G.ready", store.is_store_ready(), true)
	var cs := _cset(_state(&"quest.main.stage", OP.GREATER_EQUAL, 3))
	var before := _eval_condition(cs, store)
	_check("G.before", before["value"], false)   # default 0 >= 3 거짓
	_check("G.before_valid", before["events"][0]["report"]["valid"], true)
	store.set_value(&"quest.main.stage", 5)
	var after := _eval_condition(cs, store)
	_check("G.after", after["value"], true)       # 5 >= 3 참
	store.free()


func _test_branch_consumer_id() -> void:
	print("[H] Branch consumer == branch node id")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	var r := _run_branch_flow(_cset(_state(&"quest.main.stage", OP.EQUAL, 5)), provider)
	_check("H.routed_true", r["say"], "TRUE")
	_check("H.one_event", r["events"].size(), 1)
	_check("H.condition_node", r["events"][0]["cid"], 2)   # state_condition node id
	_check("H.consumer_branch", r["events"][0]["consumer"], 1)   # branch node id

	# false 경로도 같은 consumer로 라우팅.
	var r_false := _run_branch_flow(_cset(_state(&"quest.main.stage", OP.EQUAL, 9)), provider)
	_check("H.routed_false", r_false["say"], "FALSE")
	_check("H.consumer_branch_false", r_false["events"][0]["consumer"], 1)


func _test_expression_nested_consumer_id() -> void:
	print("[I] Expression 중첩 consumer == expression node id")
	# state_condition(2) -> expression(4, "c") 입력 포트 0. expression의 consumer는 임의 상위 노드(99).
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	var nodes := {
		2: _n(&"state_condition", {"condition_set": _cset(_state(&"quest.main.stage", OP.EQUAL, 5))}),
		4: _n(&"expression", {"expression": "c", "inputs": ["c"]}),
	}
	var conns := [_c(2, 0, 4, 0)]   # state_condition -> expression 입력 0
	var res := _resource(nodes, conns)
	# 상위 소비자를 99로 넘겨도 중첩 state_condition의 consumer는 expression id(4)여야 한다.
	var r := _eval_node(res, 4, 99, provider)
	_check("I.value", r["value"], true)          # expression "c" -> true
	_check("I.one_event", r["events"].size(), 1)
	_check("I.condition_node", r["events"][0]["cid"], 2)
	_check("I.consumer_expression", r["events"][0]["consumer"], 4)   # branch(99)가 아니라 expression


func _test_signal_emitted_once() -> void:
	print("[J] 평가당 signal 정확히 1회")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	var r := _eval_condition(_cset(_state(&"quest.main.stage", OP.EQUAL, 5)), provider)
	_check("J.events_size", r["events"].size(), 1)


func _test_signal_report_matches_return() -> void:
	print("[K] signal report.passed == 반환 bool")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	var r_true := _eval_condition(_cset(_state(&"quest.main.stage", OP.EQUAL, 5)), provider)
	_check("K.true_match", r_true["value"], r_true["events"][0]["report"]["passed"])
	var r_false := _eval_condition(_cset(_state(&"quest.main.stage", OP.EQUAL, 9)), provider)
	_check("K.false_match", r_false["value"], r_false["events"][0]["report"]["passed"])


func _test_returned_report_mutation_isolated() -> void:
	print("[L] 반환 report 변조 후 재평가 불변(deep copy)")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	var cs := _cset(_state(&"quest.main.stage", OP.EQUAL, 9))   # 논리 false, valid
	var first := _eval_condition(cs, provider)
	var report: Dictionary = first["events"][0]["report"]
	# 받은 report를 마구 변조한다.
	report["passed"] = true
	report["valid"] = false
	report["read_count"] = 999
	report["errors"].append({"code": "tampered"})
	# 같은 ConditionSet을 다시 평가하면 변조 영향 없이 동일 결과여야 한다.
	var second := _eval_condition(cs, provider)
	_check("L.value", second["value"], false)
	_check("L.passed", second["events"][0]["report"]["passed"], false)
	_check("L.valid", second["events"][0]["report"]["valid"], true)
	_check("L.read_count", second["events"][0]["report"]["read_count"], 1)
	_check("L.no_tamper", _has_code(second["events"][0]["report"], "tampered"), false)


func _test_variable_expression_branch_regression() -> void:
	print("[M] 기존 Variable/Expression Branch 회귀")
	# Variable -> Branch
	var var_nodes := {
		0: _n(&"start"), 1: _n(&"branch"),
		2: _n(&"say", {"text": "v-true"}), 3: _n(&"say", {"text": "v-false"}),
		4: _n(&"variable", {"value": true}), 5: _n(&"end"),
	}
	var var_conns := [
		_c(0, 0, 1, 1), _c(4, 0, 1, 0), _c(1, 0, 2, 0), _c(1, 1, 3, 0), _c(2, 0, 5, 0), _c(3, 0, 5, 0),
	]
	_check("M.variable_true", _capture_say(_resource(var_nodes, var_conns)), "v-true")

	# Expression -> Branch
	var expr_nodes := {
		0: _n(&"start"), 1: _n(&"branch"),
		2: _n(&"say", {"text": "e-true"}), 3: _n(&"say", {"text": "e-false"}),
		4: _n(&"expression", {"expression": "x > 0", "inputs": ["x"]}),
		6: _n(&"variable", {"value": 1}), 5: _n(&"end"),
	}
	var expr_conns := [
		_c(0, 0, 1, 1), _c(6, 0, 4, 0), _c(4, 0, 1, 0),
		_c(1, 0, 2, 0), _c(1, 1, 3, 0), _c(2, 0, 5, 0), _c(3, 0, 5, 0),
	]
	_check("M.expression_true", _capture_say(_resource(expr_nodes, expr_conns)), "e-true")


func _test_circular_data_dependency() -> void:
	print("[N] circular data dependency 회귀(크래시 없음)")
	# expression 노드가 자기 자신을 데이터 입력으로 참조한다.
	var nodes := {4: _n(&"expression", {"expression": "x", "inputs": ["x"]})}
	var conns := [_c(4, 0, 4, 0)]   # self data input
	var res := _resource(nodes, conns)
	var r := _eval_node(res, 4, -1, null)
	_check("N.value_null", r["value"], null)   # 순환 방어 -> null, 크래시 없음
	_check("N.no_condition_event", r["events"].size(), 0)


func _test_listener_cannot_alter_branch() -> void:
	print("[O] signal listener의 report 변조가 반환값/분기를 못 바꿈(P1)")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	# 논리 false(valid)인 조건. listener가 passed=true로 바꿔도 false 유지여야 한다.
	var cs := _cset(_state(&"quest.main.stage", OP.EQUAL, 9))

	# (1) 직접 _get_data_value 경로: 동기 listener가 report를 변조.
	var res := _resource({7: _n(&"state_condition", {"condition_set": cs})}, [])
	var player := DialoguePlayer.new()
	player.dialogue_resource = res
	player.set_read_state_provider(provider)
	player.condition_evaluated.connect(func(_cid: int, _consumer: int, report: Dictionary):
		report["passed"] = true
		report["valid"] = true)
	var value = player._get_data_value(7, -1)
	player.free()
	_check("O.return_false", value, false)   # listener 변조에도 false 유지

	# (2) Branch flow 경로: listener 변조에도 FALSE Flow로 라우팅.
	var r := _run_branch_flow(cs, provider, true)
	_check("O.routed_false", r["say"], "FALSE")

	# 대조: 변조 없이도 같은 조건은 FALSE다(테스트가 우연히 통과한 게 아님을 보장).
	var baseline := _run_branch_flow(cs, provider, false)
	_check("O.baseline_false", baseline["say"], "FALSE")


# Start -> Branch flow를 실행하고 첫 display_text say를 반환(트리에 붙이지 않음).
func _capture_say(res: DialogueGraphResource) -> Variant:
	var player := DialoguePlayer.new()
	var captured := {"say": null}
	player.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "display_text" and captured["say"] == null:
			captured["say"] = req.get("say"))
	player.start_dialogue(res)
	var say = captured["say"]
	player.free()
	return say
