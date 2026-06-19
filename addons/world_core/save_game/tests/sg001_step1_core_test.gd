# SG-001 Step 1 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/world_core/save_game/tests/sg001_step1_core_test.tscn
#
# 검증 범위(SG-001 Step 1 완료 조건):
# - 등록: 정상 register, 중복 id 실패, 빈 id 실패, invalid section_version 실패, helper discovery, ordering
# - capture: 정상 envelope, capture 실패 시 envelope 미생성, JSON 호환
# - validate: required missing 실패, save_version mismatch, section_version mismatch,
#   unknown saved section은 ignored로 report(실패 아님)
# - restore: validate 실패 시 restore 0회, restore order deterministic, partial restore report
# - busy guard: capture/restore 재진입 거부
extends Node

var _failures: int = 0


func _ready() -> void:
	_test_register_ok()
	_test_register_duplicate_id()
	_test_register_empty_id()
	_test_register_invalid_version()
	_test_discover_helper()
	_test_ordering_deterministic()
	_test_capture_envelope_and_json()
	_test_capture_failure_no_envelope()
	_test_validate_required_missing()
	_test_validate_save_version_mismatch()
	_test_validate_section_version_mismatch()
	_test_validate_unknown_ignored()
	_test_restore_zero_on_validation_fail()
	_test_restore_order_deterministic()
	_test_restore_partial_report()
	_test_optional_missing_ok()
	_test_busy_guard_reentrant_capture()
	_test_busy_guard_reentrant_restore()
	_test_capture_rejects_non_json_payload()
	_test_capture_rejects_int_overflow()
	_test_capture_accepts_nested_json()
	_test_validate_rejects_non_json_payload()
	_test_revalidate_duplicate_after_register()
	_test_revalidate_empty_id_after_register()
	_test_revalidate_unique_id_change()
	_test_section_version_must_be_integral()

	if _failures == 0:
		print("[SG-001 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[SG-001 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- fake sections ----------------------------------------------------

class FakeSection extends SaveSection:
	var capture_ok: bool = true
	var validate_ok: bool = true
	var restore_ok: bool = true
	var payload_data: Dictionary = {}
	var call_log: Array = []  # 공유 호출 로그(순서 검증용)

	func capture_save() -> Dictionary:
		if not capture_ok:
			return {"ok": false, "reason": &"fake_capture_fail"}
		return {"ok": true, "payload": payload_data.duplicate(true), "reason": &""}

	func validate_save(_payload: Dictionary) -> Dictionary:
		call_log.append("validate:" + String(section_id))
		if not validate_ok:
			return {"ok": false, "reason": &"fake_validate_fail"}
		return {"ok": true, "reason": &""}

	func restore_save(_payload: Dictionary) -> Dictionary:
		call_log.append("restore:" + String(section_id))
		if not restore_ok:
			return {"ok": false, "reason": &"fake_restore_fail"}
		return {"ok": true, "reason": &""}


class ReentrantCaptureSection extends SaveSection:
	var manager: SaveGameManager
	var reentry: Dictionary = {}

	func capture_save() -> Dictionary:
		reentry = manager.capture_all()  # busy여야 한다
		return {"ok": true, "payload": {}, "reason": &""}


class ReentrantRestoreSection extends SaveSection:
	var manager: SaveGameManager
	var envelope: Dictionary = {}
	var reentry: Dictionary = {}

	func restore_save(_payload: Dictionary) -> Dictionary:
		reentry = manager.restore_all(envelope)  # busy여야 한다
		return {"ok": true, "reason": &""}


# --- helpers ----------------------------------------------------------

func _make(id: StringName, order: int = 0, required: bool = true,
		version: int = 1) -> FakeSection:
	var s := FakeSection.new()
	s.section_id = id
	s.restore_order = order
	s.required = required
	s.section_version = version
	add_child(s)  # 트리에 두어 종료 시 정리
	return s


func _new_manager() -> SaveGameManager:
	var m := SaveGameManager.new()
	add_child(m)
	return m


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# --- 시나리오 ---------------------------------------------------------

func _test_register_ok() -> void:
	print("[A] 정상 등록")
	var m := _new_manager()
	var s := _make(&"alpha")
	var r := m.register_section(s)
	_check("A.ok", r["ok"], true)
	_check("A.id", r["section_id"], &"alpha")
	_check("A.has", m.has_section(&"alpha"), true)
	_check("A.count", m.get_section_ids().size(), 1)


func _test_register_duplicate_id() -> void:
	print("[B] 중복 section id 실패")
	var m := _new_manager()
	m.register_section(_make(&"dup"))
	var r := m.register_section(_make(&"dup"))
	_check("B.ok", r["ok"], false)
	_check("B.reason", r["reason"], &"section_id_duplicate")
	_check("B.count", m.get_section_ids().size(), 1)


func _test_register_empty_id() -> void:
	print("[C] empty section id 실패")
	var m := _new_manager()
	var r := m.register_section(_make(&""))
	_check("C.ok", r["ok"], false)
	_check("C.reason", r["reason"], &"section_id_empty")


func _test_register_invalid_version() -> void:
	print("[D] invalid section_version 실패")
	var m := _new_manager()
	var r := m.register_section(_make(&"badver", 0, true, 0))
	_check("D.ok", r["ok"], false)
	_check("D.reason", r["reason"], &"section_version_invalid")


func _test_discover_helper() -> void:
	print("[E] subtree discovery helper")
	var m := _new_manager()
	var container := Node.new()
	add_child(container)
	var a := FakeSection.new(); a.section_id = &"e_a"; container.add_child(a)
	var mid := Node.new(); container.add_child(mid)
	var b := FakeSection.new(); b.section_id = &"e_b"; mid.add_child(b)  # 중첩
	var r := m.discover_sections(container)
	_check("E.ok", r["ok"], true)
	_check("E.discovered_count", r["discovered"].size(), 2)
	_check("E.has_a", m.has_section(&"e_a"), true)
	_check("E.has_b_nested", m.has_section(&"e_b"), true)


func _test_ordering_deterministic() -> void:
	print("[F] deterministic ordering (restore_order, 그다음 id lexical)")
	var m := _new_manager()
	# 등록 순서를 일부러 뒤섞는다.
	m.register_section(_make(&"zeta", 10))
	m.register_section(_make(&"beta", 5))
	m.register_section(_make(&"alpha", 5))   # beta와 같은 order -> id lexical
	m.register_section(_make(&"gamma", 1))
	var ids := m.get_section_ids()
	_check("F.order", str(ids), str([&"gamma", &"alpha", &"beta", &"zeta"]))


func _test_capture_envelope_and_json() -> void:
	print("[G] capture envelope + JSON 호환")
	var m := _new_manager()
	var s1 := _make(&"world", 0, true, 2)
	s1.payload_data = {"affinity": 50, "name": "noabel", "flag": true}
	var s2 := _make(&"quest", 1, true, 1)
	s2.payload_data = {"stage": 3}
	m.register_section(s1)
	m.register_section(s2)
	var r := m.capture_all()
	_check("G.ok", r["ok"], true)
	var env: Dictionary = r["envelope"]
	_check("G.save_version", env["save_version"], 1)
	_check("G.section_count", env["sections"].size(), 2)
	_check("G.world_version", env["sections"]["world"]["section_version"], 2)
	_check("G.world_payload", env["sections"]["world"]["payload"]["affinity"], 50)
	# JSON 왕복.
	var text := JSON.stringify(env)
	var parsed = JSON.parse_string(text)
	_check_true("G.json_not_null", parsed != null)
	_check("G.json_roundtrip_world", parsed["sections"]["world"]["payload"]["name"], "noabel")
	_check("G.json_save_version", int(parsed["save_version"]), 1)


func _test_capture_failure_no_envelope() -> void:
	print("[H] capture 실패 시 envelope 미생성")
	var m := _new_manager()
	m.register_section(_make(&"good"))
	var bad := _make(&"bad")
	bad.capture_ok = false
	m.register_section(bad)
	var r := m.capture_all()
	_check("H.ok", r["ok"], false)
	_check("H.reason", r["reason"], &"capture_failed")
	_check("H.failed_id", r["section_id"], &"bad")
	_check_true("H.no_envelope", not r.has("envelope"))


func _test_validate_required_missing() -> void:
	print("[I] required missing 실패")
	var m := _new_manager()
	m.register_section(_make(&"world"))
	m.register_section(_make(&"quest"))
	var env := {"save_version": 1, "sections": {
		"world": {"section_version": 1, "payload": {}},
	}}
	var v := m.validate_envelope(env)
	_check("I.ok", v["ok"], false)
	_check("I.reason", v["reason"], &"validation_failed")
	_check_true("I.missing_quest", v["missing_required"].has(&"quest"))


func _test_validate_save_version_mismatch() -> void:
	print("[J] save_version mismatch 실패")
	var m := _new_manager()
	m.register_section(_make(&"world"))
	var env := {"save_version": 999, "sections": {
		"world": {"section_version": 1, "payload": {}},
	}}
	var v := m.validate_envelope(env)
	_check("J.ok", v["ok"], false)
	_check("J.reason", v["reason"], &"save_version_mismatch")


func _test_validate_section_version_mismatch() -> void:
	print("[K] section_version mismatch 실패 (required)")
	var m := _new_manager()
	m.register_section(_make(&"world", 0, true, 2))  # 현재 v2
	var env := {"save_version": 1, "sections": {
		"world": {"section_version": 1, "payload": {}},  # 저장은 v1
	}}
	var v := m.validate_envelope(env)
	_check("K.ok", v["ok"], false)
	var found := false
	for e in v["errors"]:
		if e["section_id"] == &"world" and e["reason"] == &"section_version_mismatch":
			found = true
	_check_true("K.has_mismatch_error", found)


func _test_validate_unknown_ignored() -> void:
	print("[L] unknown saved section은 ignored로 report(실패 아님)")
	var m := _new_manager()
	m.register_section(_make(&"world"))
	var env := {"save_version": 1, "sections": {
		"world": {"section_version": 1, "payload": {}},
		"legacy_inventory": {"section_version": 1, "payload": {"items": 3}},
	}}
	var v := m.validate_envelope(env)
	_check("L.ok", v["ok"], true)
	_check_true("L.ignored_has_legacy", v["ignored_sections"].has(&"legacy_inventory"))
	_check("L.plan_size", v["plan"].size(), 1)


func _test_restore_zero_on_validation_fail() -> void:
	print("[M] validate 실패 시 restore 0회")
	var m := _new_manager()
	var log: Array = []
	var world := _make(&"world")
	world.call_log = log
	var quest := _make(&"quest")  # required, 저장에 없음 -> validate 실패
	quest.call_log = log
	m.register_section(world)
	m.register_section(quest)
	var env := {"save_version": 1, "sections": {
		"world": {"section_version": 1, "payload": {}},
	}}
	var r := m.restore_all(env)
	_check("M.ok", r["ok"], false)
	_check("M.reason", r["reason"], &"validation_failed")
	_check("M.restored_count", r["restored_sections"].size(), 0)
	# restore_save가 한 번도 호출되지 않았는지(validate 중 fail).
	var restore_calls := 0
	for entry in log:
		if (entry as String).begins_with("restore:"):
			restore_calls += 1
	_check("M.restore_calls", restore_calls, 0)


func _test_restore_order_deterministic() -> void:
	print("[N] restore order deterministic")
	var m := _new_manager()
	var log: Array = []
	var z := _make(&"zeta", 10); z.call_log = log
	var b := _make(&"beta", 5); b.call_log = log
	var a := _make(&"alpha", 5); a.call_log = log   # beta와 같은 order
	var g := _make(&"gamma", 1); g.call_log = log
	m.register_section(z)
	m.register_section(b)
	m.register_section(a)
	m.register_section(g)
	var cap := m.capture_all()
	_check("N.capture_ok", cap["ok"], true)
	var r := m.restore_all(cap["envelope"])
	_check("N.ok", r["ok"], true)
	var restore_order: Array = []
	for entry in log:
		if (entry as String).begins_with("restore:"):
			restore_order.append((entry as String).trim_prefix("restore:"))
	_check("N.order", str(restore_order), str(["gamma", "alpha", "beta", "zeta"]))
	_check("N.restored_ids", str(r["restored_sections"]),
		str([&"gamma", &"alpha", &"beta", &"zeta"]))


func _test_restore_partial_report() -> void:
	print("[O] restore 중간 실패 시 partial_restore report")
	var m := _new_manager()
	var log: Array = []
	var a := _make(&"a_first", 0); a.call_log = log
	var b := _make(&"b_fails", 1); b.call_log = log; b.restore_ok = false
	var c := _make(&"c_last", 2); c.call_log = log
	m.register_section(a)
	m.register_section(b)
	m.register_section(c)
	var cap := m.capture_all()
	var r := m.restore_all(cap["envelope"])
	_check("O.ok", r["ok"], false)
	_check("O.reason", r["reason"], &"partial_restore")
	_check_true("O.a_restored", r["restored_sections"].has(&"a_first"))
	_check("O.failed_section", r["failed_section"], &"b_fails")
	# c_last는 restore되지 않아야 한다(즉시 중단).
	_check_true("O.c_not_restored", not r["restored_sections"].has(&"c_last"))
	var c_restored := false
	for entry in log:
		if entry == "restore:c_last":
			c_restored = true
	_check_true("O.c_restore_not_called", not c_restored)


func _test_optional_missing_ok() -> void:
	print("[P] 비-required section은 save에 없어도 통과")
	var m := _new_manager()
	m.register_section(_make(&"world", 0, true))
	m.register_section(_make(&"hints", 1, false))  # optional
	var env := {"save_version": 1, "sections": {
		"world": {"section_version": 1, "payload": {}},
	}}
	var v := m.validate_envelope(env)
	_check("P.ok", v["ok"], true)
	_check("P.plan_size", v["plan"].size(), 1)


func _test_busy_guard_reentrant_capture() -> void:
	print("[Q] capture 재진입 busy guard")
	var m := _new_manager()
	var s := ReentrantCaptureSection.new()
	s.section_id = &"reentrant"
	s.manager = m
	add_child(s)
	m.register_section(s)
	var r := m.capture_all()
	_check("Q.outer_ok", r["ok"], true)
	_check("Q.inner_ok", s.reentry["ok"], false)
	_check("Q.inner_reason", s.reentry["reason"], &"busy")
	_check("Q.not_busy_after", m.is_busy(), false)


func _test_busy_guard_reentrant_restore() -> void:
	print("[R] restore 재진입 busy guard")
	var m := _new_manager()
	var s := ReentrantRestoreSection.new()
	s.section_id = &"reentrant"
	s.manager = m
	s.envelope = {"save_version": 1, "sections": {
		"reentrant": {"section_version": 1, "payload": {}},
	}}
	add_child(s)
	m.register_section(s)
	var r := m.restore_all(s.envelope)
	_check("R.outer_ok", r["ok"], true)
	_check("R.inner_ok", s.reentry["ok"], false)
	_check("R.inner_reason", s.reentry["reason"], &"busy")
	_check("R.not_busy_after", m.is_busy(), false)


func _test_capture_rejects_non_json_payload() -> void:
	print("[S] capture: non-JSON payload 거부 (StringName / non-String key)")
	# S1: StringName 값.
	var m1 := _new_manager()
	var s1 := _make(&"sn")
	s1.payload_data = {"mood": &"calm"}  # StringName 값은 JSON 왕복에서 String으로 변형
	m1.register_section(s1)
	var r1 := m1.capture_all()
	_check("S1.ok", r1["ok"], false)
	_check("S1.reason", r1["reason"], &"payload_not_json_compatible")
	_check("S1.id", r1["section_id"], &"sn")
	_check_true("S1.no_envelope", not r1.has("envelope"))

	# S2: non-String Dictionary key.
	var m2 := _new_manager()
	var s2 := _make(&"badkey")
	s2.payload_data = {123: "x"}  # int key는 JSON에서 String으로 변형
	m2.register_section(s2)
	var r2 := m2.capture_all()
	_check("S2.ok", r2["ok"], false)
	_check("S2.reason", r2["reason"], &"payload_not_json_compatible")

	# S3: 중첩 Array 안의 StringName.
	var m3 := _new_manager()
	var s3 := _make(&"nested")
	s3.payload_data = {"list": [1, 2, &"sn"]}
	m3.register_section(s3)
	var r3 := m3.capture_all()
	_check("S3.ok", r3["ok"], false)
	_check("S3.reason", r3["reason"], &"payload_not_json_compatible")


func _test_capture_rejects_int_overflow() -> void:
	print("[T] capture: JSON-safe 범위 밖 int 거부")
	var m := _new_manager()
	var s := _make(&"big")
	s.payload_data = {"n": 9007199254740993}  # 2^53 + 1
	m.register_section(s)
	var r := m.capture_all()
	_check("T.ok", r["ok"], false)
	_check("T.reason", r["reason"], &"payload_not_json_compatible")


func _test_capture_accepts_nested_json() -> void:
	print("[U] capture: 유효 중첩 JSON payload는 통과")
	var m := _new_manager()
	var s := _make(&"ok")
	s.payload_data = {
		"i": 7, "f": 1.5, "b": true, "s": "txt", "nil": null,
		"arr": [1, "a", false, {"k": 2}],
		"dict": {"nested": {"deep": [3.0, "x"]}},
	}
	m.register_section(s)
	var r := m.capture_all()
	_check("U.ok", r["ok"], true)
	# JSON 왕복도 성공해야 한다.
	var parsed = JSON.parse_string(JSON.stringify(r["envelope"]))
	_check_true("U.json_not_null", parsed != null)


func _test_validate_rejects_non_json_payload() -> void:
	print("[V] validate_envelope: non-JSON payload 거부 (required)")
	var m := _new_manager()
	m.register_section(_make(&"world"))
	# 손상/조작된 envelope: payload에 StringName.
	var env := {"save_version": 1, "sections": {
		"world": {"section_version": 1, "payload": {"mood": &"calm"}},
	}}
	var v := m.validate_envelope(env)
	_check("V.ok", v["ok"], false)
	var found := false
	for e in v["errors"]:
		if e["section_id"] == &"world" and e["reason"] == &"payload_not_json_compatible":
			found = true
	_check_true("V.has_error", found)
	# validate 실패이므로 restore 0회.
	_check("V.plan_empty", v["plan"].size(), 0)


func _test_revalidate_duplicate_after_register() -> void:
	print("[W] 등록 후 id 변경으로 중복 발생 시 capture/validate 거부")
	var m := _new_manager()
	var a := _make(&"a_orig", 0)
	var b := _make(&"b_orig", 1)
	m.register_section(a)
	m.register_section(b)
	# 등록 후 b의 id를 a와 같게 바꿔 live 중복을 만든다(export var 경로).
	b.section_id = &"a_orig"
	var cap := m.capture_all()
	_check("W.capture_ok", cap["ok"], false)
	_check("W.capture_reason", cap["reason"], &"sections_invalid")
	var env := {"save_version": 1, "sections": {}}
	var v := m.validate_envelope(env)
	_check("W.validate_ok", v["ok"], false)
	_check("W.validate_reason", v["reason"], &"sections_invalid")


func _test_revalidate_empty_id_after_register() -> void:
	print("[X] 등록 후 id를 빈 값으로 바꾸면 capture 거부")
	var m := _new_manager()
	var a := _make(&"a_orig", 0)
	m.register_section(a)
	a.section_id = &""  # 등록 후 빈 id로 변경
	var cap := m.capture_all()
	_check("X.ok", cap["ok"], false)
	_check("X.reason", cap["reason"], &"sections_invalid")
	var found := false
	for e in cap["errors"]:
		if e["reason"] == &"section_id_empty":
			found = true
	_check_true("X.has_empty_error", found)


func _test_revalidate_unique_id_change() -> void:
	print("[Y] 등록 후 다른 고유 id로 변경 시 capture/validate/restore 거부 (lookup miss/SCRIPT ERROR 방지)")
	var m := _new_manager()
	var s := _make(&"orig", 0)
	m.register_section(s)
	# 변경 전 정상 envelope 확보.
	var cap_before := m.capture_all()
	_check("Y.cap_before_ok", cap_before["ok"], true)
	# 등록 후 빈/중복이 아닌 고유한 새 id로 변경한다(_sections key와 갈라짐).
	s.section_id = &"renamed"
	# capture 거부 + section_id_changed.
	var cap := m.capture_all()
	_check("Y.capture_ok", cap["ok"], false)
	_check("Y.capture_reason", cap["reason"], &"sections_invalid")
	var found := false
	for e in cap["errors"]:
		if e["reason"] == &"section_id_changed":
			found = true
	_check_true("Y.has_changed_error", found)
	# validate 거부.
	var v := m.validate_envelope(cap_before["envelope"])
	_check("Y.validate_ok", v["ok"], false)
	_check("Y.validate_reason", v["reason"], &"sections_invalid")
	# restore 거부: plan loop에 진입하지 않아 _sections[live_id] lookup miss가 발생하지 않는다.
	var r := m.restore_all(cap_before["envelope"])
	_check("Y.restore_ok", r["ok"], false)
	_check("Y.restore_reason", r["reason"], &"validation_failed")
	_check("Y.restore_count", r["restored_sections"].size(), 0)


func _test_section_version_must_be_integral() -> void:
	print("[Z] section_version은 정수형 number만 허용 (1.5/\"1\"/null → malformed_section)")
	# 정수형 FLOAT(1.0)은 JSON round-trip이므로 통과해야 한다.
	var m0 := _new_manager()
	m0.register_section(_make(&"world", 0, true, 1))
	var ok_env := {"save_version": 1, "sections": {
		"world": {"section_version": 1.0, "payload": {}},
	}}
	var v0 := m0.validate_envelope(ok_env)
	_check("Z.float_int_ok", v0["ok"], true)

	# 비정수 FLOAT 1.5 → malformed_section, restore 0.
	var m1 := _new_manager()
	var sec := _make(&"world", 0, true, 1)
	sec.call_log = []
	m1.register_section(sec)
	var bad_float := {"save_version": 1, "sections": {
		"world": {"section_version": 1.5, "payload": {}},
	}}
	var v1 := m1.validate_envelope(bad_float)
	_check("Z.float_bad_ok", v1["ok"], false)
	var found_mf := false
	for e in v1["errors"]:
		if e["section_id"] == &"world" and e["reason"] == &"malformed_section":
			found_mf = true
	_check_true("Z.float_malformed", found_mf)
	var r1 := m1.restore_all(bad_float)
	_check("Z.float_restore_ok", r1["ok"], false)
	_check("Z.float_restore_count", r1["restored_sections"].size(), 0)

	# string "1" → malformed_section(int() 강제 변환 우회 차단).
	var m2 := _new_manager()
	m2.register_section(_make(&"world", 0, true, 1))
	var str_ver := {"save_version": 1, "sections": {
		"world": {"section_version": "1", "payload": {}},
	}}
	var v2 := m2.validate_envelope(str_ver)
	_check("Z.string_ok", v2["ok"], false)
	var found_mf2 := false
	for e in v2["errors"]:
		if e["reason"] == &"malformed_section":
			found_mf2 = true
	_check_true("Z.string_malformed", found_mf2)
