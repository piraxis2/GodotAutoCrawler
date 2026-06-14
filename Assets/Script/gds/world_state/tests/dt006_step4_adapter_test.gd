# DT-006 Step 4 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://Assets/Script/gds/world_state/tests/dt006_step4_adapter_test.tscn
#
# 검증 범위 (snapshot adapter 경계):
# - capture_world_state(): SAVE-only, JSON 호환 Dictionary
# - restore_world_state(): JSON stringify/parse 왕복 후 타입·값 보존(SESSION default)
# - invalid data(malformed/version mismatch)는 성공으로 보고되지 않고 기존 상태 보존
# - capture는 not-ready에서 안전(빈 Dictionary)
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const RUNTIME_SCRIPT := "res://Assets/Script/gds/world_state/world_state_runtime.gd"

var _failures: int = 0


func _ready() -> void:
	_test_capture_save_only()
	_test_json_roundtrip_via_adapter()
	_test_invalid_via_adapter_preserves()
	_test_capture_not_ready_safe()

	if _failures == 0:
		print("[DT-006 Step4] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-006 Step4] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


func _def(key: StringName, vtype: int, default_value: Variant, lifetime: int = LT.SAVE) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	d.lifetime = lifetime
	return d


# 5개 타입 + SESSION을 포함한 주입 Store.
func _make_store() -> WorldStateStore:
	var s := StateSchema.new()
	var defs: Array[StateDefinition] = [
		_def(&"quest.main.stage", VT.INT, 0),
		_def(&"player.health", VT.FLOAT, 100.0),
		_def(&"player.display_name", VT.STRING, "hero"),
		_def(&"world.build.channel", VT.STRING_NAME, &"dev"),
		_def(&"session.intro.seen", VT.BOOL, false, LT.SESSION),
	]
	s.definitions = defs
	var store := WorldStateStore.new()
	store.schema = s
	store.initialize()
	return store


func _make_runtime(store: WorldStateStore):
	var rt = load(RUNTIME_SCRIPT).new()
	rt.set_store(store)
	return rt


# --- 시나리오 ---------------------------------------------------------

func _test_capture_save_only() -> void:
	print("[A] capture는 SAVE-only, JSON 호환")
	var store := _make_store()
	var rt = _make_runtime(store)
	rt.start_new_game()
	store.set_value(&"session.intro.seen", true)
	var snap: Dictionary = rt.capture_world_state()
	_check_true("A.values_is_dict", snap["values"] is Dictionary)
	_check_true("A.has_save", snap["values"].has("quest.main.stage"))
	_check_true("A.no_session", not snap["values"].has("session.intro.seen"))
	# JSON 직렬화가 가능해야 한다(왕복으로 확인).
	var parsed: Variant = JSON.parse_string(JSON.stringify(snap))
	_check_true("A.json_ok", parsed is Dictionary)
	rt.free()
	store.free()


func _test_json_roundtrip_via_adapter() -> void:
	print("[B] restore_world_state JSON 왕복 후 타입/값 보존, SESSION default")
	var store := _make_store()
	var rt = _make_runtime(store)
	rt.start_new_game()
	store.set_value(&"quest.main.stage", 7)
	store.set_value(&"player.health", 42.5)
	store.set_value(&"player.display_name", "knight")
	store.set_value(&"world.build.channel", &"prod")
	store.set_value(&"session.intro.seen", true)  # SESSION

	var json := JSON.stringify(rt.capture_world_state())
	var parsed: Variant = JSON.parse_string(json)

	# 흩뜨린다.
	store.set_value(&"quest.main.stage", 0)
	store.set_value(&"player.health", 0.0)
	store.set_value(&"player.display_name", "")
	store.set_value(&"world.build.channel", &"x")

	var report: Dictionary = rt.restore_world_state(parsed)
	_check("B.ok", report["ok"], true)
	_check("B.stage", store.get_value(&"quest.main.stage"), 7)
	_check("B.stage_typeof", typeof(store.get_value(&"quest.main.stage")), TYPE_INT)
	_check("B.health", store.get_value(&"player.health"), 42.5)
	_check("B.health_typeof", typeof(store.get_value(&"player.health")), TYPE_FLOAT)
	_check("B.name", store.get_value(&"player.display_name"), "knight")
	_check("B.name_typeof", typeof(store.get_value(&"player.display_name")), TYPE_STRING)
	_check("B.channel", store.get_value(&"world.build.channel"), "prod")
	_check("B.channel_typeof", typeof(store.get_value(&"world.build.channel")), TYPE_STRING_NAME)
	# adapter restore도 SESSION을 default로 시작한다.
	_check("B.session_default", store.get_value(&"session.intro.seen"), false)
	rt.free()
	store.free()


func _test_invalid_via_adapter_preserves() -> void:
	print("[C] invalid data는 성공 미보고 + 기존 상태 보존")
	var store := _make_store()
	var rt = _make_runtime(store)
	rt.start_new_game()
	store.set_value(&"quest.main.stage", 5)

	var malformed: Dictionary = rt.restore_world_state({"values": {}})  # schema_version 없음
	_check("C.malformed_ok", malformed["ok"], false)
	_check("C.malformed_preserved", store.get_value(&"quest.main.stage"), 5)

	var mismatch: Dictionary = rt.restore_world_state({"schema_version": 99, "values": {"quest.main.stage": 1}})
	_check("C.mismatch_ok", mismatch["ok"], false)
	_check("C.mismatch_reason", mismatch["reason"], "schema_version_mismatch")
	_check("C.mismatch_preserved", store.get_value(&"quest.main.stage"), 5)
	rt.free()
	store.free()


func _test_capture_not_ready_safe() -> void:
	print("[D] not-ready에서 capture는 빈 Dictionary(안전)")
	var rt = load(RUNTIME_SCRIPT).new()
	# Store 미주입 -> not ready.
	_check("D.capture_empty", rt.capture_world_state(), {})
	rt.free()
