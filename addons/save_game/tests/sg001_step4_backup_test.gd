# SG-001 Step 4 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/save_game/tests/sg001_step4_backup_test.tscn
#
# 검증 범위(SG-001 Step 4 완료 조건):
# - overwrite 시 .bak 생성 + bak이 직전 good 내용을 보존
# - 첫 save에는 bak 없음
# - primary corrupt + backup valid -> bak에서 복구 report
# - primary missing + backup valid -> bak에서 복구(크래시 시뮬)
# - primary corrupt + backup corrupt -> 실패(restore 0회)
# - delete_slot이 primary와 .bak을 모두 제거
extends Node

const PREFIX := "sg001s4_"

var _failures: int = 0


func _ready() -> void:
	_cleanup_test_slots()

	_test_bak_created_on_overwrite()
	_test_no_bak_on_first_save()
	_test_recover_from_backup_when_primary_corrupt()
	_test_recover_from_backup_when_primary_missing()
	_test_both_corrupt_fails()
	_test_delete_removes_backup()
	_test_corrupt_primary_does_not_clobber_good_bak()
	_test_primary_missing_bak_corrupt_reports_cause()

	_cleanup_test_slots()

	if _failures == 0:
		print("[SG-001 Step4] ALL PASS")
		get_tree().quit(0)
	else:
		print("[SG-001 Step4] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- fake section -----------------------------------------------------

class FakeSection extends SaveSection:
	var payload_data: Dictionary = {}
	var last_restored: Variant = null

	func capture_save() -> Dictionary:
		return {"ok": true, "payload": payload_data.duplicate(true), "reason": &""}

	func validate_save(_p: Dictionary) -> Dictionary:
		return {"ok": true, "reason": &""}

	func restore_save(p: Dictionary) -> Dictionary:
		last_restored = p.duplicate(true)
		return {"ok": true, "reason": &""}


# --- helpers ----------------------------------------------------------

func _setup(id: StringName, payload: Dictionary) -> Array:
	var m := SaveGameManager.new()
	add_child(m)
	var sec := FakeSection.new()
	sec.section_id = id
	sec.payload_data = payload
	add_child(sec)
	m.register_section(sec)
	return [m, sec]


func _slot(name: String) -> String:
	return PREFIX + name


func _primary(sid: String) -> String:
	return SaveGameManager.SAVES_DIR + "/" + sid + ".json"


func _bak(sid: String) -> String:
	return SaveGameManager.SAVES_DIR + "/" + sid + ".json.bak"


func _write_raw(path: String, text: String) -> void:
	DirAccess.make_dir_recursive_absolute(SaveGameManager.SAVES_DIR)
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_string(text)
	f.close()


func _read_json(path: String) -> Variant:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return null
	var t := f.get_as_text()
	f.close()
	return JSON.parse_string(t)


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

func _test_bak_created_on_overwrite() -> void:
	print("[A] overwrite 시 .bak 생성 + 직전 내용 보존")
	var pair := _setup(&"world", {"v": 1})
	var m: SaveGameManager = pair[0]
	var sec: FakeSection = pair[1]
	var sid := _slot("ow")
	m.save_slot(sid)  # v1
	sec.payload_data = {"v": 2}
	m.save_slot(sid)  # v2 -> bak는 v1

	_check_true("A.bak_exists", FileAccess.file_exists(_bak(sid)))
	_check_true("A.primary_exists", FileAccess.file_exists(_primary(sid)))
	var prim = _read_json(_primary(sid))
	var bak = _read_json(_bak(sid))
	_check("A.primary_v", int(prim["sections"]["world"]["payload"]["v"]), 2)
	_check("A.bak_v", int(bak["sections"]["world"]["payload"]["v"]), 1)


func _test_no_bak_on_first_save() -> void:
	print("[B] 첫 save에는 .bak 없음")
	var pair := _setup(&"world", {"v": 1})
	var m: SaveGameManager = pair[0]
	var sid := _slot("first")
	m.save_slot(sid)
	_check_true("B.primary_exists", FileAccess.file_exists(_primary(sid)))
	_check_true("B.no_bak", not FileAccess.file_exists(_bak(sid)))


func _test_recover_from_backup_when_primary_corrupt() -> void:
	print("[C] primary 손상 + backup 유효 -> bak에서 복구")
	var pair := _setup(&"world", {"v": 1})
	var m: SaveGameManager = pair[0]
	var sec: FakeSection = pair[1]
	var sid := _slot("recover")
	m.save_slot(sid)            # v1
	sec.payload_data = {"v": 2}
	m.save_slot(sid)            # v2, bak=v1
	# primary 손상.
	_write_raw(_primary(sid), "{ broken json ")
	sec.last_restored = null

	var r := m.load_slot(sid)
	_check("C.load_ok", r["ok"], true)
	_check("C.recovered", r["recovered_from_backup"], true)
	_check("C.source", r["source"], &"backup")
	# bak 내용(v1)이 복원돼야 한다.
	_check("C.restored_v", int(sec.last_restored["v"]), 1)


func _test_recover_from_backup_when_primary_missing() -> void:
	print("[D] primary 없음 + backup 유효 -> bak에서 복구(크래시 시뮬)")
	var pair := _setup(&"world", {"v": 1})
	var m: SaveGameManager = pair[0]
	var sec: FakeSection = pair[1]
	var sid := _slot("missing")
	m.save_slot(sid)            # v1
	sec.payload_data = {"v": 2}
	m.save_slot(sid)            # v2, bak=v1
	# primary 제거(primary→bak rename 후 tmp→primary 직전 크래시 시뮬).
	DirAccess.open(SaveGameManager.SAVES_DIR).remove(sid + ".json")
	_check_true("D.primary_gone", not FileAccess.file_exists(_primary(sid)))
	sec.last_restored = null

	var r := m.load_slot(sid)
	_check("D.load_ok", r["ok"], true)
	_check("D.recovered", r["recovered_from_backup"], true)
	_check("D.restored_v", int(sec.last_restored["v"]), 1)


func _test_both_corrupt_fails() -> void:
	print("[E] primary 손상 + backup 손상 -> 실패(restore 0회)")
	var pair := _setup(&"world", {"v": 1})
	var m: SaveGameManager = pair[0]
	var sec: FakeSection = pair[1]
	var sid := _slot("both_bad")
	m.save_slot(sid)
	sec.payload_data = {"v": 2}
	m.save_slot(sid)
	_write_raw(_primary(sid), "{ broken ")
	_write_raw(_bak(sid), "{ also broken ")
	sec.last_restored = null

	var r := m.load_slot(sid)
	_check("E.load_ok", r["ok"], false)
	_check("E.recovered", r["recovered_from_backup"], false)
	_check_true("E.error", r["error"] == &"parse_error" or r["error"] == &"corrupt")
	# restore가 시작되지 않았다.
	_check("E.no_restore", sec.last_restored, null)
	_check_true("E.no_restore_key", not r.has("restore"))


func _test_delete_removes_backup() -> void:
	print("[F] delete_slot이 primary와 .bak 모두 제거")
	var pair := _setup(&"world", {"v": 1})
	var m: SaveGameManager = pair[0]
	var sec: FakeSection = pair[1]
	var sid := _slot("del")
	m.save_slot(sid)
	sec.payload_data = {"v": 2}
	m.save_slot(sid)  # bak 생성
	_check_true("F.bak_before", FileAccess.file_exists(_bak(sid)))

	var r := m.delete_slot(sid)
	_check("F.delete_ok", r["ok"], true)
	_check_true("F.primary_gone", not FileAccess.file_exists(_primary(sid)))
	_check_true("F.bak_gone", not FileAccess.file_exists(_bak(sid)))
	_check("F.has_slot", m.has_slot(sid), false)


func _test_corrupt_primary_does_not_clobber_good_bak() -> void:
	print("[G] 손상 primary로 save해도 유효한 .bak을 덮지 않음(데이터 손실 방지)")
	var pair := _setup(&"world", {"v": 1})
	var m: SaveGameManager = pair[0]
	var sec: FakeSection = pair[1]
	var sid := _slot("clobber")
	m.save_slot(sid)            # v1
	sec.payload_data = {"v": 2}
	m.save_slot(sid)            # v2, bak=v1(good)
	# primary 손상.
	_write_raw(_primary(sid), "{ broken json ")
	# 손상 상태에서 다시 save(v3): 손상 primary는 .bak으로 회전되면 안 된다.
	sec.payload_data = {"v": 3}
	var sr := m.save_slot(sid)
	_check("G.save_ok", sr["ok"], true)
	# .bak은 여전히 good(v1)이어야 한다.
	var bak = _read_json(_bak(sid))
	_check_true("G.bak_valid", bak != null and bak is Dictionary)
	_check("G.bak_still_good", int(bak["sections"]["world"]["payload"]["v"]), 1)
	# 새 primary는 v3.
	var prim = _read_json(_primary(sid))
	_check("G.primary_v3", int(prim["sections"]["world"]["payload"]["v"]), 3)
	# 안전망 확인: primary를 다시 손상시키면 bak(v1)으로 복구된다.
	_write_raw(_primary(sid), "{ broken again ")
	sec.last_restored = null
	var lr := m.load_slot(sid)
	_check("G.recover_ok", lr["ok"], true)
	_check("G.recover_v1", int(sec.last_restored["v"]), 1)


func _test_primary_missing_bak_corrupt_reports_cause() -> void:
	print("[H] primary 없음 + bak 손상 -> 실제 원인 보고(read_failed 기본값 아님)")
	var pair := _setup(&"world", {"v": 1})
	var m: SaveGameManager = pair[0]
	var sec: FakeSection = pair[1]
	var sid := _slot("miss_badbak")
	# primary 없이 손상된 bak만 만든다.
	_write_raw(_bak(sid), "{ broken bak ")
	sec.last_restored = null
	var lr := m.load_slot(sid)
	_check("H.load_ok", lr["ok"], false)
	_check("H.recovered", lr["recovered_from_backup"], false)
	_check("H.error", lr["error"], &"parse_error")
	_check("H.no_restore", sec.last_restored, null)
