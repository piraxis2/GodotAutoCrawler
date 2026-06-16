# DT-009 Step 1 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/dialogtool/world_state/tests/dt009_step1_add_state_test.tscn
#
# 검증 범위 (WorldStateStore.add_state — ADR-010 D3/D4/D8/D10):
# - INT/FLOAT 양수/음수 Add, 같은 타입 strict(int↔float 암시 변환 없음)
# - delta 0 / 결과 무변경 → 성공·changed=false·무 signal
# - 비숫자 state / 미등록 / read-only / not-ready / busy 거부(값·signal 불변)
# - JSON-safe 경계 도달 성공, 경계 초과·산술 overflow·FLOAT INF/NAN·비유한 결과 거부
# - 성공 report old/new/changed가 value_changed와 일치
# - 외부 report 변조가 Store/다음 호출에 영향 없음, 연속 Add가 직전 commit 기준
# - 기존 set_value/apply_batch/snapshot 회귀 유지
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const INT_MAX := 9007199254740991   # 2^53 - 1
const INT_MIN := -9007199254740991

var _failures: int = 0
var _value_log: Array = []


func _ready() -> void:
	_test_int_add_pos_neg()
	_test_float_add_pos_neg()
	_test_zero_delta_no_change()
	_test_int_state_float_delta_rejected()
	_test_float_state_int_delta_rejected()
	_test_non_numeric_rejected()
	_test_unknown_key_rejected()
	_test_readonly_rejected()
	_test_not_ready_rejected()
	_test_reentrancy_busy()
	_test_int_boundary_success()
	_test_int_overflow_rejected()
	_test_float_nonfinite_rejected()
	_test_success_report_matches_signal()
	_test_failure_report_unchanged()
	_test_report_tampering_isolated()
	_test_sequential_add_uses_committed_value()
	_test_out_of_range_delta_cancellation_rejected()
	_test_report_type_contract()
	_test_regression_set_batch_snapshot()

	if _failures == 0:
		print("[DT-009 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-009 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


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


func _schema(defs: Array, version: int = 1) -> StateSchema:
	var s := StateSchema.new()
	s.schema_version = version
	var typed: Array[StateDefinition] = []
	for d in defs:
		typed.append(d)
	s.definitions = typed
	return s


func _make_store() -> WorldStateStore:
	var s := _schema([
		_def(&"player.gold", VT.INT, 100),
		_def(&"player.hp", VT.FLOAT, 10.0),
		_def(&"player.name", VT.STRING, "hero"),
		_def(&"actor.mood", VT.STRING_NAME, &"calm"),
		_def(&"world.flag", VT.BOOL, true),
		_def(&"world.locked_int", VT.INT, 7, LT.SAVE, false),  # read-only INT
	])
	var store := WorldStateStore.new()
	store.schema = s
	store.value_changed.connect(func(k, _o, n): _value_log.append({"key": k, "new": n}))
	store.initialize()
	return store


func _batch(arr: Array) -> Array[Dictionary]:
	var typed: Array[Dictionary] = []
	for d in arr:
		typed.append(d)
	return typed


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# --- 시나리오 ---------------------------------------------------------

func _test_int_add_pos_neg() -> void:
	print("[A] INT 양수/음수 Add")
	var store := _make_store()
	_value_log.clear()
	var r1 := store.add_state(&"player.gold", 25)
	_check("A.pos_applied", r1["applied"], true)
	_check("A.pos_changed", r1["changed"], true)
	_check("A.pos_op", r1["operation"], "add")
	_check("A.pos_old", r1["old_value"], 100)
	_check("A.pos_new", r1["new_value"], 125)
	_check("A.pos_error", r1["error"], &"")
	_check("A.pos_store", store.get_value(&"player.gold"), 125)
	var r2 := store.add_state(&"player.gold", -50)
	_check("A.neg_new", r2["new_value"], 75)
	_check("A.neg_store", store.get_value(&"player.gold"), 75)
	_check("A.signal_count", _value_log.size(), 2)
	store.free()


func _test_float_add_pos_neg() -> void:
	print("[B] FLOAT 양수/음수 Add")
	var store := _make_store()
	_value_log.clear()
	var r1 := store.add_state(&"player.hp", 2.5)
	_check("B.pos_applied", r1["applied"], true)
	_check("B.pos_new", r1["new_value"], 12.5)
	_check("B.pos_store", store.get_value(&"player.hp"), 12.5)
	var r2 := store.add_state(&"player.hp", -4.0)
	_check("B.neg_new", r2["new_value"], 8.5)
	_check("B.signal_count", _value_log.size(), 2)
	store.free()


func _test_zero_delta_no_change() -> void:
	print("[C] delta 0 / 0.0 → 성공·무변경·무 signal")
	var store := _make_store()
	_value_log.clear()
	var ri := store.add_state(&"player.gold", 0)
	_check("C.int_applied", ri["applied"], true)
	_check("C.int_changed", ri["changed"], false)
	_check("C.int_old", ri["old_value"], 100)
	_check("C.int_new", ri["new_value"], 100)
	_check("C.int_error", ri["error"], &"")
	var rf := store.add_state(&"player.hp", 0.0)
	_check("C.float_applied", rf["applied"], true)
	_check("C.float_changed", rf["changed"], false)
	_check("C.no_signal", _value_log.size(), 0)
	_check("C.gold_unchanged", store.get_value(&"player.gold"), 100)
	store.free()


func _test_int_state_float_delta_rejected() -> void:
	print("[D] INT state + FLOAT delta 거부")
	var store := _make_store()
	_value_log.clear()
	var r := store.add_state(&"player.gold", 1.0)
	_check("D.applied", r["applied"], false)
	_check("D.error", r["error"], &"type_mismatch")
	_check("D.unchanged", store.get_value(&"player.gold"), 100)
	_check("D.no_signal", _value_log.size(), 0)
	store.free()


func _test_float_state_int_delta_rejected() -> void:
	print("[E] FLOAT state + INT delta 거부")
	var store := _make_store()
	_value_log.clear()
	var r := store.add_state(&"player.hp", 1)
	_check("E.applied", r["applied"], false)
	_check("E.error", r["error"], &"type_mismatch")
	_check("E.unchanged", store.get_value(&"player.hp"), 10.0)
	_check("E.no_signal", _value_log.size(), 0)
	store.free()


func _test_non_numeric_rejected() -> void:
	print("[F] 비숫자 state 거부(BOOL/String/StringName)")
	var store := _make_store()
	_value_log.clear()
	var rb := store.add_state(&"world.flag", true)
	_check("F.bool_error", rb["error"], &"type_mismatch")
	var rs := store.add_state(&"player.name", "x")
	_check("F.string_error", rs["error"], &"type_mismatch")
	var rn := store.add_state(&"actor.mood", &"x")
	_check("F.sname_error", rn["error"], &"type_mismatch")
	_check("F.no_signal", _value_log.size(), 0)
	store.free()


func _test_unknown_key_rejected() -> void:
	print("[G] 미등록 key 거부")
	var store := _make_store()
	var r := store.add_state(&"nope.nope", 1)
	_check("G.applied", r["applied"], false)
	_check("G.error", r["error"], &"unknown_key")
	_check("G.key", r["key"], &"nope.nope")
	store.free()


func _test_readonly_rejected() -> void:
	print("[H] read-only key 거부")
	var store := _make_store()
	_value_log.clear()
	var r := store.add_state(&"world.locked_int", 1)
	_check("H.applied", r["applied"], false)
	_check("H.error", r["error"], &"read_only")
	_check("H.unchanged", store.get_value(&"world.locked_int"), 7)
	_check("H.no_signal", _value_log.size(), 0)
	store.free()


func _test_not_ready_rejected() -> void:
	print("[I] not-ready store 거부")
	var store := WorldStateStore.new()
	store.schema = null
	store.initialize()
	var r := store.add_state(&"player.gold", 1)
	_check("I.applied", r["applied"], false)
	_check("I.error", r["error"], &"store_not_ready")
	store.free()


func _test_reentrancy_busy() -> void:
	print("[J] 알림 중 add_state 재진입 → busy, staged 값 보존")
	var store := _make_store()
	var observed := {"first": null, "reenter_error": &"", "reenter_applied": true}
	var cb := func(k, _o, _n):
		if k == &"player.gold":
			observed["first"] = store.get_value(&"player.gold")
			var rb: Dictionary = store.add_state(&"player.gold", 1)
			observed["reenter_error"] = rb["error"]
			observed["reenter_applied"] = rb["applied"]
	store.value_changed.connect(cb)
	var r := store.add_state(&"player.gold", 5)
	_check("J.outer_new", r["new_value"], 105)
	_check("J.first_staged", observed["first"], 105)  # 발행 시점에 이미 commit됨
	_check("J.reenter_busy", observed["reenter_error"], &"store_busy")
	_check("J.reenter_rejected", observed["reenter_applied"], false)
	_check("J.final", store.get_value(&"player.gold"), 105)  # 재진입이 값을 못 바꿈
	store.value_changed.disconnect(cb)
	store.free()


func _test_int_boundary_success() -> void:
	print("[K] INT JSON-safe 상·하한 도달 성공")
	var store := _make_store()
	# 상한 도달
	store.set_value(&"player.gold", INT_MAX - 10)
	_value_log.clear()
	var rmax := store.add_state(&"player.gold", 10)
	_check("K.max_applied", rmax["applied"], true)
	_check("K.max_new", rmax["new_value"], INT_MAX)
	_check("K.max_store", store.get_value(&"player.gold"), INT_MAX)
	# 하한 도달
	store.set_value(&"player.gold", INT_MIN + 10)
	var rmin := store.add_state(&"player.gold", -10)
	_check("K.min_applied", rmin["applied"], true)
	_check("K.min_new", rmin["new_value"], INT_MIN)
	store.free()


func _test_int_overflow_rejected() -> void:
	print("[L] 경계 초과 및 산술 overflow 거부(값·signal 불변)")
	var store := _make_store()
	# 경계 +1 초과
	store.set_value(&"player.gold", INT_MAX)
	_value_log.clear()
	var rover := store.add_state(&"player.gold", 1)
	_check("L.over_applied", rover["applied"], false)
	_check("L.over_error", rover["error"], &"out_of_domain")
	_check("L.over_unchanged", store.get_value(&"player.gold"), INT_MAX)
	_check("L.over_no_signal", _value_log.size(), 0)
	# 경계 -1 미만
	store.set_value(&"player.gold", INT_MIN)
	var runder := store.add_state(&"player.gold", -1)
	_check("L.under_error", runder["error"], &"out_of_domain")
	# JSON-safe 범위 밖 거대 delta(int64 max)
	store.set_value(&"player.gold", 100)
	var rwrap := store.add_state(&"player.gold", 9223372036854775807)
	_check("L.wrap_applied", rwrap["applied"], false)
	_check("L.wrap_error", rwrap["error"], &"out_of_domain")
	_check("L.wrap_unchanged", store.get_value(&"player.gold"), 100)
	store.free()


func _test_out_of_range_delta_cancellation_rejected() -> void:
	print("[T] JSON-safe 범위 밖 delta는 결과가 상쇄돼도 거부(값·signal 불변)")
	var store := _make_store()
	# -1 + 2^53 = 2^53-1 (결과는 안전 상한). delta가 범위 밖이므로 거부해야 한다.
	store.set_value(&"player.gold", -1)
	_value_log.clear()
	var rhi := store.add_state(&"player.gold", 9007199254740992)  # 2^53
	_check("T.hi_applied", rhi["applied"], false)
	_check("T.hi_error", rhi["error"], &"out_of_domain")
	_check("T.hi_unchanged", store.get_value(&"player.gold"), -1)
	_check("T.hi_no_signal", _value_log.size(), 0)
	# 1 + -(2^53) = -(2^53-1) (결과는 안전 하한). delta 범위 밖이므로 거부.
	store.set_value(&"player.gold", 1)
	_value_log.clear()
	var rlo := store.add_state(&"player.gold", -9007199254740992)
	_check("T.lo_applied", rlo["applied"], false)
	_check("T.lo_error", rlo["error"], &"out_of_domain")
	_check("T.lo_unchanged", store.get_value(&"player.gold"), 1)
	_check("T.lo_no_signal", _value_log.size(), 0)
	store.free()


func _test_report_type_contract() -> void:
	print("[U] report 필드 타입 계약(typeof 단언, str() false-green 방지)")
	var store := _make_store()
	# 성공 INT report
	var r := store.add_state(&"player.gold", 5)
	_check("U.applied_type", typeof(r["applied"]), TYPE_BOOL)
	_check("U.changed_type", typeof(r["changed"]), TYPE_BOOL)
	_check("U.op_type", typeof(r["operation"]), TYPE_STRING)
	_check("U.op_value", r["operation"], "add")
	_check("U.key_type", typeof(r["key"]), TYPE_STRING_NAME)
	_check("U.error_type", typeof(r["error"]), TYPE_STRING_NAME)
	_check("U.old_int_type", typeof(r["old_value"]), TYPE_INT)
	_check("U.new_int_type", typeof(r["new_value"]), TYPE_INT)
	# 성공 FLOAT report의 old/new 타입
	var rf := store.add_state(&"player.hp", 1.0)
	_check("U.old_float_type", typeof(rf["old_value"]), TYPE_FLOAT)
	_check("U.new_float_type", typeof(rf["new_value"]), TYPE_FLOAT)
	# 실패 report에서도 key/error는 StringName
	var re := store.add_state(&"nope.nope", 1)
	_check("U.fail_key_type", typeof(re["key"]), TYPE_STRING_NAME)
	_check("U.fail_error_type", typeof(re["error"]), TYPE_STRING_NAME)
	store.free()


func _test_float_nonfinite_rejected() -> void:
	print("[M] FLOAT INF/NAN delta와 비유한 결과 거부")
	var store := _make_store()
	_value_log.clear()
	var rinf := store.add_state(&"player.hp", INF)
	_check("M.inf_applied", rinf["applied"], false)
	_check("M.inf_error", rinf["error"], &"out_of_domain")
	var rnan := store.add_state(&"player.hp", NAN)
	_check("M.nan_error", rnan["error"], &"out_of_domain")
	# 유한 피연산자지만 결과가 inf로 overflow. set_value는 정상 변경이라 signal을 내므로,
	# 거부될 add 직전에 로그를 비워 add가 무발행임을 단언한다.
	store.set_value(&"player.hp", 1.0e308)
	_value_log.clear()
	var rover := store.add_state(&"player.hp", 1.0e308)
	_check("M.result_overflow", rover["error"], &"out_of_domain")
	_check("M.hp_unchanged", store.get_value(&"player.hp"), 1.0e308)
	_check("M.no_signal", _value_log.size(), 0)
	store.free()


func _test_success_report_matches_signal() -> void:
	print("[N] 성공 report old/new/changed가 value_changed와 일치")
	var store := _make_store()
	var sig := {"old": null, "new": null, "count": 0}
	var cb := func(_k, o, n):
		sig["old"] = o
		sig["new"] = n
		sig["count"] += 1
	store.value_changed.connect(cb)
	var r := store.add_state(&"player.gold", 30)
	_check("N.changed", r["changed"], true)
	_check("N.sig_count", sig["count"], 1)
	_check("N.old_match", r["old_value"], sig["old"])
	_check("N.new_match", r["new_value"], sig["new"])
	store.value_changed.disconnect(cb)
	store.free()


func _test_failure_report_unchanged() -> void:
	print("[O] 실패 report에서 값·signal 불변")
	var store := _make_store()
	_value_log.clear()
	var r := store.add_state(&"player.gold", 1.5)  # type_mismatch
	_check("O.applied", r["applied"], false)
	_check("O.changed", r["changed"], false)
	_check("O.old_null", r["old_value"], null)
	_check("O.new_null", r["new_value"], null)
	_check("O.store_unchanged", store.get_value(&"player.gold"), 100)
	_check("O.no_signal", _value_log.size(), 0)
	store.free()


func _test_report_tampering_isolated() -> void:
	print("[P] 외부 report 변조가 Store/다음 호출에 영향 없음")
	var store := _make_store()
	var r := store.add_state(&"player.gold", 10)  # 100 -> 110
	# 반환된 report를 외부에서 변조
	r["new_value"] = 99999
	r["applied"] = false
	r["old_value"] = 0
	_check("P.store_after_tamper", store.get_value(&"player.gold"), 110)
	# 다음 호출은 변조와 무관하게 직전 commit(110) 기준
	var r2 := store.add_state(&"player.gold", 5)
	_check("P.next_old", r2["old_value"], 110)
	_check("P.next_new", r2["new_value"], 115)
	_check("P.store_final", store.get_value(&"player.gold"), 115)
	store.free()


func _test_sequential_add_uses_committed_value() -> void:
	print("[Q] 연속 Add가 직전 commit 값을 기준으로 계산")
	var store := _make_store()
	var olds: Array = []
	var news: Array = []
	for delta in [10, 20, -5]:
		var r := store.add_state(&"player.gold", delta)
		olds.append(r["old_value"])
		news.append(r["new_value"])
	_check("Q.olds", olds, [100, 110, 130])
	_check("Q.news", news, [110, 130, 125])
	_check("Q.store_final", store.get_value(&"player.gold"), 125)
	store.free()


func _test_regression_set_batch_snapshot() -> void:
	print("[R] 기존 set_value/apply_batch/snapshot 회귀 유지")
	var store := _make_store()
	# set_value
	_check("R.set_ok", store.set_value(&"player.gold", 42), OK)
	_check("R.set_value", store.get_value(&"player.gold"), 42)
	# apply_batch
	var rb := store.apply_batch(_batch([
		{"key": &"player.gold", "value": 1},
		{"key": &"player.hp", "value": 3.0},
	]))
	_check("R.batch_applied", rb["applied"], true)
	_check("R.batch_gold", store.get_value(&"player.gold"), 1)
	# snapshot export/import 왕복
	var snap := store.export_snapshot()
	store.set_value(&"player.gold", 777)
	var rep := store.import_snapshot(snap)
	_check("R.import_no_errors", rep["errors"].size(), 0)
	_check("R.import_restored", store.get_value(&"player.gold"), 1)
	store.free()
