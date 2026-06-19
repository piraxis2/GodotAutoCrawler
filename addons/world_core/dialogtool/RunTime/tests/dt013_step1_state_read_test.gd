# DT-013 Step 1 검증용 헤드리스 테스트(Runtime State Read Evaluator).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/dialogtool/RunTime/tests/dt013_step1_state_read_test.tscn
#
# 목표: 직접 구성한 runtime snapshot에서 state_read Data 노드가 주입된 read provider로부터 단일 key
#       값을 strict typeof로 읽어 Data value로 공급하고, state_read_evaluated signal로 report/consumer를
#       노출하며, 모든 실패가 SCRIPT ERROR 없이 구조화 report + Data error-dominance로 닫힘을 검증한다.
#
# 확정 계약(ADR-015 / DT-013 Step 1):
# - params: key(StringName|String), value_type(int; TYPE_BOOL/INT/FLOAT/STRING/STRING_NAME).
# - provider는 주입된 _read_state_provider만 직접 소비(/root/WorldState 미조회).
# - null=provider_missing, 계약 위반=provider_contract_invalid(read_state도 호출 전 차단),
#   has_state==false=state_missing(read_state 호출 0), typeof 불일치=actual_type_mismatch.
# - String/StringName key만 정규화, 그 외 손상 Variant=key_invalid.
# - 실패={value:null, errored:true}, 성공={value, errored:false}. signal=평가당 1회, detached report.
# - report sentinel: 값 미읽기 실패는 actual_type=TYPE_NIL/value=null, type mismatch는 실제 타입/값 보존.
#
# 제외: 에디터 GraphNode UI, Definition, Adapter/Registry 등록, .tres 왕복(Step 2+).
extends Node

const SCHEMA_PATH := "res://addons/world_core/world_state/examples/world_state_schema_example.tres"

var _failures: int = 0


# --- duck-typed read provider 변형(테스트 주입용) ---------------------

# 정상 read provider 계약(has_state/read_state). 호출 횟수를 따로 센다.
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


# read_state 메서드가 아예 없는 provider(계약 위반).
class NoReadMethodProvider:
	func has_state(key: StringName) -> bool:
		return true


# read_state arity 불일치(2개 필수 인자) — 호출 전 차단되어야 한다.
class ReadArityProvider:
	var read_calls: int = 0
	func has_state(key: StringName) -> bool:
		return true
	func read_state(key: StringName, extra) -> Variant:
		read_calls += 1
		return null


# read_state 첫 인자가 typed int — StringName key 호출 시 SCRIPT ERROR가 나므로 호출 전 차단.
class ReadTypedIntProvider:
	var read_calls: int = 0
	func has_state(key: StringName) -> bool:
		return true
	func read_state(key: int) -> Variant:
		read_calls += 1
		return null


# has_state 메서드가 없는 provider(계약 위반).
class NoHasMethodProvider:
	func read_state(key: StringName) -> Variant:
		return null


# has_state arity 불일치(0개 인자).
class HasArityProvider:
	func has_state() -> bool:
		return true
	func read_state(key: StringName) -> Variant:
		return null


# has_state 선언 반환형이 non-bool(int) — 계약 검증이 거부해야 한다.
class HasDeclaredIntProvider:
	func has_state(key: StringName) -> int:
		return 1
	func read_state(key: StringName) -> Variant:
		return null


# has_state 선언은 untyped지만 런타임에 non-bool을 반환 — 런타임 방어가 거부해야 한다.
class HasRuntimeNonBoolProvider:
	var read_calls: int = 0
	func has_state(key):
		return 7   # untyped 선언, non-bool 반환
	func read_state(key: StringName) -> Variant:
		read_calls += 1
		return null


func _ready() -> void:
	_test_success_all_types()
	_test_provider_missing()
	_test_provider_contract_invalid_matrix()
	_test_read_state_contract_blocked_before_call()
	_test_state_missing_no_read()
	_test_key_string_stringname_compat()
	_test_key_invalid_variants()
	_test_actual_type_mismatch_no_coercion()
	_test_report_sentinels()
	_test_error_dominance_branch()
	_test_error_dominance_expression()
	_test_error_dominance_choice()
	_test_value_supplier_into_expression()
	_test_signal_once_consumer_detached()

	if _failures == 0:
		print("[DT-013 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-013 Step1] FAILED: %d assertion(s)" % _failures)
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


func _make_store() -> WorldStateStore:
	var schema: StateSchema = ResourceLoader.load(SCHEMA_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var store := WorldStateStore.new()
	store.schema = schema
	store.initialize()
	return store


# state_read 노드 하나(id 7)를 구성해 _eval_data로 평가하고 발행 signal을 수집한다.
# 반환: {value, errored, events:[{rid, consumer, report}]}.
func _eval_read(params: Dictionary, provider, consumer_id: int = -1) -> Dictionary:
	var res := _resource({7: _n(&"state_read", params)}, [])
	var player := DialoguePlayer.new()
	player.dialogue_resource = res
	# 무조건 주입한다(freed Object는 `!= null`이 false라 가드를 두면 주입을 건너뛰어 genuine null로
	# 오인된다 — provider_missing이 아니라 provider_contract_invalid로 분류되어야 하므로 가드 금지).
	player.set_read_state_provider(provider)
	var events: Array = []
	player.state_read_evaluated.connect(func(rid: int, consumer: int, report: Dictionary):
		events.append({"rid": rid, "consumer": consumer, "report": report}))
	var result: Dictionary = player._eval_data(7, consumer_id)
	player.free()
	return {"value": result["value"], "errored": result["errored"], "events": events}


# Start -> Branch(state_read 또는 expression) -> Say TRUE / Say FALSE 흐름을 실행하고 첫 say를 반환한다.
# data_nodes: branch 입력(port 0)으로 연결될 Data 노드 서브그래프. data_root_id가 branch 조건이다.
func _run_branch_flow(data_nodes: Dictionary, data_conns: Array, data_root_id: int, provider) -> Dictionary:
	var nodes := {
		0: _n(&"start"),
		1: _n(&"branch"),
		3: _n(&"say", {"text": "TRUE"}),
		4: _n(&"say", {"text": "FALSE"}),
		5: _n(&"end"),
	}
	for k in data_nodes:
		nodes[k] = data_nodes[k]
	var conns := [
		_c(0, 0, 1, 1),               # start flow -> branch flow 입력(to_port=1)
		_c(data_root_id, 0, 1, 0),    # data root -> branch 조건 입력 0
		_c(1, 0, 3, 0),               # branch true -> say TRUE
		_c(1, 1, 4, 0),               # branch false -> say FALSE
		_c(3, 0, 5, 0),
		_c(4, 0, 5, 0),
	]
	for cn in data_conns:
		conns.append(cn)
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
	player.state_read_evaluated.connect(func(rid: int, consumer: int, report: Dictionary):
		events.append({"rid": rid, "consumer": consumer, "report": report}))
	player.start_dialogue(res)
	var say = captured["say"]
	player.free()
	return {"say": say, "events": events}


# --- 시나리오 ---------------------------------------------------------

func _test_success_all_types() -> void:
	print("[A] 실제 WorldStateStore에서 BOOL/INT/FLOAT/STRING/STRING_NAME success")
	var store := _make_store()
	_check("A.ready", store.is_store_ready(), true)
	# 쓰기 가능 key는 값을 설정하고, read-only(world.build.channel)는 default를 읽는다.
	_check("A.set_bool", store.set_value(&"session.intro.seen", true), OK)
	_check("A.set_int", store.set_value(&"quest.main.stage", 7), OK)
	_check("A.set_float", store.set_value(&"player.health", 42.5), OK)
	_check("A.set_string", store.set_value(&"player.display_name", "Hero"), OK)

	var rb := _eval_read({"key": &"session.intro.seen", "value_type": TYPE_BOOL}, store)
	_check("A.bool_value", rb["value"], true)
	_check("A.bool_errored", rb["errored"], false)
	_check("A.bool_ok", rb["events"][0]["report"]["ok"], true)
	_check("A.bool_actual_type", rb["events"][0]["report"]["actual_type"], TYPE_BOOL)

	var ri := _eval_read({"key": &"quest.main.stage", "value_type": TYPE_INT}, store)
	_check("A.int_value", ri["value"], 7)
	_check("A.int_errored", ri["errored"], false)

	var rf := _eval_read({"key": &"player.health", "value_type": TYPE_FLOAT}, store)
	_check("A.float_value", rf["value"], 42.5)
	_check("A.float_errored", rf["errored"], false)

	var rs := _eval_read({"key": &"player.display_name", "value_type": TYPE_STRING}, store)
	_check("A.string_value", rs["value"], "Hero")
	_check("A.string_errored", rs["errored"], false)

	var rsn := _eval_read({"key": &"world.build.channel", "value_type": TYPE_STRING_NAME}, store)
	_check("A.string_name_value", rsn["value"], &"dev")
	_check("A.string_name_errored", rsn["errored"], false)
	_check("A.string_name_actual_type", rsn["events"][0]["report"]["actual_type"], TYPE_STRING_NAME)
	store.free()


func _test_provider_missing() -> void:
	print("[B] provider 미지정 -> provider_missing fail-closed")
	var r := _eval_read({"key": &"quest.main.stage", "value_type": TYPE_INT}, null)
	_check("B.value", r["value"], null)
	_check("B.errored", r["errored"], true)
	_check("B.ok", r["events"][0]["report"]["ok"], false)
	_check("B.error", r["events"][0]["report"]["error"], &"provider_missing")
	_check("B.actual_type", r["events"][0]["report"]["actual_type"], TYPE_NIL)
	_check("B.report_value", r["events"][0]["report"]["value"], null)


func _test_provider_contract_invalid_matrix() -> void:
	print("[C] provider_contract_invalid matrix")
	var params := {"key": &"quest.main.stage", "value_type": TYPE_INT}

	# non-Object provider(int).
	var r_nonobj := _eval_read(params, 123)
	_check("C.nonobject_errored", r_nonobj["errored"], true)
	_check("C.nonobject_error", r_nonobj["events"][0]["report"]["error"], &"provider_contract_invalid")

	# non-Object provider(Dictionary).
	var r_dict := _eval_read(params, {"not": "a provider"})
	_check("C.dict_error", r_dict["events"][0]["report"]["error"], &"provider_contract_invalid")

	# freed Object(dt009와 동일하게 WorldStateStore를 free 후 주입 — typeof는 OBJECT로 남아
	# is_instance_valid 사전 검사가 contract invalid로 거른다).
	var freed := WorldStateStore.new()
	freed.free()
	var r_freed := _eval_read(params, freed)
	_check("C.freed_error", r_freed["events"][0]["report"]["error"], &"provider_contract_invalid")

	# has_state 메서드 없음.
	var r_nohas := _eval_read(params, NoHasMethodProvider.new())
	_check("C.no_has_error", r_nohas["events"][0]["report"]["error"], &"provider_contract_invalid")

	# has_state arity 불일치.
	var r_hasarity := _eval_read(params, HasArityProvider.new())
	_check("C.has_arity_error", r_hasarity["events"][0]["report"]["error"], &"provider_contract_invalid")

	# has_state 선언 반환형 non-bool(int).
	var r_hasdecl := _eval_read(params, HasDeclaredIntProvider.new())
	_check("C.has_declared_int_error", r_hasdecl["events"][0]["report"]["error"], &"provider_contract_invalid")

	# has_state 런타임 non-bool 반환.
	var runtime_nonbool := HasRuntimeNonBoolProvider.new()
	var r_hasrt := _eval_read(params, runtime_nonbool)
	_check("C.has_runtime_nonbool_error", r_hasrt["events"][0]["report"]["error"], &"provider_contract_invalid")
	# 런타임 non-bool은 has_state는 호출되지만 read_state는 호출되지 않는다.
	_check("C.has_runtime_no_read", runtime_nonbool.read_calls, 0)


func _test_read_state_contract_blocked_before_call() -> void:
	print("[D] read_state 계약 위반은 호출 전에 차단(provider_contract_invalid, read_state 호출 0)")
	var params := {"key": &"quest.main.stage", "value_type": TYPE_INT}

	# read_state 메서드 없음.
	var r_noread := _eval_read(params, NoReadMethodProvider.new())
	_check("D.no_read_error", r_noread["events"][0]["report"]["error"], &"provider_contract_invalid")

	# read_state arity 불일치 — 호출 전 차단.
	var arity := ReadArityProvider.new()
	var r_arity := _eval_read(params, arity)
	_check("D.read_arity_error", r_arity["events"][0]["report"]["error"], &"provider_contract_invalid")
	_check("D.read_arity_no_call", arity.read_calls, 0)

	# read_state 첫 인자 typed int — 호출 전 차단.
	var typed_int := ReadTypedIntProvider.new()
	var r_typed := _eval_read(params, typed_int)
	_check("D.read_typed_int_error", r_typed["events"][0]["report"]["error"], &"provider_contract_invalid")
	_check("D.read_typed_int_no_call", typed_int.read_calls, 0)


func _test_state_missing_no_read() -> void:
	print("[E] has_state==false -> state_missing, read_state 호출 0")
	var provider := FakeReadProvider.new({})   # 비어 있음
	var r := _eval_read({"key": &"quest.main.stage", "value_type": TYPE_INT}, provider)
	_check("E.value", r["value"], null)
	_check("E.errored", r["errored"], true)
	_check("E.error", r["events"][0]["report"]["error"], &"state_missing")
	_check("E.has_calls", provider.has_calls, 1)
	_check("E.read_calls", provider.read_calls, 0)
	_check("E.actual_type", r["events"][0]["report"]["actual_type"], TYPE_NIL)


func _test_key_string_stringname_compat() -> void:
	print("[F] key String/StringName 호환(동일 read)")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	var r_sn := _eval_read({"key": &"quest.main.stage", "value_type": TYPE_INT}, provider)
	_check("F.stringname_value", r_sn["value"], 5)
	_check("F.stringname_key", r_sn["events"][0]["report"]["key"], &"quest.main.stage")
	# String key도 StringName으로 정규화돼 같은 값을 읽는다.
	var r_str := _eval_read({"key": "quest.main.stage", "value_type": TYPE_INT}, provider)
	_check("F.string_value", r_str["value"], 5)
	_check("F.string_key_typeof", typeof(r_str["events"][0]["report"]["key"]), TYPE_STRING_NAME)


func _test_key_invalid_variants() -> void:
	print("[G] 손상 key Variant(int/Dictionary/Array)는 변환 없이 key_invalid")
	var provider := FakeReadProvider.new({&"quest.main.stage": 5})
	for raw in [123, {"k": "v"}, [1, 2], true]:
		var r := _eval_read({"key": raw, "value_type": TYPE_INT}, provider)
		_check("G.errored[%s]" % str(raw), r["errored"], true)
		_check("G.error[%s]" % str(raw), r["events"][0]["report"]["error"], &"key_invalid")
		_check("G.key_sentinel[%s]" % str(raw), r["events"][0]["report"]["key"], &"")
	# 손상 key는 provider를 건드리지 않는다.
	_check("G.no_has_calls", provider.has_calls, 0)
	_check("G.no_read_calls", provider.read_calls, 0)


func _test_actual_type_mismatch_no_coercion() -> void:
	print("[H] type mismatch -> actual_type_mismatch, 암시적 변환 없음")
	# expected INT지만 FLOAT 값(int↔float 변환 없음).
	var p_if := FakeReadProvider.new({&"quest.main.stage": 5.0})
	var r_if := _eval_read({"key": &"quest.main.stage", "value_type": TYPE_INT}, p_if)
	_check("H.int_float_value", r_if["value"], null)
	_check("H.int_float_errored", r_if["errored"], true)
	_check("H.int_float_error", r_if["events"][0]["report"]["error"], &"actual_type_mismatch")

	# expected STRING지만 STRING_NAME 값(String↔StringName 변환 없음).
	var p_ss := FakeReadProvider.new({&"player.display_name": &"Hero"})
	var r_ss := _eval_read({"key": &"player.display_name", "value_type": TYPE_STRING}, p_ss)
	_check("H.string_strname_errored", r_ss["errored"], true)
	_check("H.string_strname_error", r_ss["events"][0]["report"]["error"], &"actual_type_mismatch")

	# expected BOOL지만 INT 값.
	var p_bi := FakeReadProvider.new({&"session.intro.seen": 1})
	var r_bi := _eval_read({"key": &"session.intro.seen", "value_type": TYPE_BOOL}, p_bi)
	_check("H.bool_int_errored", r_bi["errored"], true)
	_check("H.bool_int_error", r_bi["events"][0]["report"]["error"], &"actual_type_mismatch")


func _test_report_sentinels() -> void:
	print("[I] report sentinel(값 미읽기 vs type mismatch)")
	# 값 미읽기 실패(state_missing): actual_type=TYPE_NIL, value=null.
	var p_missing := FakeReadProvider.new({})
	var r_missing := _eval_read({"key": &"quest.main.stage", "value_type": TYPE_INT}, p_missing)
	_check("I.missing_actual_type", r_missing["events"][0]["report"]["actual_type"], TYPE_NIL)
	_check("I.missing_value", r_missing["events"][0]["report"]["value"], null)
	_check("I.missing_expected_type", r_missing["events"][0]["report"]["expected_type"], TYPE_INT)

	# type mismatch: 실제 actual_type=typeof(read_value), value=read_value 보존.
	var p_mm := FakeReadProvider.new({&"quest.main.stage": "five"})
	var r_mm := _eval_read({"key": &"quest.main.stage", "value_type": TYPE_INT}, p_mm)
	_check("I.mismatch_actual_type", r_mm["events"][0]["report"]["actual_type"], TYPE_STRING)
	_check("I.mismatch_value", r_mm["events"][0]["report"]["value"], "five")
	# 단, 반환 Data value는 errored이므로 null이다.
	_check("I.mismatch_return_value", r_mm["value"], null)


func _test_error_dominance_branch() -> void:
	print("[J] Branch가 errored state_read를 false로 fail-closed")
	# state_read가 state_missing(errored)이면 Branch는 false 분기(FALSE)로 가야 한다.
	var provider := FakeReadProvider.new({})   # missing
	var data := {2: _n(&"state_read", {"key": &"session.intro.seen", "value_type": TYPE_BOOL})}
	var r := _run_branch_flow(data, [], 2, provider)
	_check("J.routed_false", r["say"], "FALSE")
	# consumer는 Branch 노드 id(1)다.
	_check("J.consumer_branch", r["events"][0]["consumer"], 1)

	# 대조: 같은 key가 true면 TRUE 분기(테스트가 우연히 통과한 게 아님).
	var p_ok := FakeReadProvider.new({&"session.intro.seen": true})
	var r_ok := _run_branch_flow(data, [], 2, p_ok)
	_check("J.routed_true", r_ok["say"], "TRUE")


func _test_error_dominance_expression() -> void:
	print("[K] Expression이 errored state_read를 true로 못 뒤집음(error-dominance)")
	# state_read(2) -> expression(6, "c or true")가 errored를 삼키려 해도 Branch는 false로 닫혀야 한다.
	var provider := FakeReadProvider.new({})   # state_missing -> errored
	var data := {
		2: _n(&"state_read", {"key": &"session.intro.seen", "value_type": TYPE_BOOL}),
		6: _n(&"expression", {"expression": "c or true", "inputs": ["c"]}),
	}
	var conns := [_c(2, 0, 6, 0)]   # state_read -> expression 입력 0
	var r := _run_branch_flow(data, conns, 6, provider)
	_check("K.or_true_routed_false", r["say"], "FALSE")
	# state_read의 consumer는 직접 소유 노드인 expression(6)이다.
	_check("K.consumer_expression", r["events"][0]["consumer"], 6)

	# `not c` 형태도 errored 전파로 false 유지.
	var data2 := {
		2: _n(&"state_read", {"key": &"session.intro.seen", "value_type": TYPE_BOOL}),
		6: _n(&"expression", {"expression": "not c", "inputs": ["c"]}),
	}
	var r2 := _run_branch_flow(data2, [_c(2, 0, 6, 0)], 6, provider)
	_check("K.not_c_routed_false", r2["say"], "FALSE")


func _test_error_dominance_choice() -> void:
	print("[L] Choice 항목 조건이 errored state_read면 항목 숨김(fail-closed)")
	# choice(1) 항목 0의 Data 입력(port 1) = errored state_read -> 숨김. 항목 1은 무조건 표시.
	var nodes := {
		0: _n(&"start"),
		1: _n(&"choice", {"choices": ["A", "B"]}),
		2: _n(&"state_read", {"key": &"session.intro.seen", "value_type": TYPE_BOOL}),
		3: _n(&"say", {"text": "PICK-A"}),
		4: _n(&"say", {"text": "PICK-B"}),
		5: _n(&"end"),
	}
	var conns := [
		_c(0, 0, 1, 0),       # start -> choice
		_c(2, 0, 1, 1),       # state_read -> choice 항목0 조건 입력(port 1)
		_c(1, 0, 3, 0),       # choice 항목0 flow -> say A
		_c(1, 1, 4, 0),       # choice 항목1 flow -> say B
		_c(3, 0, 5, 0),
		_c(4, 0, 5, 0),
	]
	var res := _resource(nodes, conns)
	var player := DialoguePlayer.new()
	player.dialogue_resource = res
	player.set_read_state_provider(FakeReadProvider.new({}))   # state_missing -> errored -> 항목0 숨김
	var offered := {"choices": null}
	var events: Array = []
	player.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "offer_choice":
			offered["choices"] = req.get("choices"))
	player.state_read_evaluated.connect(func(rid: int, consumer: int, report: Dictionary):
		events.append({"rid": rid, "consumer": consumer, "report": report}))
	player.start_dialogue(res)
	# 항목0(errored)은 숨겨지고 항목1("B")만 표시돼야 한다.
	_check("L.visible_choices", offered["choices"], ["B"])
	# state_read consumer는 Choice 노드 id(1)다.
	_check("L.consumer_choice", events[0]["consumer"], 1)
	player.free()


func _test_value_supplier_into_expression() -> void:
	print("[M] state_read(INT) 값이 Expression 비교에 공급됨")
	# state_read(2)=7 -> expression(6, "x > 5") -> Branch true(TRUE).
	var provider := FakeReadProvider.new({&"quest.main.stage": 7})
	var data := {
		2: _n(&"state_read", {"key": &"quest.main.stage", "value_type": TYPE_INT}),
		6: _n(&"expression", {"expression": "x > 5", "inputs": ["x"]}),
	}
	var r := _run_branch_flow(data, [_c(2, 0, 6, 0)], 6, provider)
	_check("M.gt5_routed_true", r["say"], "TRUE")

	# 값이 5면 x > 5 거짓 -> FALSE.
	var p_low := FakeReadProvider.new({&"quest.main.stage": 5})
	var r_low := _run_branch_flow(data, [_c(2, 0, 6, 0)], 6, p_low)
	_check("M.eq5_routed_false", r_low["say"], "FALSE")


func _test_signal_once_consumer_detached() -> void:
	print("[N] signal 1회, consumer 보존, detached report")
	var provider := FakeReadProvider.new({&"quest.main.stage": 9})

	# 평가당 signal 정확히 1회 + consumer id 보존.
	var r := _eval_read({"key": &"quest.main.stage", "value_type": TYPE_INT}, provider, 42)
	_check("N.events_size", r["events"].size(), 1)
	_check("N.rid", r["events"][0]["rid"], 7)
	_check("N.consumer", r["events"][0]["consumer"], 42)

	# detached report: 받은 report를 마구 변조해도 재평가는 불변이어야 한다.
	var report: Dictionary = r["events"][0]["report"]
	report["ok"] = false
	report["value"] = 999
	report["error"] = &"tampered"
	var second := _eval_read({"key": &"quest.main.stage", "value_type": TYPE_INT}, provider, 42)
	_check("N.second_value", second["value"], 9)
	_check("N.second_ok", second["events"][0]["report"]["ok"], true)
	_check("N.second_error", second["events"][0]["report"]["error"], &"")

	# 동기 listener가 발행된 report를 변조해도 반환 Data value는 영향받지 않는다.
	var res := _resource({7: _n(&"state_read", {"key": &"quest.main.stage", "value_type": TYPE_INT})}, [])
	var player := DialoguePlayer.new()
	player.dialogue_resource = res
	player.set_read_state_provider(provider)
	player.state_read_evaluated.connect(func(_rid: int, _consumer: int, rep: Dictionary):
		rep["value"] = -1
		rep["ok"] = false)
	var value = player._eval_data(7, -1)
	player.free()
	_check("N.listener_value_intact", value["value"], 9)
	_check("N.listener_errored_intact", value["errored"], false)
