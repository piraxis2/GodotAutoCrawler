# DT-005 Step 4 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://Assets/Script/gds/world_state/tests/dt005_step4_batch_test.tscn
#
# 검증 범위 (apply_batch):
# - 모든 변경을 먼저 검증, 하나라도 실패하면 전체 거부(부분 적용 없음)
# - 같은 key 중복 시 전체 거부
# - 결과 diff에 key/old/new 기록
# - 실패한 batch는 값과 signal을 모두 바꾸지 않음
# - 성공 시 모든 값 반영 후 입력 순서로 value_changed 발행(부분 상태 없음)
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime

var _failures: int = 0
var _value_log: Array = []


func _ready() -> void:
	_test_valid_batch_applies_in_order()
	_test_input_order_signals()
	_test_atomic_reject_on_type_error()
	_test_duplicate_key_rejected()
	_test_unknown_key_rejected()
	_test_readonly_rejected()
	_test_out_of_domain_rejected()
	_test_malformed_change_rejected()
	_test_multiple_errors_collected()
	_test_same_value_no_signal_no_diff()
	_test_empty_batch()
	_test_not_ready()
	_test_no_partial_state_and_reentrancy()
	_test_invalid_key_type()
	_test_string_key_allowed()
	_test_duplicate_independent_of_other_errors()

	if _failures == 0:
		print("[DT-005 Step4] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-005 Step4] FAILED: %d assertion(s)" % _failures)
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
		_def(&"world.locked", VT.BOOL, true, LT.SAVE, false),  # read-only
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


func _errors_have(report: Dictionary, reason: String) -> bool:
	for e in report["errors"]:
		if e.get("reason") == reason:
			return true
	return false


func _diff_keys(report: Dictionary) -> Array:
	var out: Array = []
	for d in report["diff"]:
		out.append(String(d["key"]))
	return out


func _log_keys() -> Array:
	var out: Array = []
	for e in _value_log:
		out.append(String(e["key"]))
	return out


# --- 시나리오 ---------------------------------------------------------

func _test_valid_batch_applies_in_order() -> void:
	print("[A] 정상 batch 전체 적용 + diff")
	var store := _make_store()
	_value_log.clear()
	var report := store.apply_batch(_batch([
		{"key": &"player.gold", "value": 200},
		{"key": &"player.hp", "value": 5.5},
		{"key": &"player.name", "value": "x"},
	]))
	_check("A.applied", report["applied"], true)
	_check("A.errors", report["errors"].size(), 0)
	_check("A.gold", store.get_value(&"player.gold"), 200)
	_check("A.hp", store.get_value(&"player.hp"), 5.5)
	_check("A.name", store.get_value(&"player.name"), "x")
	# diff key/old/new
	_check("A.diff_size", report["diff"].size(), 3)
	_check("A.diff0_key", String(report["diff"][0]["key"]), "player.gold")
	_check("A.diff0_old", report["diff"][0]["old"], 100)
	_check("A.diff0_new", report["diff"][0]["new"], 200)
	_check("A.signal_count", _value_log.size(), 3)
	store.free()


func _test_input_order_signals() -> void:
	print("[B] value_changed는 입력 순서로 발행(contract 순서 아님)")
	var store := _make_store()
	_value_log.clear()
	# contract 순서는 gold가 name보다 앞이지만, 입력 순서는 name -> gold.
	store.apply_batch(_batch([
		{"key": &"player.name", "value": "z"},
		{"key": &"player.gold", "value": 7},
	]))
	_check("B.order", _log_keys(), ["player.name", "player.gold"])
	store.free()


func _test_atomic_reject_on_type_error() -> void:
	print("[C] 중간 타입 오류 시 전체 거부(값/signal 불변)")
	var store := _make_store()
	_value_log.clear()
	var report := store.apply_batch(_batch([
		{"key": &"player.gold", "value": 200},      # 유효
		{"key": &"player.hp", "value": "not-float"}, # 타입 오류
	]))
	_check("C.applied", report["applied"], false)
	_check_true("C.type_error", _errors_have(report, "type_mismatch"))
	_check("C.gold_unchanged", store.get_value(&"player.gold"), 100)
	_check("C.no_signal", _value_log.size(), 0)
	_check("C.diff_empty", report["diff"].size(), 0)
	store.free()


func _test_duplicate_key_rejected() -> void:
	print("[D] 같은 key 중복 시 전체 거부")
	var store := _make_store()
	_value_log.clear()
	var report := store.apply_batch(_batch([
		{"key": &"player.gold", "value": 1},
		{"key": &"player.gold", "value": 2},
	]))
	_check("D.applied", report["applied"], false)
	_check_true("D.dup_error", _errors_have(report, "duplicate_key"))
	_check("D.unchanged", store.get_value(&"player.gold"), 100)
	_check("D.no_signal", _value_log.size(), 0)
	store.free()


func _test_unknown_key_rejected() -> void:
	print("[E] 미등록 key 거부")
	var store := _make_store()
	var report := store.apply_batch(_batch([{"key": &"nope.nope", "value": 1}]))
	_check("E.applied", report["applied"], false)
	_check_true("E.unknown", _errors_have(report, "unknown_key"))
	store.free()


func _test_readonly_rejected() -> void:
	print("[F] read-only key 거부(전체 batch)")
	var store := _make_store()
	_value_log.clear()
	var report := store.apply_batch(_batch([
		{"key": &"player.gold", "value": 5},
		{"key": &"world.locked", "value": false},
	]))
	_check("F.applied", report["applied"], false)
	_check_true("F.read_only", _errors_have(report, "read_only"))
	_check("F.gold_unchanged", store.get_value(&"player.gold"), 100)
	_check("F.no_signal", _value_log.size(), 0)
	store.free()


func _test_out_of_domain_rejected() -> void:
	print("[G] JSON-safe 도메인 위반 거부")
	var store := _make_store()
	var r1 := store.apply_batch(_batch([{"key": &"player.gold", "value": 9007199254740993}]))
	_check_true("G.int_over", _errors_have(r1, "out_of_domain"))
	var r2 := store.apply_batch(_batch([{"key": &"player.hp", "value": INF}]))
	_check_true("G.float_inf", _errors_have(r2, "out_of_domain"))
	_check("G.gold_unchanged", store.get_value(&"player.gold"), 100)
	store.free()


func _test_malformed_change_rejected() -> void:
	print("[H] 형식 오류 change 거부")
	var store := _make_store()
	var report := store.apply_batch(_batch([{"key": &"player.gold"}]))  # value 없음
	_check("H.applied", report["applied"], false)
	_check_true("H.malformed", _errors_have(report, "malformed_change"))
	store.free()


func _test_multiple_errors_collected() -> void:
	print("[I] 여러 오류를 모두 수집")
	var store := _make_store()
	var report := store.apply_batch(_batch([
		{"key": &"nope.nope", "value": 1},          # unknown
		{"key": &"player.gold", "value": "x"},      # type
	]))
	_check("I.applied", report["applied"], false)
	_check("I.error_count", report["errors"].size(), 2)
	_check_true("I.unknown", _errors_have(report, "unknown_key"))
	_check_true("I.type", _errors_have(report, "type_mismatch"))
	store.free()


func _test_same_value_no_signal_no_diff() -> void:
	print("[J] 같은 값은 적용되되 signal/diff 없음")
	var store := _make_store()
	_value_log.clear()
	# gold는 바뀌고(300), hp는 default와 동일(10.0)
	var report := store.apply_batch(_batch([
		{"key": &"player.gold", "value": 300},
		{"key": &"player.hp", "value": 10.0},
	]))
	_check("J.applied", report["applied"], true)
	_check("J.diff_keys", _diff_keys(report), ["player.gold"])  # hp 제외
	_check("J.signal_count", _value_log.size(), 1)
	_check("J.signal_key", _log_keys(), ["player.gold"])
	store.free()


func _test_empty_batch() -> void:
	print("[K] 빈 batch는 성공(무변경)")
	var store := _make_store()
	_value_log.clear()
	var report := store.apply_batch(_batch([]))
	_check("K.applied", report["applied"], true)
	_check("K.diff_empty", report["diff"].size(), 0)
	_check("K.no_signal", _value_log.size(), 0)
	store.free()


func _test_not_ready() -> void:
	print("[L] not-ready store")
	var store := WorldStateStore.new()
	store.schema = null
	store.initialize()
	var report := store.apply_batch(_batch([{"key": &"player.gold", "value": 1}]))
	_check("L.applied", report["applied"], false)
	_check_true("L.not_ready", _errors_have(report, "store_not_ready"))
	store.free()


func _test_no_partial_state_and_reentrancy() -> void:
	print("[M] 적용 전 전체 반영 + 알림 중 재진입 거부")
	var store := _make_store()
	store.set_value(&"player.gold", 0)
	store.set_value(&"player.hp", 0.0)

	var observed := {"first_gold": null, "first_hp": null, "reenter_err": OK, "batch_busy": false}
	var cb := func(k, _o, _n):
		if k == &"player.gold":
			# 첫 callback에서 이미 모든 batch 값이 반영돼 있어야 한다.
			observed["first_gold"] = store.get_value(&"player.gold")
			observed["first_hp"] = store.get_value(&"player.hp")
			# 알림 중 재진입 mutation은 거부된다.
			observed["reenter_err"] = store.set_value(&"player.hp", 1.0)
			var rb: Dictionary = store.apply_batch(_batch([{"key": &"player.gold", "value": 9}]))
			observed["batch_busy"] = _errors_have(rb, "store_busy")
	store.value_changed.connect(cb)

	store.apply_batch(_batch([
		{"key": &"player.gold", "value": 5},
		{"key": &"player.hp", "value": 7.0},
	]))
	_check("M.first_gold", observed["first_gold"], 5)
	_check("M.first_hp", observed["first_hp"], 7.0)  # 부분 상태 없음
	_check("M.reenter_busy", observed["reenter_err"], ERR_BUSY)
	_check_true("M.batch_busy", observed["batch_busy"])
	_check("M.hp_final", store.get_value(&"player.hp"), 7.0)
	store.value_changed.disconnect(cb)
	store.free()


func _test_invalid_key_type() -> void:
	print("[N] 잘못된 key 타입 -> 런타임 오류 대신 malformed_change")
	var store := _make_store()
	_value_log.clear()
	# null key
	var r_null := store.apply_batch(_batch([{"key": null, "value": 1}]))
	_check("N.null_applied", r_null["applied"], false)
	_check_true("N.null_malformed", _errors_have(r_null, "malformed_change"))
	# int key
	var r_int := store.apply_batch(_batch([{"key": 5, "value": 1}]))
	_check("N.int_applied", r_int["applied"], false)
	_check_true("N.int_malformed", _errors_have(r_int, "malformed_change"))
	# 값/signal 불변
	_check("N.gold_unchanged", store.get_value(&"player.gold"), 100)
	_check("N.no_signal", _value_log.size(), 0)
	store.free()


func _test_string_key_allowed() -> void:
	print("[O] String key 허용")
	var store := _make_store()
	var report := store.apply_batch(_batch([{"key": "player.gold", "value": 42}]))
	_check("O.applied", report["applied"], true)
	_check("O.value", store.get_value(&"player.gold"), 42)
	store.free()


func _test_duplicate_independent_of_other_errors() -> void:
	print("[P] 중복 검사는 다른 오류와 독립")
	var store := _make_store()
	# 첫 항목 type_mismatch + 같은 key 재등장 -> 두 오류 모두 기록.
	var r1 := store.apply_batch(_batch([
		{"key": &"player.gold", "value": "bad"},  # type_mismatch
		{"key": &"player.gold", "value": 1},      # duplicate
	]))
	_check("P.applied", r1["applied"], false)
	_check_true("P.type", _errors_have(r1, "type_mismatch"))
	_check_true("P.dup", _errors_have(r1, "duplicate_key"))
	_check("P.error_count", r1["errors"].size(), 2)
	# 같은 unknown key 반복 -> unknown_key + duplicate_key.
	var r2 := store.apply_batch(_batch([
		{"key": &"nope.nope", "value": 1},
		{"key": &"nope.nope", "value": 2},
	]))
	_check_true("P.unknown", _errors_have(r2, "unknown_key"))
	_check_true("P.unknown_dup", _errors_have(r2, "duplicate_key"))
	store.free()
