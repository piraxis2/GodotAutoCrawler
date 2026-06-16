# DT-009 Step 2 검증용 헤드리스 테스트(Runtime Mutation Provider and Effects).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt009_step2_runtime_mutation_test.tscn
#
# 수작업 runtime snapshot으로 state_set/state_add Effect의 런타임 디스패치를 검증한다(에디터 UI는 Step 3).
# 실제 WorldStateStore를 mutation provider로 주입하고, DialoguePlayer 직접 실행(동기)과
# DialogueManager 경로(lifecycle)를 모두 다룬다.
#
# 검증 범위(ADR-010 D1~D10):
# - Set 5타입 성공/same-value, Add 양수/음수/0, 연속 Add 순서, mutation은 Flow 이동 전 완료.
# - provider 누락(provider_missing)/계약 위반(provider_contract_invalid)/non-Object, read↔mutation 권한 분리.
# - Store 오류(read_only/type_mismatch/out_of_domain/unknown_key) fail-closed + Flow 계속.
# - 앞 Effect 성공 + 뒤 Effect 실패 독립 transaction(rollback 없음).
# - report seam: 실행당 1회, listener 변조/재진입 안전, Portrait와 혼합 저장 순서 + garbage Portrait 없음.
# - Lifecycle: 반복 실행, same-frame latest-wins, 폐기 provider mutation 0회.
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const OP := StateCondition.Operator

var _failures: int = 0
var _stores: Array = []


func _ready() -> void:
	_install_watchdog(30.0)
	# --- 동기(DialoguePlayer 직접) 시나리오 ---
	_test_set_all_types()
	_test_set_same_value_no_signal()
	_test_add_pos_neg()
	_test_add_zero_no_signal()
	_test_consecutive_add_then_branch_reads_new_value()
	_test_provider_missing()
	_test_provider_contract_invalid()
	_test_provider_non_object()
	_test_read_not_promoted_to_mutation()
	_test_store_errors_fail_closed()
	_test_prior_success_later_failure_independent()
	_test_report_once_and_tamper_safe()
	_test_portrait_and_state_mixed_order()
	_test_repeat_execution_consistency()
	_test_invalid_provider_variants()
	_test_listener_cannot_swap_provider()
	_test_store_busy_and_not_ready()
	_test_malformed_key_no_crash()
	_test_provider_arg_type_checked()
	_test_provider_report_schema_validated()
	# --- 비동기(DialogueManager lifecycle) 시나리오 ---
	await _test_manager_latest_wins_discarded_no_mutation()

	for s in _stores:
		if is_instance_valid(s):
			s.free()
	if _failures == 0:
		print("[DT-009 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-009 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-009 Step2] WATCHDOG TIMEOUT after %.0fs — 미완료 종료(행 가능성)." % seconds)
		get_tree().quit(2))


# --- 헬퍼 -------------------------------------------------------------

func _def(key: StringName, vtype: int, default_value: Variant,
		lifetime: int = LT.SAVE, writable: bool = true) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	d.lifetime = lifetime
	d.writable = writable
	return d


func _make_store() -> WorldStateStore:
	var s := StateSchema.new()
	s.schema_version = 1
	var typed: Array[StateDefinition] = []
	for d in [
		_def(&"player.gold", VT.INT, 100),
		_def(&"player.hp", VT.FLOAT, 10.0),
		_def(&"player.name", VT.STRING, "hero"),
		_def(&"actor.mood", VT.STRING_NAME, &"calm"),
		_def(&"world.flag", VT.BOOL, false),
		_def(&"world.locked_int", VT.INT, 7, LT.SAVE, false),  # read-only
	]:
		typed.append(d)
	s.definitions = typed
	var store := WorldStateStore.new()
	store.schema = s
	store.initialize()
	_stores.append(store)
	return store


func _n(type: StringName, params: Dictionary = {}) -> Dictionary:
	return {"type": type, "params": params}


func _c(from_id: int, to_id: int, kind: String = "", from_port: int = 0, to_port: int = 0) -> Dictionary:
	var d := {"from_node_id": from_id, "from_port": from_port, "to_node_id": to_id, "to_port": to_port}
	if kind != "":
		d["kind"] = kind
	return d


func _make_resource(nodes: Dictionary, conns: Array) -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = nodes
	var typed: Array[Dictionary] = []
	for c in conns:
		typed.append(c)
	res.runtime_connections = typed
	res.start_node_id = 0
	return res


# DialoguePlayer를 동기 실행하고 발행된 mutation report + ui_request 로그를 수집한다.
func _run_player(nodes: Dictionary, conns: Array, mutation_provider, read_provider = null) -> Dictionary:
	var res := _make_resource(nodes, conns)
	var player := DialoguePlayer.new()
	var reports: Array = []
	var ui_log: Array = []
	player.state_mutation_evaluated.connect(func(eid: int, rep: Dictionary): reports.append({"eid": eid, "report": rep}))
	player.ui_request.connect(func(req: Dictionary): ui_log.append(req))
	if read_provider != null:
		player.set_read_state_provider(read_provider)
	player.set_mutation_state_provider(mutation_provider)
	player.start_dialogue(res)
	var out := {"reports": reports, "ui_log": ui_log, "waiting": player.waiting_for}
	player.free()
	return out


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


func _ui_kinds(ui_log: Array) -> Array:
	var out: Array = []
	for req in ui_log:
		match req.get("type"):
			"portrait_state": out.append("portrait:%s" % req.get("slot"))
			"display_text": out.append("say")
			"offer_choice": out.append("choice")
	return out


# 소비 계약 중 하나만 구현해 provider_contract_invalid를 유도하는 dummy.
class _PartialProvider extends RefCounted:
	func apply_state_batch(_changes: Array) -> Dictionary:
		return {"applied": true, "diff": [], "errors": []}
	# add_state 없음 → 계약 위반


# 잘못된 arity(apply_state_batch가 0 인자) — 호출 시 SCRIPT ERROR를 내므로 사전 거부돼야 한다.
class _WrongArityProvider extends RefCounted:
	func apply_state_batch() -> Dictionary:
		return {"applied": true, "diff": [], "errors": []}
	func add_state(_k, _d) -> Dictionary:
		return {}


# 계약은 맞지만 non-Dictionary를 반환 — 반환 형태 검증으로 거부돼야 한다(set/add 각각).
class _WrongReturnProvider extends RefCounted:
	func apply_state_batch(_c) -> int:
		return 5
	func add_state(_k, _d) -> String:
		return "nope"


# arity는 맞지만 인자 타입이 틀린 provider — 호출하면 인자 타입 오류(SCRIPT ERROR). 사전 거부돼야 한다.
class _WrongArgTypeProvider extends RefCounted:
	func apply_state_batch(_changes: int) -> Dictionary:
		return {"applied": true, "diff": [], "errors": []}
	func add_state(_key: int, _delta) -> Dictionary:
		return {}


# changes가 Array[int]인 provider — 최상위는 Array지만 원소 타입이 달라 Array[Dictionary] 호출 시
# SCRIPT ERROR가 난다. 원소 타입까지 검사해 사전 거부돼야 한다.
class _ArrayIntProvider extends RefCounted:
	func apply_state_batch(_changes: Array[int]) -> Dictionary:
		return {"applied": true, "diff": [], "errors": []}
	func add_state(_k, _d) -> Dictionary:
		return {"applied": true, "old_value": 0, "new_value": 1}


# 반환 Dictionary의 내부 스키마가 손상된 provider. mode로 손상 유형을 고른다.
class _BadSchemaProvider extends RefCounted:
	var mode: String = ""
	func apply_state_batch(_c) -> Dictionary:
		match mode:
			"diff_not_array": return {"applied": true, "diff": "bad", "errors": []}
			"applied_int": return {"applied": 1, "diff": [], "errors": []}
			"errors_bad": return {"applied": false, "diff": [], "errors": [1]}
			"diff_missing_keys": return {"applied": true, "diff": [{"key": &"x"}], "errors": []}
		return {"applied": true, "diff": [], "errors": []}
	func add_state(_k, _d) -> Dictionary:
		match mode:
			"add_string_error": return {"applied": false, "error": "read_only"}  # String → D10 위반
			"add_applied_int": return {"applied": 7}
		return {"applied": true, "old_value": 0, "new_value": 1}


# --- 시나리오 ---------------------------------------------------------

func _test_set_all_types() -> void:
	print("[A] state_set 5타입 성공 + report")
	var store := _make_store()
	var nodes := {
		0: _n(&"start"),
		1: _n(&"state_set", {"key": &"player.gold", "value": 200}),
		2: _n(&"state_set", {"key": &"player.hp", "value": 3.5}),
		3: _n(&"state_set", {"key": &"player.name", "value": "zed"}),
		4: _n(&"state_set", {"key": &"actor.mood", "value": &"angry"}),
		5: _n(&"state_set", {"key": &"world.flag", "value": true}),
		9: _n(&"say", {"text": "done"}),
	}
	var conns := [
		_c(0, 1, "effect"), _c(0, 2, "effect"), _c(0, 3, "effect"),
		_c(0, 4, "effect"), _c(0, 5, "effect"),
		_c(0, 9),
	]
	var r := _run_player(nodes, conns, store)
	_check("A.gold", store.get_value(&"player.gold"), 200)
	_check("A.hp", store.get_value(&"player.hp"), 3.5)
	_check("A.name", store.get_value(&"player.name"), "zed")
	_check("A.mood", store.get_value(&"actor.mood"), &"angry")
	_check("A.flag", store.get_value(&"world.flag"), true)
	_check("A.report_count", r["reports"].size(), 5)
	var first: Dictionary = r["reports"][0]["report"]
	_check("A.r0_applied", first["applied"], true)
	_check("A.r0_op", first["operation"], "set")
	_check("A.r0_key", first["key"], &"player.gold")
	_check("A.r0_old", first["old_value"], 100)
	_check("A.r0_new", first["new_value"], 200)
	_check("A.r0_error", first["error"], &"")
	_check("A.waiting_say", r["waiting"], &"text")


func _test_set_same_value_no_signal() -> void:
	print("[B] state_set same-value → applied, old==new, value_changed 무발행")
	var store := _make_store()
	var vc := {"count": 0}
	store.value_changed.connect(func(_k, _o, _n): vc["count"] += 1)
	var nodes := {0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": 100}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	var r := _run_player(nodes, conns, store)
	var rep: Dictionary = r["reports"][0]["report"]
	_check("B.applied", rep["applied"], true)
	_check("B.old", rep["old_value"], 100)
	_check("B.new", rep["new_value"], 100)
	_check("B.error", rep["error"], &"")
	_check("B.no_value_changed", vc["count"], 0)


func _test_add_pos_neg() -> void:
	print("[C] state_add 양수/음수 + authoritative old/new")
	var store := _make_store()
	var nodes := {
		0: _n(&"start"),
		1: _n(&"state_add", {"key": &"player.gold", "delta": 25}),
		2: _n(&"state_add", {"key": &"player.hp", "delta": -4.0}),
		9: _n(&"say"),
	}
	var conns := [_c(0, 1, "effect"), _c(0, 2, "effect"), _c(0, 9)]
	var r := _run_player(nodes, conns, store)
	_check("C.gold", store.get_value(&"player.gold"), 125)
	_check("C.hp", store.get_value(&"player.hp"), 6.0)
	_check("C.r0_op", r["reports"][0]["report"]["operation"], "add")
	_check("C.r0_old", r["reports"][0]["report"]["old_value"], 100)
	_check("C.r0_new", r["reports"][0]["report"]["new_value"], 125)
	_check("C.r1_new", r["reports"][1]["report"]["new_value"], 6.0)


func _test_add_zero_no_signal() -> void:
	print("[D] state_add 0 → applied, old==new, value_changed 무발행")
	var store := _make_store()
	var vc := {"count": 0}
	store.value_changed.connect(func(_k, _o, _n): vc["count"] += 1)
	var nodes := {0: _n(&"start"), 1: _n(&"state_add", {"key": &"player.gold", "delta": 0}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	var r := _run_player(nodes, conns, store)
	var rep: Dictionary = r["reports"][0]["report"]
	_check("D.applied", rep["applied"], true)
	_check("D.old", rep["old_value"], 100)
	_check("D.new", rep["new_value"], 100)
	_check("D.no_value_changed", vc["count"], 0)


func _test_consecutive_add_then_branch_reads_new_value() -> void:
	print("[E] 연속 Add(저장 순서) 후 Branch가 변경된 값을 읽음(mutation은 Flow 이동 전 완료)")
	var store := _make_store()
	# Start --effect--> add(+10), add(+20)  ; Start --flow--> Branch(gold == 130)
	var cs := ConditionSet.new()
	var sc := StateCondition.new()
	sc.key = &"player.gold"
	sc.operator = OP.EQUAL
	sc.expected_value = 130
	cs.root = sc
	var nodes := {
		0: _n(&"start"),
		1: _n(&"state_add", {"key": &"player.gold", "delta": 10}),
		2: _n(&"state_add", {"key": &"player.gold", "delta": 20}),
		3: _n(&"branch"),
		4: _n(&"state_condition", {"condition_set": cs}),
		5: _n(&"say", {"text": "TRUE"}),
		6: _n(&"say", {"text": "FALSE"}),
		7: _n(&"end"),
	}
	var conns := [
		_c(0, 1, "effect"), _c(0, 2, "effect"),
		_c(0, 3, "", 0, 1),     # start flow -> branch flow-in (to_port 1)
		_c(4, 3, "", 0, 0),     # state_condition -> branch 조건 입력 (to_port 0)
		_c(3, 5, "", 0, 0),     # branch true
		_c(3, 6, "", 1, 0),     # branch false
		_c(5, 7), _c(6, 7),
	]
	# read provider도 같은 store(조건 평가용). mutation도 같은 store.
	var r := _run_player(nodes, conns, store, store)
	_check("E.gold_final", store.get_value(&"player.gold"), 130)
	_check("E.add_order_r0", r["reports"][0]["report"]["new_value"], 110)
	_check("E.add_order_r1_old", r["reports"][1]["report"]["old_value"], 110)
	_check("E.add_order_r1_new", r["reports"][1]["report"]["new_value"], 130)
	# Branch가 TRUE로 갔다 = mutation이 Flow 이동 전에 완료되어 새 값을 읽음.
	_check("E.branch_true_say", _ui_kinds(r["ui_log"]), ["say"])
	# say 텍스트 직접 확인
	var said := ""
	for req in r["ui_log"]:
		if req.get("type") == "display_text":
			said = req.get("say")
	_check("E.said_TRUE", said, "TRUE")


func _test_provider_missing() -> void:
	print("[F] mutation provider 누락 → provider_missing, Flow 계속")
	var nodes := {0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": 5}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	var r := _run_player(nodes, conns, null)
	_check("F.applied", r["reports"][0]["report"]["applied"], false)
	_check("F.error", r["reports"][0]["report"]["error"], &"provider_missing")
	_check("F.flow_continued", r["waiting"], &"text")


func _test_provider_contract_invalid() -> void:
	print("[G] provider 계약 위반(add_state 누락) → provider_contract_invalid")
	var bad := _PartialProvider.new()
	var nodes := {0: _n(&"start"), 1: _n(&"state_add", {"key": &"player.gold", "delta": 1}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	var r := _run_player(nodes, conns, bad)
	_check("G.error", r["reports"][0]["report"]["error"], &"provider_contract_invalid")
	_check("G.flow_continued", r["waiting"], &"text")


func _test_provider_non_object() -> void:
	print("[H] non-Object provider → provider_contract_invalid")
	var nodes := {0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": 5}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	var r := _run_player(nodes, conns, 12345)  # int은 Object가 아님
	_check("H.error", r["reports"][0]["report"]["error"], &"provider_contract_invalid")


func _test_read_not_promoted_to_mutation() -> void:
	print("[I] read provider만 주입 → mutation 자동 승격 없음(provider_missing, Store 불변)")
	var store := _make_store()
	var nodes := {0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": 999}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	# read provider = store, mutation provider = null.
	var r := _run_player(nodes, conns, null, store)
	_check("I.error", r["reports"][0]["report"]["error"], &"provider_missing")
	_check("I.store_unchanged", store.get_value(&"player.gold"), 100)


func _test_store_errors_fail_closed() -> void:
	print("[J] Store 오류 fail-closed(값 불변) + Flow 계속")
	# read-only (set)
	var store := _make_store()
	var r1 := _run_player({0: _n(&"start"), 1: _n(&"state_set", {"key": &"world.locked_int", "value": 9}), 9: _n(&"say")},
		[_c(0, 1, "effect"), _c(0, 9)], store)
	_check("J.readonly_error", r1["reports"][0]["report"]["error"], &"read_only")
	_check("J.readonly_unchanged", store.get_value(&"world.locked_int"), 7)
	_check("J.readonly_flow", r1["waiting"], &"text")
	# type_mismatch (set)
	var store2 := _make_store()
	var r2 := _run_player({0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": "x"}), 9: _n(&"say")},
		[_c(0, 1, "effect"), _c(0, 9)], store2)
	_check("J.type_error", r2["reports"][0]["report"]["error"], &"type_mismatch")
	_check("J.type_unchanged", store2.get_value(&"player.gold"), 100)
	# unknown_key (set)
	var store3 := _make_store()
	var r3 := _run_player({0: _n(&"start"), 1: _n(&"state_set", {"key": &"nope.nope", "value": 1}), 9: _n(&"say")},
		[_c(0, 1, "effect"), _c(0, 9)], store3)
	_check("J.unknown_error", r3["reports"][0]["report"]["error"], &"unknown_key")
	# out_of_domain (add)
	var store4 := _make_store()
	store4.set_value(&"player.gold", 9007199254740991)  # INT max
	var r4 := _run_player({0: _n(&"start"), 1: _n(&"state_add", {"key": &"player.gold", "delta": 1}), 9: _n(&"say")},
		[_c(0, 1, "effect"), _c(0, 9)], store4)
	_check("J.domain_error", r4["reports"][0]["report"]["error"], &"out_of_domain")
	_check("J.domain_unchanged", store4.get_value(&"player.gold"), 9007199254740991)


func _test_prior_success_later_failure_independent() -> void:
	print("[K] 앞 Effect 성공 + 뒤 Effect 실패 → 독립 transaction(앞 변경 유지)")
	var store := _make_store()
	var nodes := {
		0: _n(&"start"),
		1: _n(&"state_set", {"key": &"player.gold", "value": 200}),     # 성공
		2: _n(&"state_set", {"key": &"world.locked_int", "value": 9}),  # read_only 실패
		9: _n(&"say"),
	}
	var conns := [_c(0, 1, "effect"), _c(0, 2, "effect"), _c(0, 9)]
	var r := _run_player(nodes, conns, store)
	_check("K.first_committed", store.get_value(&"player.gold"), 200)  # rollback 없음
	_check("K.r0_applied", r["reports"][0]["report"]["applied"], true)
	_check("K.r1_failed", r["reports"][1]["report"]["applied"], false)
	_check("K.r1_error", r["reports"][1]["report"]["error"], &"read_only")


func _test_report_once_and_tamper_safe() -> void:
	print("[L] report 실행당 1회 + listener 변조가 Store/결과 불변")
	var store := _make_store()
	var res := _make_resource(
		{0: _n(&"start"), 1: _n(&"state_add", {"key": &"player.gold", "delta": 50}), 9: _n(&"say")},
		[_c(0, 1, "effect"), _c(0, 9)])
	var player := DialoguePlayer.new()
	var count := {"n": 0}
	# listener가 report를 적극적으로 변조한다.
	player.state_mutation_evaluated.connect(func(_eid: int, rep: Dictionary):
		count["n"] += 1
		rep["new_value"] = 99999
		rep["applied"] = false
		rep["error"] = &"hacked")
	player.set_mutation_state_provider(store)
	player.start_dialogue(res)
	_check("L.emit_once", count["n"], 1)
	_check("L.store_correct", store.get_value(&"player.gold"), 150)  # 변조 무시, 실제 commit 유지
	# 다음 mutation도 변조와 무관하게 직전 commit(150) 기준.
	var rep2: Dictionary = store.add_state(&"player.gold", 10)
	_check("L.next_old", rep2["old_value"], 150)
	player.free()


func _test_portrait_and_state_mixed_order() -> void:
	print("[M] Portrait + state Effect 혼합 저장 순서 + garbage Portrait 없음")
	var store := _make_store()
	var nodes := {
		0: _n(&"start"),
		1: _n(&"portrait_show", {"slot": "left", "texture_path": "res://a.png"}),
		2: _n(&"state_set", {"key": &"player.gold", "value": 300}),
		3: _n(&"portrait_show", {"slot": "right", "texture_path": "res://b.png"}),
		9: _n(&"say"),
	}
	var conns := [_c(0, 1, "effect"), _c(0, 2, "effect"), _c(0, 3, "effect"), _c(0, 9)]
	var r := _run_player(nodes, conns, store)
	# state_set는 ui_request를 발행하지 않는다 → Portrait는 정확히 left, right + say (garbage 없음).
	_check("M.ui_kinds", _ui_kinds(r["ui_log"]), ["portrait:left", "portrait:right", "say"])
	_check("M.store_mutated", store.get_value(&"player.gold"), 300)
	_check("M.one_state_report", r["reports"].size(), 1)


func _test_repeat_execution_consistency() -> void:
	print("[N] 같은 리소스 반복 실행 일관성(각 실행이 자기 store에 1회 적용)")
	var nodes := {0: _n(&"start"), 1: _n(&"state_add", {"key": &"player.gold", "delta": 5}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	var store_a := _make_store()
	var store_b := _make_store()
	var ra := _run_player(nodes, conns, store_a)
	var rb := _run_player(nodes, conns, store_b)
	_check("N.a_gold", store_a.get_value(&"player.gold"), 105)
	_check("N.b_gold", store_b.get_value(&"player.gold"), 105)
	_check("N.a_one_report", ra["reports"].size(), 1)
	_check("N.b_one_report", rb["reports"].size(), 1)


func _test_invalid_provider_variants() -> void:
	print("[P] 잘못된 provider 변형 → provider_contract_invalid, SCRIPT ERROR 없음, Flow 계속")
	var set_nodes := {0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": 5}), 9: _n(&"say")}
	var add_nodes := {0: _n(&"start"), 1: _n(&"state_add", {"key": &"player.gold", "delta": 1}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	# 잘못된 arity(호출 전 reflection으로 거부)
	var r_arity := _run_player(set_nodes, conns, _WrongArityProvider.new())
	_check("P.arity_error", r_arity["reports"][0]["report"]["error"], &"provider_contract_invalid")
	_check("P.arity_flow", r_arity["waiting"], &"text")
	# 잘못된 반환 타입(set: int 반환)
	var r_ret_set := _run_player(set_nodes, conns, _WrongReturnProvider.new())
	_check("P.set_return_error", r_ret_set["reports"][0]["report"]["error"], &"provider_contract_invalid")
	# 잘못된 반환 타입(add: String 반환)
	var r_ret_add := _run_player(add_nodes, conns, _WrongReturnProvider.new())
	_check("P.add_return_error", r_ret_add["reports"][0]["report"]["error"], &"provider_contract_invalid")
	# freed Object
	var dead := WorldStateStore.new()
	dead.schema = null
	dead.initialize()
	dead.free()
	var r_freed := _run_player(set_nodes, conns, dead)
	_check("P.freed_error", r_freed["reports"][0]["report"]["error"], &"provider_contract_invalid")


func _test_listener_cannot_swap_provider() -> void:
	print("[Q] report listener의 provider 교체가 같은 chain 뒤 Effect에 영향 없음(D10 재진입)")
	var store_a := _make_store()
	var store_b := _make_store()
	var res := _make_resource({
		0: _n(&"start"),
		1: _n(&"state_set", {"key": &"player.gold", "value": 200}),
		2: _n(&"state_set", {"key": &"player.gold", "value": 300}),
		9: _n(&"say"),
	}, [_c(0, 1, "effect"), _c(0, 2, "effect"), _c(0, 9)])
	var player := DialoguePlayer.new()
	player.set_mutation_state_provider(store_a)
	# 첫 report 직후 provider를 store_b로 바꾸려 시도한다.
	player.state_mutation_evaluated.connect(func(_e: int, _r: Dictionary):
		player.set_mutation_state_provider(store_b))
	player.start_dialogue(res)
	# chain 시작 시 provider가 고정되므로 두 Effect 모두 store_a에 적용된다.
	_check("Q.store_a_final", store_a.get_value(&"player.gold"), 300)
	_check("Q.store_b_untouched", store_b.get_value(&"player.gold"), 100)
	player.free()


func _test_store_busy_and_not_ready() -> void:
	print("[R] store_not_ready / store_busy report 경로")
	# not-ready store provider
	var bad := WorldStateStore.new()
	bad.schema = null
	bad.initialize()   # not ready
	var r1 := _run_player({0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": 5}), 9: _n(&"say")},
		[_c(0, 1, "effect"), _c(0, 9)], bad)
	_check("R.not_ready", r1["reports"][0]["report"]["error"], &"store_not_ready")
	bad.free()

	# store_busy: store가 value_changed 알림 중(_in_notification)일 때 dialogue effect가 같은 store에 mutation 시도.
	var store := _make_store()
	var busy := {"error": &"", "n": 0}
	var cb := func(_k, _o, _n):
		if busy["n"] > 0:
			return
		busy["n"] += 1
		var r2 := _run_player({0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.hp", "value": 9.0}), 9: _n(&"say")},
			[_c(0, 1, "effect"), _c(0, 9)], store)
		busy["error"] = r2["reports"][0]["report"]["error"]
	store.value_changed.connect(cb)
	store.set_value(&"player.gold", 1)   # value_changed 발생 → cb 안에서 알림 중 effect mutation
	_check("R.busy", busy["error"], &"store_busy")
	_check("R.busy_unchanged", store.get_value(&"player.hp"), 10.0)


func _test_malformed_key_no_crash() -> void:
	print("[S] 손상된 key(non-String) → 런타임 오류 없이 fail-closed")
	var store := _make_store()
	var r := _run_player({0: _n(&"start"), 1: _n(&"state_set", {"key": 12345, "value": 5}), 9: _n(&"say")},
		[_c(0, 1, "effect"), _c(0, 9)], store)
	_check("S.applied", r["reports"][0]["report"]["applied"], false)
	_check("S.error", r["reports"][0]["report"]["error"], &"unknown_key")
	_check("S.flow", r["waiting"], &"text")


func _test_provider_arg_type_checked() -> void:
	print("[T] provider 인자 타입 불일치 → provider_contract_invalid(호출 전 reflection)")
	var set_nodes := {0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": 5}), 9: _n(&"say")}
	var add_nodes := {0: _n(&"start"), 1: _n(&"state_add", {"key": &"player.gold", "delta": 1}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	var rs := _run_player(set_nodes, conns, _WrongArgTypeProvider.new())
	_check("T.set", rs["reports"][0]["report"]["error"], &"provider_contract_invalid")
	var ra := _run_player(add_nodes, conns, _WrongArgTypeProvider.new())
	_check("T.add", ra["reports"][0]["report"]["error"], &"provider_contract_invalid")
	# typed array 원소 타입 불일치(Array[int]) → SCRIPT ERROR 없이 사전 거부, Flow 계속.
	var rai := _run_player(set_nodes, conns, _ArrayIntProvider.new())
	_check("T.array_int", rai["reports"][0]["report"]["error"], &"provider_contract_invalid")
	_check("T.array_int_flow", rai["waiting"], &"text")


func _test_provider_report_schema_validated() -> void:
	print("[U] 손상된 반환 report 스키마 → provider_contract_invalid(크래시/거짓 성공 없음)")
	var set_nodes := {0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": 5}), 9: _n(&"say")}
	var add_nodes := {0: _n(&"start"), 1: _n(&"state_add", {"key": &"player.gold", "delta": 1}), 9: _n(&"say")}
	var conns := [_c(0, 1, "effect"), _c(0, 9)]
	for mode in ["diff_not_array", "applied_int", "errors_bad", "diff_missing_keys"]:
		var p := _BadSchemaProvider.new()
		p.mode = mode
		var r := _run_player(set_nodes, conns, p)
		_check("U.set_%s" % mode, r["reports"][0]["report"]["error"], &"provider_contract_invalid")
		_check("U.set_%s_not_applied" % mode, r["reports"][0]["report"]["applied"], false)
	for mode in ["add_string_error", "add_applied_int"]:
		var p := _BadSchemaProvider.new()
		p.mode = mode
		var r := _run_player(add_nodes, conns, p)
		_check("U.add_%s" % mode, r["reports"][0]["report"]["error"], &"provider_contract_invalid")
		_check("U.add_%s_not_applied" % mode, r["reports"][0]["report"]["applied"], false)


func _test_manager_latest_wins_discarded_no_mutation() -> void:
	print("[O] Manager same-frame latest-wins → 폐기 provider mutation 0회")
	var nodes := {0: _n(&"start"), 1: _n(&"state_set", {"key": &"player.gold", "value": 200}), 9: _n(&"say"), 8: _n(&"end")}
	var conns := [_c(0, 1, "effect"), _c(0, 9), _c(9, 8)]

	var store_discarded := _make_store()
	var store_active := _make_store()
	var vc_discarded := {"n": 0}
	store_discarded.value_changed.connect(func(_k, _o, _n): vc_discarded["n"] += 1)

	# 첫 play(폐기 대상) 직후 같은 프레임에 둘째 play(활성).
	DialogueManager.play(_make_resource(nodes, conns), store_discarded, store_discarded)
	var player_a: DialoguePlayer = DialogueManager._ui.dialogue_player
	DialogueManager.play(_make_resource(nodes, conns), store_active, store_active)
	var player_b: DialoguePlayer = DialogueManager._ui.dialogue_player

	await get_tree().process_frame
	await get_tree().process_frame

	_check_true("O.distinct_players", player_a != player_b)
	_check("O.discarded_no_mutation", vc_discarded["n"], 0)
	_check("O.discarded_unchanged", store_discarded.get_value(&"player.gold"), 100)
	_check("O.active_mutated", store_active.get_value(&"player.gold"), 200)

	DialogueManager._dismiss()
	await get_tree().process_frame
