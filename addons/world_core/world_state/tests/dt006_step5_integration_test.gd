# DT-006 Step 5 end-to-end 통합 테스트.
# 실행:
#   godot --headless --path <project> res://addons/world_core/world_state/tests/dt006_step5_integration_test.tscn
#
# 실제 autoload(/root/WorldState + /root/WorldStateRuntime)로 부팅→new game→mutation→capture→
# mutation→restore→scene-lifecycle 보존→실제 Store를 DialogueManager.play에 주입까지 한 흐름으로 검증한다.
extends Node

var _failures: int = 0


func _ready() -> void:
	_e2e_lifecycle()
	await _e2e_dialogue_injection()

	if _failures == 0:
		print("[DT-006 Step5] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-006 Step5] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


func _say_resource(text: String) -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = {
		0: {"type": &"start", "params": {}},
		1: {"type": &"say", "params": {"text": text}},
		2: {"type": &"end", "params": {}},
	}
	var conns: Array[Dictionary] = [
		{"from_node_id": 0, "from_port": 0, "to_node_id": 1, "to_port": 0},
		{"from_node_id": 1, "from_port": 0, "to_node_id": 2, "to_port": 0},
	]
	res.runtime_connections = conns
	res.start_node_id = 0
	return res


# --- end-to-end -------------------------------------------------------

func _e2e_lifecycle() -> void:
	var store = get_node_or_null("/root/WorldState")
	var rt = get_node_or_null("/root/WorldStateRuntime")

	print("[A] 부팅: autoload Store/Runtime ready")
	_check_true("A.store", store != null)
	_check_true("A.runtime", rt != null)
	if store == null or rt == null:
		return
	_check_true("A.store_wired", rt.get_store() == store)
	_check("A.store_ready", rt.is_store_ready(), true)

	print("[B] start_new_game -> bootstrap default + session-ready")
	# 부팅 후 이전 상태가 있을 수 있으니 먼저 흩뜨린 뒤 새 게임으로 default 확인.
	store.set_value(&"quest.main.stage", 3)
	var new_report: Dictionary = rt.start_new_game()
	_check("B.ok", new_report["ok"], true)
	_check("B.session_ready", rt.is_session_ready(), true)
	_check("B.stage_default", store.get_value(&"quest.main.stage"), 0)
	_check("B.health_default", store.get_value(&"player.health"), 100.0)
	_check("B.session_default", store.get_value(&"session.intro.seen"), false)

	print("[C] mutation -> capture(SAVE-only)")
	store.set_value(&"quest.main.stage", 12)
	store.set_value(&"player.health", 55.0)
	store.set_value(&"session.intro.seen", true)  # SESSION
	var snap: Dictionary = rt.capture_world_state()
	_check_true("C.capture_save", snap["values"].has("quest.main.stage"))
	_check_true("C.capture_no_session", not snap["values"].has("session.intro.seen"))

	print("[D] 추가 mutation -> restore: SAVE 복원, SESSION default")
	store.set_value(&"quest.main.stage", 99)
	store.set_value(&"player.health", 1.0)
	var restore_report: Dictionary = rt.restore_world_state(snap)
	_check("D.ok", restore_report["ok"], true)
	_check("D.session_ready", rt.is_session_ready(), true)
	_check("D.stage_restored", store.get_value(&"quest.main.stage"), 12)
	_check("D.health_restored", store.get_value(&"player.health"), 55.0)
	_check("D.session_default", store.get_value(&"session.intro.seen"), false)

	print("[E] scene-lifecycle: transient scene churn에도 autoload 값 보존")
	store.set_value(&"quest.main.stage", 7)
	var transient := Node.new()
	add_child(transient)
	transient.queue_free()
	var store2 = get_node_or_null("/root/WorldState")
	_check_true("E.same_instance", store2 == store)
	_check("E.value_persists", store2.get_value(&"quest.main.stage"), 7)
	_check("E.still_ready", rt.is_store_ready(), true)


func _e2e_dialogue_injection() -> void:
	print("[F] 실제 Store를 DialogueManager.play에 read provider로 주입")
	var store = get_node_or_null("/root/WorldState")
	if store == null:
		_check("F.store", false, true)
		return
	store.set_value(&"quest.main.stage", 21)

	DialogueManager.play(_say_resource("hello"), store)
	await get_tree().process_frame
	await get_tree().process_frame

	_check("F.playing", DialogueManager.is_playing(), true)
	var player = DialogueManager._ui.dialogue_player
	_check_true("F.provider_is_store", player.get_read_state_provider() == store)
	# Dialogue read seam이 실제 Store 값을 라우팅한다.
	_check("F.read_routes", player.read_state(&"quest.main.stage"), 21)
	_check("F.try_missing", player.try_read_state(&"nope.nope", -1), -1)
	DialogueManager._dismiss()
	await get_tree().process_frame
	# 정리: 다음 실행 위생을 위해 default로.
	get_node_or_null("/root/WorldStateRuntime").start_new_game()
