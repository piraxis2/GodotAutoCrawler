# SG-001 Step 3 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/world_core/save_game_world_state/tests/sg001_step3_world_state_section_test.tscn
#
# 검증 범위(SG-001 Step 3 완료 조건):
# - WorldStateSaveSection을 통한 실제 WorldState SAVE snapshot 파일 저장/복원(slot file 경유 JSON 왕복)
# - new game -> mutate -> save -> mutate -> load -> SAVE restore + SESSION default
# - load validation 실패 시 기존 WorldState 보존(restore 0회)
# - capture-not-ready(store/session) 실패 시 save file 미작성, 빈 payload 미포함
# - restore 중간 실패 시 중단 + partial restore report(world_state는 복원, 이후 section 실패)
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const RUNTIME_SCRIPT := "res://addons/world_core/world_state/world_state_runtime.gd"
const PREFIX := "sg001s3_"

var _failures: int = 0


func _ready() -> void:
	_cleanup_test_slots()

	_test_full_roundtrip()
	_test_load_validation_fail_preserves()
	_test_capture_store_not_ready()
	_test_capture_session_not_ready()
	_test_partial_restore_with_adapter()
	_test_runtime_contract_invalid()

	_cleanup_test_slots()

	if _failures == 0:
		print("[SG-001 Step3] ALL PASS")
		get_tree().quit(0)
	else:
		print("[SG-001 Step3] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- fake section (partial restore용) ---------------------------------

class FakeLateSection extends SaveSection:
	var restore_ok: bool = true

	func capture_save() -> Dictionary:
		return {"ok": true, "payload": {}, "reason": &""}

	func validate_save(_p: Dictionary) -> Dictionary:
		return {"ok": true, "reason": &""}

	func restore_save(_p: Dictionary) -> Dictionary:
		if not restore_ok:
			return {"ok": false, "reason": &"fake_late_fail"}
		return {"ok": true, "reason": &""}


# 계약 메서드 이름은 모두 갖지만 반환 타입이 잘못된 가짜 runtime(duck-type shape 위반 검증).
class BadRuntime extends Node:
	func is_store_ready() -> bool:
		return true

	func is_session_ready() -> bool:
		return true

	func capture_world_state() -> Variant:
		return "not a dict"

	func peek_world_state_compatibility(_s) -> Variant:
		return "nope"

	func restore_world_state(_s) -> Variant:
		return 42


# --- helpers ----------------------------------------------------------

func _def(key: StringName, vtype: int, default_value: Variant, lifetime: int = LT.SAVE) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	d.lifetime = lifetime
	return d


func _make_store() -> WorldStateStore:
	var s := StateSchema.new()
	var defs: Array[StateDefinition] = [
		_def(&"quest.main.stage", VT.INT, 0),
		_def(&"player.health", VT.FLOAT, 100.0),
		_def(&"player.title", VT.STRING, "novice"),
		_def(&"world.build.channel", VT.STRING_NAME, &"dev"),
		_def(&"session.intro.seen", VT.BOOL, false, LT.SESSION),
	]
	s.definitions = defs
	var store := WorldStateStore.new()
	store.schema = s
	store.initialize()
	return store


func _make_runtime(store):
	var rt = load(RUNTIME_SCRIPT).new()
	if store != null:
		rt.set_store(store)
	return rt


func _make_manager_with_section(rt) -> Array:
	var m := SaveGameManager.new()
	add_child(m)
	var sec := WorldStateSaveSection.new()
	sec.set_runtime(rt)
	add_child(sec)
	m.register_section(sec)
	return [m, sec]


func _slot(name: String) -> String:
	return PREFIX + name


func _slot_file(sid: String) -> String:
	return SaveGameManager.SAVES_DIR + "/" + sid + ".json"


func _read_json(sid: String) -> Variant:
	var f := FileAccess.open(_slot_file(sid), FileAccess.READ)
	if f == null:
		return null
	var t := f.get_as_text()
	f.close()
	return JSON.parse_string(t)


func _write_json(sid: String, data: Variant) -> void:
	var f := FileAccess.open(_slot_file(sid), FileAccess.WRITE)
	f.store_string(JSON.stringify(data))
	f.close()


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

func _test_full_roundtrip() -> void:
	print("[A] new game -> mutate -> save -> mutate -> load -> SAVE restore + SESSION default (파일 경유)")
	var store := _make_store()
	var rt = _make_runtime(store)
	var pair := _make_manager_with_section(rt)
	var m: SaveGameManager = pair[0]

	rt.start_new_game()
	store.set_value(&"quest.main.stage", 7)
	store.set_value(&"player.health", 42.5)
	store.set_value(&"player.title", "knight")
	store.set_value(&"world.build.channel", &"prod")
	store.set_value(&"session.intro.seen", true)  # SESSION (저장 안 됨)

	var sid := _slot("rt")
	var sr := m.save_slot(sid)
	_check("A.save_ok", sr["ok"], true)
	_check_true("A.file_exists", FileAccess.file_exists(_slot_file(sid)))

	# 저장 후 상태를 흩뜨린다.
	store.set_value(&"quest.main.stage", 0)
	store.set_value(&"player.health", 0.0)
	store.set_value(&"player.title", "")
	store.set_value(&"world.build.channel", &"x")

	var lr := m.load_slot(sid)
	_check("A.load_ok", lr["ok"], true)
	# 파일 JSON 왕복(정수형 float) 후에도 Store가 wire 값을 schema 타입으로 복원한다.
	_check("A.stage", store.get_value(&"quest.main.stage"), 7)
	_check("A.stage_typeof", typeof(store.get_value(&"quest.main.stage")), TYPE_INT)
	_check("A.health", store.get_value(&"player.health"), 42.5)
	_check("A.health_typeof", typeof(store.get_value(&"player.health")), TYPE_FLOAT)
	_check("A.title", store.get_value(&"player.title"), "knight")
	_check("A.channel", store.get_value(&"world.build.channel"), "prod")
	_check("A.channel_typeof", typeof(store.get_value(&"world.build.channel")), TYPE_STRING_NAME)
	# SESSION은 load에서 default로 시작.
	_check("A.session_default", store.get_value(&"session.intro.seen"), false)

	rt.free()
	store.free()


func _test_load_validation_fail_preserves() -> void:
	print("[B] load validation 실패 시 기존 WorldState 보존(restore 0회)")
	var store := _make_store()
	var rt = _make_runtime(store)
	var pair := _make_manager_with_section(rt)
	var m: SaveGameManager = pair[0]

	rt.start_new_game()
	store.set_value(&"quest.main.stage", 7)
	var sid := _slot("tamper")
	m.save_slot(sid)

	# 파일의 world_state payload schema_version을 불일치로 변조한다.
	var env = _read_json(sid)
	env["sections"]["world_state"]["payload"]["schema_version"] = 99
	_write_json(sid, env)

	# 현재 상태를 알려진 값으로 둔다(복원되면 7로, 보존되면 3으로 남는다).
	store.set_value(&"quest.main.stage", 3)

	var lr := m.load_slot(sid)
	_check("B.load_ok", lr["ok"], false)
	# validate-all 실패이므로 restore가 시작되지 않는다.
	_check("B.restore_count", lr["restore"]["restored_sections"].size(), 0)
	# 기존 WorldState 보존.
	_check("B.preserved", store.get_value(&"quest.main.stage"), 3)

	rt.free()
	store.free()


func _test_capture_store_not_ready() -> void:
	print("[C] capture: store not ready 시 save file 미작성")
	var rt = _make_runtime(null)  # store 미주입 -> not ready
	var pair := _make_manager_with_section(rt)
	var m: SaveGameManager = pair[0]

	var sid := _slot("store_nr")
	var sr := m.save_slot(sid)
	_check("C.save_ok", sr["ok"], false)
	_check("C.error", sr["error"], &"capture_failed")
	_check_true("C.no_file", not FileAccess.file_exists(_slot_file(sid)))

	rt.free()


func _test_capture_session_not_ready() -> void:
	print("[D] capture: store ready지만 session not ready 시 save file 미작성")
	var store := _make_store()  # initialize됨 -> store ready
	var rt = _make_runtime(store)
	# start_new_game 미호출 -> session not ready
	var pair := _make_manager_with_section(rt)
	var m: SaveGameManager = pair[0]

	_check("D.store_ready", rt.is_store_ready(), true)
	_check("D.session_not_ready", rt.is_session_ready(), false)

	var sid := _slot("session_nr")
	var sr := m.save_slot(sid)
	_check("D.save_ok", sr["ok"], false)
	_check("D.error", sr["error"], &"capture_failed")
	_check_true("D.no_file", not FileAccess.file_exists(_slot_file(sid)))

	rt.free()
	store.free()


func _test_partial_restore_with_adapter() -> void:
	print("[E] restore 중간 실패 시 중단 + partial restore report(world_state 복원 후 후속 section 실패)")
	var store := _make_store()
	var rt = _make_runtime(store)
	var m := SaveGameManager.new()
	add_child(m)
	var ws := WorldStateSaveSection.new()
	ws.set_runtime(rt)
	add_child(ws)
	var late := FakeLateSection.new()
	late.section_id = &"z_late"
	late.restore_order = 100  # world_state(-100) 이후 복원
	add_child(late)
	m.register_section(ws)
	m.register_section(late)

	rt.start_new_game()
	store.set_value(&"quest.main.stage", 11)
	var sid := _slot("partial")
	_check("E.save_ok", m.save_slot(sid)["ok"], true)

	# 흩뜨린 뒤, 후속 section restore가 실패하도록 설정.
	store.set_value(&"quest.main.stage", 0)
	late.restore_ok = false

	var lr := m.load_slot(sid)
	_check("E.load_ok", lr["ok"], false)
	var rep: Dictionary = lr["restore"]
	_check("E.reason", rep["reason"], &"partial_restore")
	# world_state는 먼저 복원됐다.
	_check_true("E.world_state_restored", rep["restored_sections"].has(&"world_state"))
	_check("E.failed_section", rep["failed_section"], &"z_late")
	# world_state 값이 실제로 복원됐는지(중간 실패 전).
	_check("E.stage_restored", store.get_value(&"quest.main.stage"), 11)

	rt.free()
	store.free()


func _test_runtime_contract_invalid() -> void:
	print("[F] duck-type runtime 반환 shape 위반 -> runtime_contract_invalid (SCRIPT ERROR 없음)")
	var bad := BadRuntime.new()
	add_child(bad)
	var sec := WorldStateSaveSection.new()
	sec.set_runtime(bad)
	add_child(sec)

	# capture: capture_world_state가 Dictionary가 아님.
	var cap := sec.capture_save()
	_check("F.capture_ok", cap["ok"], false)
	_check("F.capture_reason", cap["reason"], &"runtime_contract_invalid")
	# validate: peek가 Dictionary가 아님.
	var v := sec.validate_save({"schema_version": 1, "values": {}})
	_check("F.validate_ok", v["ok"], false)
	_check("F.validate_reason", v["reason"], &"runtime_contract_invalid")
	# restore: report가 Dictionary가 아님.
	var r := sec.restore_save({"schema_version": 1, "values": {}})
	_check("F.restore_ok", r["ok"], false)
	_check("F.restore_reason", r["reason"], &"runtime_contract_invalid")
