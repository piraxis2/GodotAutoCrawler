# SG-001 Step 2 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/world_core/save_game/tests/sg001_step2_slot_store_test.tscn
#
# 검증 범위(SG-001 Step 2 완료 조건):
# - slot_id validation(^[a-zA-Z0-9_-]{1,64}$)
# - save_slot/load_slot 왕복 + section payload 보존
# - atomic write(tmp+rename) overwrite(Windows atomic replace)
# - created_at 보존 / updated_at 갱신
# - missing/corrupt 파일 실패 report(크래시 없음)
# - list_slots 메타 읽기 + per-slot corrupt isolation
# - delete_slot / has_slot
# - capture 실패 시 파일 미작성
# - 디스크 파일이 유효 JSON
#
# 주의: Godot JSON.parse_string은 모든 number를 float로 읽는다(`7`->`7.0`). JSON은 int/float를
# 구분하지 않으므로 core는 JSON number를 그대로 돌려준다. int 의미가 필요한 section은 자기 restore_save에서
# 정규화한다(WorldState adapter=Step 3). 따라서 number 비교는 int()로 수치 동등성을 확인한다.
extends Node

const PREFIX := "sg001s2_"

var _failures: int = 0


func _ready() -> void:
	_cleanup_test_slots()

	_test_slot_id_validation()
	_test_save_creates_file()
	_test_roundtrip_restore()
	_test_overwrite_atomic_replace()
	_test_created_preserved_updated_changes()
	_test_load_missing()
	_test_load_corrupt()
	_test_list_slots_and_corrupt_isolation()
	_test_delete_and_has_slot()
	_test_capture_failure_no_file()
	_test_file_is_valid_json()
	_test_list_structurally_corrupt()

	_cleanup_test_slots()

	if _failures == 0:
		print("[SG-001 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[SG-001 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- fake section -----------------------------------------------------

class FakeSection extends SaveSection:
	var capture_ok: bool = true
	var payload_data: Dictionary = {}
	var last_restored: Dictionary = {}

	func capture_save() -> Dictionary:
		if not capture_ok:
			return {"ok": false, "reason": &"fake_capture_fail"}
		return {"ok": true, "payload": payload_data.duplicate(true), "reason": &""}

	func validate_save(_p: Dictionary) -> Dictionary:
		return {"ok": true, "reason": &""}

	func restore_save(p: Dictionary) -> Dictionary:
		last_restored = p.duplicate(true)
		return {"ok": true, "reason": &""}


# --- helpers ----------------------------------------------------------

func _make(id: StringName, payload: Dictionary = {}) -> FakeSection:
	var s := FakeSection.new()
	s.section_id = id
	s.payload_data = payload
	add_child(s)
	return s


func _new_manager() -> SaveGameManager:
	var m := SaveGameManager.new()
	add_child(m)
	return m


func _slot(name: String) -> String:
	return PREFIX + name


func _slot_file(sid: String) -> String:
	return SaveGameManager.SAVES_DIR + "/" + sid + ".json"


func _write_raw(sid: String, text: String) -> void:
	DirAccess.make_dir_recursive_absolute(SaveGameManager.SAVES_DIR)
	var f := FileAccess.open(_slot_file(sid), FileAccess.WRITE)
	f.store_string(text)
	f.close()


func _read_raw(sid: String) -> String:
	var f := FileAccess.open(_slot_file(sid), FileAccess.READ)
	if f == null:
		return ""
	var t := f.get_as_text()
	f.close()
	return t


func _cleanup_test_slots() -> void:
	if not DirAccess.dir_exists_absolute(SaveGameManager.SAVES_DIR):
		return
	var d := DirAccess.open(SaveGameManager.SAVES_DIR)
	if d == null:
		return
	for fname in d.get_files():
		if fname.begins_with(PREFIX):
			d.remove(fname)


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# --- 시나리오 ---------------------------------------------------------

func _test_slot_id_validation() -> void:
	print("[A] slot_id validation")
	var m := _new_manager()
	m.register_section(_make(&"s", {"x": 1}))
	# 잘못된 id는 capture/파일 IO 없이 invalid_slot_id.
	_check("A.empty", m.save_slot("")["error"], &"invalid_slot_id")
	_check("A.space", m.save_slot("bad id")["error"], &"invalid_slot_id")
	_check("A.slash", m.save_slot("a/b")["error"], &"invalid_slot_id")
	_check("A.dot", m.save_slot("a.b")["error"], &"invalid_slot_id")
	var long_id := ""
	for i in 65:
		long_id += "a"
	_check("A.too_long", m.save_slot(long_id)["error"], &"invalid_slot_id")
	# 유효 id는 통과.
	var ok := m.save_slot(_slot("valid_1"))
	_check("A.valid_ok", ok["ok"], true)
	# 64자 경계 허용.
	var id64 := ""
	for i in 64:
		id64 += "b"
	# 64자라도 PREFIX를 안 붙이고 정확히 64자로 테스트(정리 위해 별도 prefix 미사용은 cleanup이 못 잡으니
	# 여기선 검증만 하고 즉시 삭제).
	var ok64 := m.save_slot(id64)
	_check("A.len64_ok", ok64["ok"], true)
	m.delete_slot(id64)


func _test_save_creates_file() -> void:
	print("[B] save_slot 파일 생성 + has_slot")
	var m := _new_manager()
	m.register_section(_make(&"world", {"affinity": 50}))
	var sid := _slot("create")
	var r := m.save_slot(sid)
	_check("B.ok", r["ok"], true)
	_check("B.slot_id", r["slot_id"], StringName(sid))
	_check("B.error_empty", r["error"], &"")
	_check_true("B.file_exists", FileAccess.file_exists(_slot_file(sid)))
	_check("B.has_slot", m.has_slot(sid), true)
	_check_true("B.no_tmp_left", not FileAccess.file_exists(SaveGameManager.SAVES_DIR + "/" + sid + ".json.tmp"))


func _test_roundtrip_restore() -> void:
	print("[C] save -> load 왕복 section payload 보존")
	var m := _new_manager()
	var sec := _make(&"world", {"affinity": 50, "name": "noabel", "flags": [true, false]})
	m.register_section(sec)
	var sid := _slot("rt")
	_check("C.save_ok", m.save_slot(sid)["ok"], true)
	# restore 전에 section 값을 비워 load가 실제로 복원하는지 본다.
	sec.last_restored = {}
	var r := m.load_slot(sid)
	_check("C.load_ok", r["ok"], true)
	# JSON round-trip: int 50 -> float 50.0(수치 동등). String/bool은 그대로 보존.
	_check("C.restored_affinity", int(sec.last_restored["affinity"]), 50)
	_check("C.restored_name", sec.last_restored["name"], "noabel")
	_check("C.restored_flags", str(sec.last_restored["flags"]), str([true, false]))


func _test_overwrite_atomic_replace() -> void:
	print("[D] overwrite atomic replace (Windows rename-over)")
	var m := _new_manager()
	var sec := _make(&"world", {"v": 1})
	m.register_section(sec)
	var sid := _slot("ow")
	_check("D.save1_ok", m.save_slot(sid)["ok"], true)
	# payload 변경 후 다시 저장 -> 파일이 새 값으로 교체돼야 한다.
	sec.payload_data = {"v": 2}
	var r2 := m.save_slot(sid)
	_check("D.save2_ok", r2["ok"], true)
	var parsed = JSON.parse_string(_read_raw(sid))
	_check("D.file_v", int(parsed["sections"]["world"]["payload"]["v"]), 2)


func _test_created_preserved_updated_changes() -> void:
	print("[E] created_at 보존 / updated_at 갱신")
	var m := _new_manager()
	m.register_section(_make(&"world", {"x": 1}))
	var sid := _slot("ts")
	_check("E.save1_ok", m.save_slot(sid)["ok"], true)
	# 파일의 created_at_unix를 알려진 과거 값으로 바꿔 보존을 검증한다.
	var env = JSON.parse_string(_read_raw(sid))
	env["created_at_unix"] = 11111
	env["updated_at_unix"] = 11111
	_write_raw(sid, JSON.stringify(env))
	# 다시 저장.
	_check("E.save2_ok", m.save_slot(sid)["ok"], true)
	var env2 = JSON.parse_string(_read_raw(sid))
	_check("E.created_preserved", int(env2["created_at_unix"]), 11111)
	_check_true("E.updated_changed", int(env2["updated_at_unix"]) != 11111)


func _test_load_missing() -> void:
	print("[F] 없는 slot load")
	var m := _new_manager()
	m.register_section(_make(&"world", {"x": 1}))
	var r := m.load_slot(_slot("nope"))
	_check("F.ok", r["ok"], false)
	_check("F.error", r["error"], &"slot_not_found")


func _test_load_corrupt() -> void:
	print("[G] 손상 파일 load (크래시 없이 실패 report)")
	var m := _new_manager()
	m.register_section(_make(&"world", {"x": 1}))
	var sid := _slot("corrupt")
	_write_raw(sid, "{ this is not valid json ")
	var r := m.load_slot(sid)
	_check("G.ok", r["ok"], false)
	_check("G.error", r["error"], &"parse_error")
	# 배열 등 Dictionary가 아닌 JSON은 corrupt.
	_write_raw(sid, "[1,2,3]")
	var r2 := m.load_slot(sid)
	_check("G.array_error", r2["error"], &"corrupt")


func _test_list_slots_and_corrupt_isolation() -> void:
	print("[H] list_slots 메타 + per-slot corrupt isolation")
	var m := _new_manager()
	m.register_section(_make(&"world", {"x": 1}))
	var a := _slot("list_a")
	var b := _slot("list_b")
	var c := _slot("list_corrupt")
	m.save_slot(a, {"display_name": "Slot A", "play_time_seconds": 120})
	m.save_slot(b, {"display_name": "Slot B"})
	_write_raw(c, "{ broken ")  # 손상
	var all := m.list_slots()
	# PREFIX로 시작하는 list_ 항목만 필터.
	var mine: Dictionary = {}
	for e in all:
		var s := String(e["slot_id"])
		if s.begins_with(_slot("list_")):
			mine[s] = e
	_check("H.count", mine.size(), 3)
	_check("H.a_ok", mine[a]["ok"], true)
	_check("H.a_meta", mine[a]["metadata"]["display_name"], "Slot A")
	_check("H.a_playtime", int(mine[a]["metadata"]["play_time_seconds"]), 120)
	_check("H.a_save_version", mine[a]["save_version"], 1)
	_check("H.b_ok", mine[b]["ok"], true)
	# 손상 slot은 ok:false로 보고되지만 나머지는 정상.
	_check("H.corrupt_ok", mine[c]["ok"], false)
	_check_true("H.corrupt_has_error", mine[c]["error"] == &"parse_error" or mine[c]["error"] == &"corrupt")


func _test_delete_and_has_slot() -> void:
	print("[I] delete_slot / has_slot")
	var m := _new_manager()
	m.register_section(_make(&"world", {"x": 1}))
	var sid := _slot("del")
	m.save_slot(sid)
	_check("I.has_before", m.has_slot(sid), true)
	var r := m.delete_slot(sid)
	_check("I.delete_ok", r["ok"], true)
	_check("I.has_after", m.has_slot(sid), false)
	_check_true("I.file_gone", not FileAccess.file_exists(_slot_file(sid)))
	# 없는 slot 삭제는 slot_not_found.
	var r2 := m.delete_slot(sid)
	_check("I.delete_missing", r2["error"], &"slot_not_found")


func _test_capture_failure_no_file() -> void:
	print("[J] capture 실패 시 파일 미작성")
	var m := _new_manager()
	var good := _make(&"good", {"x": 1})
	m.register_section(good)
	var bad := _make(&"bad", {})
	bad.capture_ok = false
	m.register_section(bad)
	var sid := _slot("capfail")
	var r := m.save_slot(sid)
	_check("J.ok", r["ok"], false)
	_check("J.error", r["error"], &"capture_failed")
	_check_true("J.no_file", not FileAccess.file_exists(_slot_file(sid)))


func _test_file_is_valid_json() -> void:
	print("[K] 디스크 파일이 유효 JSON")
	var m := _new_manager()
	m.register_section(_make(&"world", {"affinity": 7, "name": "x", "nested": {"a": [1, 2]}}))
	var sid := _slot("json")
	m.save_slot(sid, {"display_name": "JSON"})
	var text := _read_raw(sid)
	var parsed = JSON.parse_string(text)
	_check_true("K.parses", parsed != null)
	_check_true("K.is_dict", parsed is Dictionary)
	_check("K.save_version", int(parsed["save_version"]), 1)
	_check("K.slot_id", parsed["slot_id"], sid)
	_check("K.section_payload", int(parsed["sections"]["world"]["payload"]["affinity"]), 7)
	_check("K.metadata", parsed["metadata"]["display_name"], "JSON")


func _test_list_structurally_corrupt() -> void:
	print("[L] list_slots: 파싱되지만 구조 손상된 slot 격리")
	var m := _new_manager()
	m.register_section(_make(&"world", {"x": 1}))
	# 유효 JSON Dictionary지만 save_version이 정수형이 아니고 metadata가 String.
	var bad := _slot("struct_bad")
	_write_raw(bad, JSON.stringify({"save_version": "x", "sections": {}, "metadata": "oops"}))
	# 정상 slot 1개도 같이 둔다(격리 확인).
	var good := _slot("struct_good")
	m.save_slot(good, {"display_name": "Good"})

	var all := m.list_slots()
	var found_bad := {}
	var found_good := {}
	for e in all:
		var s := String(e["slot_id"])
		if s == bad:
			found_bad = e
		elif s == good:
			found_good = e
	_check_true("L.bad_present", not found_bad.is_empty())
	_check("L.bad_ok", found_bad["ok"], false)
	_check("L.bad_error", found_bad["error"], &"corrupt")
	# 손상 slot이 정상 slot 나열을 막지 않는다.
	_check("L.good_ok", found_good["ok"], true)
	_check("L.good_meta", found_good["metadata"]["display_name"], "Good")
	# metadata가 Dictionary가 아닌 손상 slot은 load도 malformed_envelope로 거부(save_version 비정수).
	var lr := m.load_slot(bad)
	_check("L.load_ok", lr["ok"], false)
