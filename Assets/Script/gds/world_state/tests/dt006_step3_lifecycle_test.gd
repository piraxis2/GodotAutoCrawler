# DT-006 Step 3 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://Assets/Script/gds/world_state/tests/dt006_step3_lifecycle_test.tscn
#
# 검증 범위 (WorldStateRuntime coordinator):
# - start_new_game(): SAVE+SESSION 모두 default + session-ready
# - restore_game(): default 재초기화 후 SAVE 복원, SESSION default, session-ready (transactional)
# - 실패한 restore(malformed/version mismatch): 기존 상태/세션 보존, session-ready 미전환
# - busy/재진입 거부, capture_world_state(SAVE-only), autoload 존재/연결
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const RUNTIME_SCRIPT := "res://Assets/Script/gds/world_state/world_state_runtime.gd"

var _failures: int = 0
var _ready_log: Array = []
var _failed_log: Array = []


func _ready() -> void:
	_test_new_game()
	_test_capture_and_restore()
	_test_restore_malformed_preserves()
	_test_restore_version_mismatch_preserves()
	_test_reentrancy_busy()
	_test_store_swap_during_restore_rejected()
	_test_store_swap_idle_resets_session()
	_test_autoload_wired()

	if _failures == 0:
		print("[DT-006 Step3] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-006 Step3] FAILED: %d assertion(s)" % _failures)
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


func _def(key: StringName, vtype: int, default_value: Variant,
		lifetime: int = LT.SAVE) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	d.lifetime = lifetime
	return d


# 테스트용 Store(주입). SAVE int + SESSION bool.
func _make_store() -> WorldStateStore:
	var s := StateSchema.new()
	var defs: Array[StateDefinition] = [
		_def(&"quest.main.stage", VT.INT, 0),
		_def(&"actor.example.affinity", VT.INT, 0),
		_def(&"session.intro.seen", VT.BOOL, false, LT.SESSION),
	]
	s.definitions = defs
	var store := WorldStateStore.new()
	store.schema = s
	store.initialize()
	return store


# coordinator를 주입 Store와 함께 만든다(트리에 붙이지 않음 -> _ready 미실행, autoload 미조회).
func _make_runtime(store: WorldStateStore):
	var rt = load(RUNTIME_SCRIPT).new()
	rt.set_store(store)
	rt.world_state_ready.connect(func(mode, report): _ready_log.append({"mode": mode, "report": report}))
	rt.world_state_failed.connect(func(mode, report): _failed_log.append({"mode": mode, "report": report}))
	return rt


# --- 시나리오 ---------------------------------------------------------

func _test_new_game() -> void:
	print("[A] start_new_game -> 전부 default + session-ready")
	var store := _make_store()
	store.set_value(&"quest.main.stage", 5)
	store.set_value(&"session.intro.seen", true)
	var rt = _make_runtime(store)
	_ready_log.clear()
	var report: Dictionary = rt.start_new_game()
	_check("A.ok", report["ok"], true)
	_check("A.session_ready", rt.is_session_ready(), true)
	_check("A.stage_default", store.get_value(&"quest.main.stage"), 0)
	_check("A.session_default", store.get_value(&"session.intro.seen"), false)
	_check("A.signal", _ready_log.size(), 1)
	_check("A.signal_mode", _ready_log[0]["mode"], &"new_game")
	rt.free()
	store.free()


func _test_capture_and_restore() -> void:
	print("[B] capture(SAVE-only) + restore round-trip, SESSION default")
	var store := _make_store()
	var rt = _make_runtime(store)
	rt.start_new_game()
	store.set_value(&"quest.main.stage", 3)
	store.set_value(&"actor.example.affinity", 42)
	store.set_value(&"session.intro.seen", true)  # SESSION

	var snap: Dictionary = rt.capture_world_state()
	_check_true("B.capture_has_save", snap["values"].has("quest.main.stage"))
	_check_true("B.capture_no_session", not snap["values"].has("session.intro.seen"))

	# 흩뜨린 뒤 restore.
	store.set_value(&"quest.main.stage", 99)
	store.set_value(&"actor.example.affinity", 0)
	_ready_log.clear()
	var report: Dictionary = rt.restore_game(snap)
	_check("B.ok", report["ok"], true)
	_check("B.session_ready", rt.is_session_ready(), true)
	_check("B.stage_restored", store.get_value(&"quest.main.stage"), 3)
	_check("B.affinity_restored", store.get_value(&"actor.example.affinity"), 42)
	# load는 SESSION을 default로 시작한다(snapshot에 없고 initialize가 default로 reset).
	_check("B.session_default", store.get_value(&"session.intro.seen"), false)
	_check("B.signal_mode", _ready_log[0]["mode"], &"load")
	rt.free()
	store.free()


func _test_restore_malformed_preserves() -> void:
	print("[C] malformed restore -> 기존 상태/세션 보존, session-ready 미전환")
	var store := _make_store()
	var rt = _make_runtime(store)
	rt.start_new_game()
	store.set_value(&"quest.main.stage", 7)
	store.set_value(&"session.intro.seen", true)
	_failed_log.clear()
	var report: Dictionary = rt.restore_game({})  # malformed
	_check("C.ok", report["ok"], false)
	_check("C.reason", report["reason"], "malformed_snapshot")
	_check_true("C.preserved_flag", report.get("preserved", false))
	# 기존 상태 보존(reset 없음).
	_check("C.stage_preserved", store.get_value(&"quest.main.stage"), 7)
	_check("C.session_preserved", store.get_value(&"session.intro.seen"), true)
	# 새 게임으로 이미 session-ready였고, 실패 restore가 이를 해치지 않는다.
	_check("C.session_still_ready", rt.is_session_ready(), true)
	_check("C.failed_signal", _failed_log.size(), 1)
	rt.free()
	store.free()


func _test_restore_version_mismatch_preserves() -> void:
	print("[D] version mismatch restore -> 보존")
	var store := _make_store()
	var rt = _make_runtime(store)
	rt.start_new_game()
	store.set_value(&"quest.main.stage", 4)
	var report: Dictionary = rt.restore_game({"schema_version": 2, "values": {"quest.main.stage": 1}})
	_check("D.ok", report["ok"], false)
	_check("D.reason", report["reason"], "schema_version_mismatch")
	_check("D.stage_preserved", store.get_value(&"quest.main.stage"), 4)
	rt.free()
	store.free()


func _test_reentrancy_busy() -> void:
	print("[E] restore import 중 재진입 lifecycle 호출은 busy 거부")
	var store := _make_store()
	var rt = _make_runtime(store)
	rt.start_new_game()
	store.set_value(&"quest.main.stage", 1)
	var snap: Dictionary = rt.capture_world_state()
	# restore가 import에서 stage를 다시 1로 commit하도록, 먼저 다른 값으로 바꿔 둔다.
	# (connect 전에 수행 — 이 set의 value_changed로 cb가 먼저 발생하지 않게.)
	store.set_value(&"quest.main.stage", 50)

	# restore_game의 import가 value_changed를 발행하는 동안(coordinator _busy=true) 재진입 호출한다.
	var observed := {"reenter": null}
	var cb := func(_k, _o, _n):
		if observed["reenter"] == null:
			observed["reenter"] = rt.start_new_game()
	store.value_changed.connect(cb)

	rt.restore_game(snap)  # initialize(stage->0) 후 import(stage 0->1) commit 중 cb 발생
	store.value_changed.disconnect(cb)

	_check_true("E.reenter_observed", observed["reenter"] != null)
	if observed["reenter"] != null:
		_check("E.reenter_ok", observed["reenter"]["ok"], false)
		_check("E.reenter_reason", observed["reenter"]["reason"], "busy")
	rt.free()
	store.free()


func _test_store_swap_during_restore_rejected() -> void:
	print("[G] restore import 중 set_store 교체/null 거부 (transaction 보호)")
	var store := _make_store()
	var other := _make_store()
	var rt = _make_runtime(store)
	rt.start_new_game()
	store.set_value(&"quest.main.stage", 1)
	var snap: Dictionary = rt.capture_world_state()
	store.set_value(&"quest.main.stage", 50)  # connect 전

	var observed := {"attempted": false}
	var cb := func(_k, _o, _n):
		if not observed["attempted"]:
			observed["attempted"] = true
			rt.set_store(other)  # _busy 중 -> 거부
			rt.set_store(null)   # _busy 중 -> 거부
	store.value_changed.connect(cb)
	var report: Dictionary = rt.restore_game(snap)  # import(stage 0->1) 중 cb
	store.value_changed.disconnect(cb)

	_check_true("G.attempted", observed["attempted"])
	# Store는 교체되지 않았고, restore는 원래 Store에 정상 적용됐다.
	_check_true("G.store_unchanged", rt.get_store() == store)
	_check("G.ok", report["ok"], true)
	_check("G.stage_restored", store.get_value(&"quest.main.stage"), 1)
	_check("G.session_ready", rt.is_session_ready(), true)
	rt.free()
	store.free()
	other.free()


func _test_store_swap_idle_resets_session() -> void:
	print("[H] idle Store 교체는 session 해제, null 주입은 안전")
	var a := _make_store()
	var b := _make_store()
	var rt = _make_runtime(a)
	rt.start_new_game()
	_check("H.session_before", rt.is_session_ready(), true)
	# idle 교체 -> session-ready 해제, 새 Store 반영.
	rt.set_store(b)
	_check_true("H.store_is_b", rt.get_store() == b)
	_check("H.session_after_swap", rt.is_session_ready(), false)
	# null 주입(idle)도 런타임 오류 없이 안전 — not-ready로 보고.
	rt.set_store(null)
	_check("H.null_capture", rt.capture_world_state(), {})
	_check("H.null_new_game_reason", rt.start_new_game()["reason"], "store_missing")
	_check("H.null_restore_reason", rt.restore_game({"schema_version": 1, "values": {}})["reason"], "store_missing")
	rt.free()
	a.free()
	b.free()


func _test_autoload_wired() -> void:
	print("[F] autoload WorldStateRuntime 연결")
	var rt = get_node_or_null("/root/WorldStateRuntime")
	_check_true("F.exists", rt != null)
	if rt == null:
		return
	var store = get_node_or_null("/root/WorldState")
	_check_true("F.store_wired", rt.get_store() == store)
	_check("F.store_ready", rt.is_store_ready(), true)
