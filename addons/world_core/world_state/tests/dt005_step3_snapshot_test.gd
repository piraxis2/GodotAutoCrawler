# DT-005 Step 3 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/world_core/world_state/tests/dt005_step3_snapshot_test.tscn
#
# 검증 범위:
# - export: SAVE만 포함, SESSION 제외, StringName -> String wire
# - reset_lifetime: 해당 lifetime만 default 복원 + state_reset 발행
# - snapshot round-trip(메모리/JSON)에서 값과 schema 타입 보존
# - import replace-load: 없는 SAVE key는 default, SESSION은 미변경
# - 잘못된 snapshot(구조/version)은 Store를 손상시키지 않음
# - unknown/SESSION/type mismatch 항목 개별 무시 + report
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime

var _failures: int = 0
var _value_log: Array = []
var _reset_log: Array = []
var _import_log: Array = []


func _ready() -> void:
	_test_export_shape_and_lifetime()
	_test_export_session_explicit()
	_test_reset_lifetime()
	_test_roundtrip_memory()
	_test_roundtrip_json_types()
	_test_import_replace_load()
	_test_import_malformed_rejected()
	_test_import_version_mismatch_rejected()
	_test_import_item_classification()
	_test_import_applies_to_readonly()
	_test_import_signal_emitted()
	_test_import_atomic_no_partial_state()
	_test_lossy_coercion_rejected()
	_test_report_isolation_and_unified_emit()
	_test_large_int_json_safety()
	_test_unsafe_write_and_default_rejected()
	_test_notification_reentrancy_rejected()

	if _failures == 0:
		print("[DT-005 Step3] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-005 Step3] FAILED: %d assertion(s)" % _failures)
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


# 표준 schema: SAVE 5종 + SESSION 2종.
func _make_store(version: int = 1) -> WorldStateStore:
	var s := _schema([
		_def(&"player.gold", VT.INT, 100),
		_def(&"player.hp", VT.FLOAT, 10.0),
		_def(&"player.name", VT.STRING, "hero"),
		_def(&"actor.mood", VT.STRING_NAME, &"calm"),
		_def(&"world.locked", VT.BOOL, true, LT.SAVE, false),  # read-only
		_def(&"session.temp", VT.INT, 0, LT.SESSION),
		_def(&"session.flag", VT.BOOL, false, LT.SESSION),
	], version)
	var store := WorldStateStore.new()
	store.schema = s
	store.value_changed.connect(func(k, o, n): _value_log.append({"key": k, "old": o, "new": n}))
	store.state_reset.connect(func(lt): _reset_log.append(lt))
	store.snapshot_imported.connect(func(r): _import_log.append(r))
	store.initialize()
	return store


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# report의 특정 list에서 key 존재 확인. list는 [{key,reason}...] 또는 [key...] 둘 다 가능.
func _list_has_key(items: Array, key: String) -> bool:
	for it in items:
		if it is Dictionary:
			if it.get("key") == key:
				return true
		elif it == key:
			return true
	return false


# --- 시나리오 ---------------------------------------------------------

func _test_export_shape_and_lifetime() -> void:
	print("[A] export 형식 + lifetime 분리")
	var store := _make_store()
	store.set_value(&"player.gold", 200)
	var snap := store.export_snapshot()
	_check("A.version", snap["schema_version"], 1)
	_check_true("A.has_values", snap["values"] is Dictionary)
	var v: Dictionary = snap["values"]
	_check_true("A.has_gold", v.has("player.gold"))
	_check("A.gold_value", v["player.gold"], 200)
	_check_true("A.has_locked", v.has("world.locked"))
	# SESSION 제외
	_check_true("A.no_session_temp", not v.has("session.temp"))
	_check_true("A.no_session_flag", not v.has("session.flag"))
	# StringName -> String wire
	_check("A.mood_wire_type", typeof(v["actor.mood"]), TYPE_STRING)
	_check("A.mood_wire_value", v["actor.mood"], "calm")
	store.free()


func _test_export_session_explicit() -> void:
	print("[B] export(SESSION) 명시 시 SESSION만")
	var store := _make_store()
	var snap := store.export_snapshot(LT.SESSION)
	var v: Dictionary = snap["values"]
	_check_true("B.has_session_temp", v.has("session.temp"))
	_check_true("B.has_session_flag", v.has("session.flag"))
	_check_true("B.no_save_gold", not v.has("player.gold"))
	store.free()


func _test_reset_lifetime() -> void:
	print("[C] reset_lifetime")
	var store := _make_store()
	store.set_value(&"player.gold", 500)
	store.set_value(&"session.temp", 9)
	_reset_log.clear()
	_value_log.clear()

	store.reset_lifetime(LT.SESSION)
	_check("C.session_reset", store.get_value(&"session.temp"), 0)
	_check("C.save_untouched", store.get_value(&"player.gold"), 500)
	_check("C.reset_signal", _reset_log.size(), 1)
	_check("C.reset_signal_arg", _reset_log[0], LT.SESSION)
	_check_true("C.value_changed_session", _value_log.size() >= 1)

	_reset_log.clear()
	store.reset_lifetime(LT.SAVE)
	_check("C.save_reset", store.get_value(&"player.gold"), 100)
	_check("C.locked_default", store.get_value(&"world.locked"), true)
	_check("C.reset_signal2_arg", _reset_log[0], LT.SAVE)
	store.free()


func _test_roundtrip_memory() -> void:
	print("[D] 메모리 snapshot 왕복")
	var store := _make_store()
	store.set_value(&"player.gold", 300)
	store.set_value(&"player.hp", 3.5)
	store.set_value(&"player.name", "x")
	store.set_value(&"actor.mood", &"angry")
	var snap := store.export_snapshot()

	# Store를 흩뜨린다.
	store.set_value(&"player.gold", 1)
	store.set_value(&"player.hp", 1.0)
	store.set_value(&"player.name", "y")
	store.set_value(&"actor.mood", &"z")

	var report := store.import_snapshot(snap)
	_check("D.no_errors", report["errors"].size(), 0)
	_check("D.gold", store.get_value(&"player.gold"), 300)
	_check("D.hp", store.get_value(&"player.hp"), 3.5)
	_check("D.name", store.get_value(&"player.name"), "x")
	_check("D.mood", store.get_value(&"actor.mood"), "angry")
	_check("D.mood_typeof", typeof(store.get_value(&"actor.mood")), TYPE_STRING_NAME)
	store.free()


func _test_roundtrip_json_types() -> void:
	print("[E] JSON 직렬화/역직렬화 후 타입 복원")
	var store := _make_store()
	store.set_value(&"player.gold", 42)
	store.set_value(&"player.hp", 7.5)
	store.set_value(&"player.name", "knight")
	store.set_value(&"actor.mood", &"brave")
	store.set_value(&"world.locked", true) # read-only지만 default와 동일
	var snap := store.export_snapshot()

	var json := JSON.stringify(snap)
	var parsed: Variant = JSON.parse_string(json)
	_check_true("E.parsed_dict", parsed is Dictionary)

	# 흩뜨린 뒤 parsed로 import.
	store.set_value(&"player.gold", 0)
	store.set_value(&"player.hp", 0.0)
	store.set_value(&"player.name", "")
	store.set_value(&"actor.mood", &"x")
	var report := store.import_snapshot(parsed)
	_check("E.no_errors", report["errors"].size(), 0)

	_check("E.gold", store.get_value(&"player.gold"), 42)
	_check("E.gold_typeof", typeof(store.get_value(&"player.gold")), TYPE_INT)
	_check("E.hp", store.get_value(&"player.hp"), 7.5)
	_check("E.hp_typeof", typeof(store.get_value(&"player.hp")), TYPE_FLOAT)
	_check("E.name", store.get_value(&"player.name"), "knight")
	_check("E.name_typeof", typeof(store.get_value(&"player.name")), TYPE_STRING)
	_check("E.mood", store.get_value(&"actor.mood"), "brave")
	_check("E.mood_typeof", typeof(store.get_value(&"actor.mood")), TYPE_STRING_NAME)
	_check("E.locked_typeof", typeof(store.get_value(&"world.locked")), TYPE_BOOL)
	store.free()


func _test_import_replace_load() -> void:
	print("[F] import replace-load: 없는 SAVE key는 default, SESSION 미변경")
	var store := _make_store()
	store.set_value(&"player.gold", 300)
	store.set_value(&"player.name", "x")
	store.set_value(&"session.temp", 7)

	# player.gold만 담은 부분 snapshot.
	var snap := {"schema_version": 1, "values": {"player.gold": 555}}
	var report := store.import_snapshot(snap)
	_check("F.applied_gold", _list_has_key(report["applied"], "player.gold"), true)
	_check("F.gold", store.get_value(&"player.gold"), 555)
	# snapshot에 없는 SAVE key는 default로.
	_check("F.name_default", store.get_value(&"player.name"), "hero")
	# SESSION은 import가 건드리지 않는다.
	_check("F.session_untouched", store.get_value(&"session.temp"), 7)
	store.free()


func _test_import_malformed_rejected() -> void:
	print("[G] 잘못된 구조 snapshot 전체 거부(무변경)")
	var store := _make_store()
	store.set_value(&"player.gold", 300)

	var bad := [
		{},                                           # 빈
		{"schema_version": 1},                        # values 없음
		{"values": {}},                               # version 없음
		{"schema_version": "1", "values": {}},        # version 타입 오류(String)
		{"schema_version": 1, "values": 5},           # values 타입 오류
	]
	var idx := 0
	for snap in bad:
		var report := store.import_snapshot(snap)
		_check("G[%d].rejected" % idx, _list_has_key(report["errors"], ""), true)
		_check("G[%d].applied_empty" % idx, report["applied"].size(), 0)
		idx += 1
	# 어떤 경우에도 값이 바뀌지 않았다.
	_check("G.gold_unchanged", store.get_value(&"player.gold"), 300)
	store.free()


func _test_import_version_mismatch_rejected() -> void:
	print("[H] schema_version 불일치 전체 거부")
	var store := _make_store()
	store.set_value(&"player.gold", 300)
	var snap := {"schema_version": 2, "values": {"player.gold": 1}}
	var report := store.import_snapshot(snap)
	_check_true("H.error", _list_has_key(report["errors"], ""))
	_check("H.applied_empty", report["applied"].size(), 0)
	_check("H.gold_unchanged", store.get_value(&"player.gold"), 300)
	store.free()


func _test_import_item_classification() -> void:
	print("[I] unknown/SESSION/type-mismatch 개별 무시 + 유효 항목 적용")
	var store := _make_store()
	var snap := {
		"schema_version": 1,
		"values": {
			"nope.nope": 1,          # unknown
			"session.temp": 5,       # SESSION
			"player.gold": "bad",    # type mismatch
			"player.hp": 2.5,        # valid
		},
	}
	var report := store.import_snapshot(snap)
	_check_true("I.unknown_ignored", _list_has_key(report["ignored"], "nope.nope"))
	_check_true("I.session_ignored", _list_has_key(report["ignored"], "session.temp"))
	_check_true("I.gold_error", _list_has_key(report["errors"], "player.gold"))
	_check_true("I.hp_applied", _list_has_key(report["applied"], "player.hp"))
	_check("I.hp_value", store.get_value(&"player.hp"), 2.5)
	# 타입 오류라 적용 안 됨 -> replace-load로 default 복원.
	_check("I.gold_default", store.get_value(&"player.gold"), 100)
	# SESSION은 import에서 reset되지 않는다(default 0이 우연히 같더라도 미변경 경로).
	_check("I.session_untouched", store.get_value(&"session.temp"), 0)
	store.free()


func _test_import_applies_to_readonly() -> void:
	print("[J] import는 read-only key에도 적용(시스템 작업)")
	var store := _make_store()
	var snap := {"schema_version": 1, "values": {"world.locked": false}}
	var report := store.import_snapshot(snap)
	_check_true("J.applied", _list_has_key(report["applied"], "world.locked"))
	_check("J.value", store.get_value(&"world.locked"), false)
	store.free()


func _test_import_signal_emitted() -> void:
	print("[K] snapshot_imported 시그널 + 거부 후 Store 정상")
	var store := _make_store()
	_import_log.clear()
	store.import_snapshot({"schema_version": 1, "values": {"player.gold": 7}})
	_check("K.signal_count", _import_log.size(), 1)
	_check_true("K.signal_report", _import_log[0] is Dictionary and _import_log[0].has("applied"))

	# 거부된 import 뒤에도 Store는 정상 동작.
	store.import_snapshot({})
	_check("K.set_after_reject", store.set_value(&"player.gold", 11), OK)
	_check("K.get_after_reject", store.get_value(&"player.gold"), 11)
	store.free()


func _test_import_atomic_no_partial_state() -> void:
	print("[L] import 중 value_changed가 부분 상태를 노출하지 않음")
	var store := _make_store()
	# 여러 SAVE key를 default와 다르게 만들어 import가 여럿을 바꾸게 한다.
	store.set_value(&"player.gold", 0)
	store.set_value(&"player.hp", 0.0)
	store.set_value(&"player.name", "")

	# 첫 value_changed callback 시점의 전체 상태를 포착한다.
	var captured := {"done": false, "gold": null, "hp": null, "name": null}
	var cb := func(_k, _o, _n):
		if not captured["done"]:
			captured["done"] = true
			captured["gold"] = store.get_value(&"player.gold")
			captured["hp"] = store.get_value(&"player.hp")
			captured["name"] = store.get_value(&"player.name")
	store.value_changed.connect(cb)

	store.import_snapshot({
		"schema_version": 1,
		"values": {"player.gold": 7, "player.hp": 3.0, "player.name": "q"},
	})
	# 첫 callback에서 이미 모든 값이 최종 상태여야 한다(혼합 상태 없음).
	_check("L.first_cb_gold", captured["gold"], 7)
	_check("L.first_cb_hp", captured["hp"], 3.0)
	_check("L.first_cb_name", captured["name"], "q")
	store.value_changed.disconnect(cb)
	store.free()


func _test_lossy_coercion_rejected() -> void:
	print("[M] 손실 입력 coercion 거부")

	# schema_version 1.5 -> malformed(비정수)
	var s1 := _make_store()
	var r1 := s1.import_snapshot({"schema_version": 1.5, "values": {"player.gold": 5}})
	_check_true("M.v1_5_rejected", _list_has_key(r1["errors"], ""))
	_check("M.v1_5_no_apply", r1["applied"].size(), 0)
	s1.free()

	# schema_version 1.0(정수값 float) -> 허용
	var s2 := _make_store()
	var r2 := s2.import_snapshot({"schema_version": 1.0, "values": {"player.gold": 5}})
	_check_true("M.v1_0_applied", _list_has_key(r2["applied"], "player.gold"))
	_check("M.v1_0_value", s2.get_value(&"player.gold"), 5)
	s2.free()

	# INT에 1.000001(비정수 float) -> type_mismatch, default 복원
	var s3 := _make_store()
	var r3 := s3.import_snapshot({"schema_version": 1, "values": {"player.gold": 1.000001}})
	_check_true("M.int_nonintegral_error", _list_has_key(r3["errors"], "player.gold"))
	_check("M.int_nonintegral_default", s3.get_value(&"player.gold"), 100)
	s3.free()

	# INT에 5.0(정수값 float) -> 허용
	var s4 := _make_store()
	var r4 := s4.import_snapshot({"schema_version": 1, "values": {"player.gold": 5.0}})
	_check_true("M.int_integral_applied", _list_has_key(r4["applied"], "player.gold"))
	_check("M.int_integral_value", s4.get_value(&"player.gold"), 5)
	s4.free()

	# FLOAT에 inf/nan -> type_mismatch
	var s5 := _make_store()
	var r5 := s5.import_snapshot({"schema_version": 1, "values": {"player.hp": INF}})
	_check_true("M.float_inf_error", _list_has_key(r5["errors"], "player.hp"))
	var r6 := s5.import_snapshot({"schema_version": 1, "values": {"player.hp": NAN}})
	_check_true("M.float_nan_error", _list_has_key(r6["errors"], "player.hp"))
	s5.free()


func _test_report_isolation_and_unified_emit() -> void:
	print("[N] report deep-copy 격리 + 모든 종료 경로 발행")
	var store := _make_store()
	var sig_reports: Array = []
	store.snapshot_imported.connect(func(r): sig_reports.append(r))

	var ret := store.import_snapshot({"schema_version": 1, "values": {"player.gold": 7}})
	# 반환 report를 변조해도 signal로 받은 report는 영향받지 않는다.
	ret["applied"].clear()
	ret["errors"].append({"key": "x", "reason": "tampered"})
	_check("N.sig_count", sig_reports.size(), 1)
	_check_true("N.sig_applied_intact", _list_has_key(sig_reports[0]["applied"], "player.gold"))
	_check("N.sig_no_tamper", sig_reports[0]["errors"].size(), 0)
	store.free()

	# not-ready 경로도 snapshot_imported를 발행한다(발행 정책 통일).
	var store2 := WorldStateStore.new()
	store2.schema = null
	store2.initialize()
	var got: Array = []
	store2.snapshot_imported.connect(func(r): got.append(r))
	var rep := store2.import_snapshot({"schema_version": 1, "values": {}})
	_check("N.notready_emitted", got.size(), 1)
	_check_true("N.notready_error", _list_has_key(rep["errors"], ""))
	store2.free()


func _test_large_int_json_safety() -> void:
	print("[O] 큰 INT는 JSON-safe 범위만 허용(조용한 정밀도 손실 방지)")
	var store := _make_store()

	# 경계: 2^53-1 허용 + JSON 왕복 보존.
	var maxsafe := 9007199254740991
	var r_ok := store.import_snapshot({"schema_version": 1, "values": {"player.gold": maxsafe}})
	_check_true("O.maxsafe_applied", _list_has_key(r_ok["applied"], "player.gold"))
	_check("O.maxsafe_value", store.get_value(&"player.gold"), maxsafe)
	var parsed: Variant = JSON.parse_string(JSON.stringify(store.export_snapshot()))
	store.set_value(&"player.gold", 0)
	store.import_snapshot(parsed)
	_check("O.maxsafe_json_roundtrip", store.get_value(&"player.gold"), maxsafe)

	# 2^53+1: int wire여도 거부, default 유지(리뷰 repro 9007199254740993).
	store.reset_value(&"player.gold")
	var r_over := store.import_snapshot({"schema_version": 1, "values": {"player.gold": 9007199254740993}})
	_check_true("O.2p53p1_rejected", _list_has_key(r_over["errors"], "player.gold"))
	_check("O.2p53p1_default", store.get_value(&"player.gold"), 100)

	# 2^53+1을 JSON으로 보내도 조용히 9007199254740992로 적용되지 않는다.
	var bad_parsed: Variant = JSON.parse_string('{"schema_version":1,"values":{"player.gold":9007199254740993}}')
	var r_badjson := store.import_snapshot(bad_parsed)
	_check_true("O.2p53p1_json_rejected", _list_has_key(r_badjson["errors"], "player.gold"))
	_check("O.2p53p1_json_default", store.get_value(&"player.gold"), 100)

	# INT64_MAX/INT64_MIN(int wire) 거부.
	var r_i64max := store.import_snapshot({"schema_version": 1, "values": {"player.gold": 9223372036854775807}})
	_check_true("O.i64max_rejected", _list_has_key(r_i64max["errors"], "player.gold"))
	var r_i64min := store.import_snapshot({"schema_version": 1, "values": {"player.gold": -9223372036854775808}})
	_check_true("O.i64min_rejected", _list_has_key(r_i64min["errors"], "player.gold"))

	# 양의 2^63 float 거부(INT64_MIN으로 wrap되던 버그).
	var r_2p63 := store.import_snapshot({"schema_version": 1, "values": {"player.gold": 9223372036854775808.0}})
	_check_true("O.2p63_float_rejected", _list_has_key(r_2p63["errors"], "player.gold"))

	# 경계: -(2^53-1) 허용.
	store.reset_value(&"player.gold")
	var minsafe := -9007199254740991
	var r_min := store.import_snapshot({"schema_version": 1, "values": {"player.gold": minsafe}})
	_check_true("O.minsafe_applied", _list_has_key(r_min["applied"], "player.gold"))
	_check("O.minsafe_value", store.get_value(&"player.gold"), minsafe)

	# FLOAT key에 int wire: float 변환 정밀도 손실을 막기 위해 safe 범위만 허용한다.
	store.reset_value(&"player.hp") # default 10.0
	var rf_over := store.import_snapshot({"schema_version": 1, "values": {"player.hp": 9007199254740993}})
	_check_true("O.float_int_over_rejected", _list_has_key(rf_over["errors"], "player.hp"))
	_check("O.float_int_over_default", store.get_value(&"player.hp"), 10.0)
	var rf_i64max := store.import_snapshot({"schema_version": 1, "values": {"player.hp": 9223372036854775807}})
	_check_true("O.float_i64max_rejected", _list_has_key(rf_i64max["errors"], "player.hp"))
	var rf_i64min := store.import_snapshot({"schema_version": 1, "values": {"player.hp": -9223372036854775808}})
	_check_true("O.float_i64min_rejected", _list_has_key(rf_i64min["errors"], "player.hp"))
	# FLOAT key에 ±(2^53-1) int wire: 허용 + 정확 보존.
	var rf_max := store.import_snapshot({"schema_version": 1, "values": {"player.hp": maxsafe}})
	_check_true("O.float_maxsafe_applied", _list_has_key(rf_max["applied"], "player.hp"))
	_check("O.float_maxsafe_value", store.get_value(&"player.hp"), float(maxsafe))
	store.reset_value(&"player.hp")
	var rf_min := store.import_snapshot({"schema_version": 1, "values": {"player.hp": minsafe}})
	_check_true("O.float_minsafe_applied", _list_has_key(rf_min["applied"], "player.hp"))
	_check("O.float_minsafe_value", store.get_value(&"player.hp"), float(minsafe))
	store.free()


func _test_unsafe_write_and_default_rejected() -> void:
	print("[P] unsafe set/default/schema_version 거부 (export 무손실 보장)")

	# P1: set_value INT 2^53+1 거부, 값 불변.
	var store := _make_store()
	_check("P1.int_over_safe", store.set_value(&"player.gold", 9007199254740993), ERR_INVALID_DATA)
	_check("P1.unchanged", store.get_value(&"player.gold"), 100)
	# P2: set_value FLOAT INF/NAN 거부.
	_check("P2.inf", store.set_value(&"player.hp", INF), ERR_INVALID_DATA)
	_check("P2.nan", store.set_value(&"player.hp", NAN), ERR_INVALID_DATA)
	_check("P2.hp_unchanged", store.get_value(&"player.hp"), 10.0)
	# P3: safe 경계값은 set 가능 + export 무손실.
	_check("P3.maxsafe_set", store.set_value(&"player.gold", 9007199254740991), OK)
	var parsed: Variant = JSON.parse_string(JSON.stringify(store.export_snapshot()))
	store.set_value(&"player.gold", 0)
	store.import_snapshot(parsed)
	_check("P3.export_roundtrip", store.get_value(&"player.gold"), 9007199254740991)
	store.free()

	# P4: schema_version가 JSON-safe 범위 밖이면 not ready.
	var s_ver := _schema([_def(&"player.gold", VT.INT, 1)], 9007199254740993)
	var st_ver := WorldStateStore.new()
	st_ver.schema = s_ver
	_check("P4.init", st_ver.initialize(), false)
	_check("P4.not_ready", st_ver.is_store_ready(), false)
	st_ver.free()

	# P5: INT default가 safe 범위 밖이면 not ready.
	var s_int := _schema([_def(&"player.gold", VT.INT, 9007199254740993)])
	var st_int := WorldStateStore.new()
	st_int.schema = s_int
	_check("P5.init", st_int.initialize(), false)
	st_int.free()

	# P6: FLOAT default가 INF면 not ready.
	var s_flt := _schema([_def(&"player.hp", VT.FLOAT, INF)])
	var st_flt := WorldStateStore.new()
	st_flt.schema = s_flt
	_check("P6.init", st_flt.initialize(), false)
	st_flt.free()


func _test_notification_reentrancy_rejected() -> void:
	print("[Q] value_changed 알림 중 재진입 mutation 거부 (stale event 방지)")
	var store := _make_store()
	store.set_value(&"player.gold", 0)
	store.set_value(&"player.hp", 0.0)

	# gold가 contract 순서상 먼저 발행된다. 그 callback에서 모든 mutation 경로를 재진입 시도한다.
	var observed := {
		"set_err": OK, "reset_err": OK, "init_ret": true,
		"import_busy": false, "hp_during_cb": null,
	}
	var event_ok := {"consistent": true}
	var cb := func(k, _o, n):
		# 각 이벤트의 new_value가 실제 현재 값과 일치해야 한다.
		if store.get_value(k) != n:
			event_ok["consistent"] = false
		if k == &"player.gold":
			observed["set_err"] = store.set_value(&"player.hp", 99.0)
			observed["reset_err"] = store.reset_value(&"player.hp")
			observed["init_ret"] = store.initialize()          # 재초기화도 거부
			store.reset_lifetime(LT.SAVE)                       # 거부(void)
			var rep: Dictionary = store.import_snapshot({"schema_version": 1, "values": {"player.hp": 1.0}})
			observed["import_busy"] = _list_has_key(rep["errors"], "")
			observed["hp_during_cb"] = store.get_value(&"player.hp")
	store.value_changed.connect(cb)

	var report := store.import_snapshot({"schema_version": 1, "values": {"player.gold": 5, "player.hp": 7.0}})

	# 모든 재진입 mutation 경로가 거부된다.
	_check("Q.set_busy", observed["set_err"], ERR_BUSY)
	_check("Q.reset_busy", observed["reset_err"], ERR_BUSY)
	_check("Q.init_false", observed["init_ret"], false)
	_check_true("Q.import_busy", observed["import_busy"])
	# 콜백 중 hp는 batch 값 7.0 유지 — 어떤 재진입도 통하지 않고 기존 상태도 비워지지 않음.
	_check("Q.hp_during_cb", observed["hp_during_cb"], 7.0)
	# 성공 report와 실제 Store 상태가 일치한다(initialize가 batch를 무너뜨리지 않음).
	_check_true("Q.report_applied",
		_list_has_key(report["applied"], "player.gold") and _list_has_key(report["applied"], "player.hp"))
	_check("Q.gold_final", store.get_value(&"player.gold"), 5)
	_check("Q.hp_final", store.get_value(&"player.hp"), 7.0)
	# 모든 이벤트의 new_value가 실제 값과 일치(stale 없음).
	_check_true("Q.event_consistent", event_ok["consistent"])
	store.value_changed.disconnect(cb)

	# 알림 종료 후 명시적 initialize()는 정상 동작한다.
	_check("Q.init_after", store.initialize(), true)
	_check("Q.init_after_default", store.get_value(&"player.gold"), 100)
	store.free()
