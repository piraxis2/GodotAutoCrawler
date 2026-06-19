# SG-002 Step 2 검증용 헤드리스 통합 테스트.
# 실행:
#   godot --headless --path <project> res://addons/world_core/save_game_world_state/tests/sg002_step2_save_flow_world_state_test.tscn
#
# 검증 범위(SG-002 Step 2 완료 조건):
# - SaveFlow가 SaveGameManager + WorldStateSaveSection 조합에서 실제 slot save/load를 올바르게 위임한다.
# - WorldState store/session ready일 때 SaveFlow.save_manual 성공 + load_manual로 SAVE snapshot 파일 왕복.
# - store/session not-ready capture 실패가 원본 manager report로 그대로 전달(passthrough).
# - load backup recovery의 recovered_from_backup/source/restore가 SaveFlow.load_manual에서 손실되지 않음.
#
# 이 테스트는 의도적으로 SaveGame ↔ WorldState 경계를 넘는다(domain 결합). 따라서 domain-free를 유지해야
# 하는 addons/save_game/tests/가 아니라 통합 adapter와 같은 addons/save_game_world_state/tests/에 둔다.
#
# 주의: Godot JSON은 number를 float로 읽는다. WorldStateStore.import_snapshot이 schema 타입으로 복원하므로
# typeof/값 동등성을 함께 단언한다.
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const RUNTIME_SCRIPT := "res://addons/world_core/world_state/world_state_runtime.gd"
const PREFIX := "sg002s2_"

var _failures: int = 0


func _ready() -> void:
	_cleanup_test_slots()

	_test_full_roundtrip_via_save_flow()
	_test_store_not_ready_passthrough()
	_test_session_not_ready_passthrough()
	_test_backup_recovery_report_preserved()

	_cleanup_test_slots()

	if _failures == 0:
		print("[SG-002 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[SG-002 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- duck-type metadata provider(통합 경유 metadata 검증용) -------------

class MetaProvider extends RefCounted:
	var data: Dictionary = {}

	func make_save_metadata(_slot_id) -> Dictionary:
		return data.duplicate(true)


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


# SaveGameManager + WorldStateSaveSection + SaveFlow(주입)를 구성해 [flow, manager, section]을 반환한다.
func _make_flow_with_world_state(rt) -> Array:
	var m := SaveGameManager.new()
	add_child(m)
	var sec := WorldStateSaveSection.new()
	sec.set_runtime(rt)
	add_child(sec)
	m.register_section(sec)
	var flow := SaveFlow.new()
	add_child(flow)
	flow.set_manager(m)
	return [flow, m, sec]


func _slot(name: String) -> String:
	return PREFIX + name


func _slot_file(sid: String) -> String:
	return SaveGameManager.SAVES_DIR + "/" + sid + ".json"


func _delete_primary(sid: String) -> void:
	# primary(.json)만 제거하고 .bak은 남겨 backup recovery 경로를 강제한다.
	var d := DirAccess.open(SaveGameManager.SAVES_DIR)
	if d != null and d.file_exists(sid + ".json"):
		d.remove(sid + ".json")


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

func _test_full_roundtrip_via_save_flow() -> void:
	print("[A] SaveFlow.save_manual/load_manual로 WorldState SAVE snapshot 파일 왕복")
	var store := _make_store()
	var rt = _make_runtime(store)
	var trio := _make_flow_with_world_state(rt)
	var flow: SaveFlow = trio[0]

	# metadata provider + caller override가 통합 경로에서도 동작하는지 함께 본다.
	var provider := MetaProvider.new()
	provider.data = {"chapter": "Forest", "play_time_seconds": 120}
	flow.set_metadata_provider(provider)

	rt.start_new_game()
	store.set_value(&"quest.main.stage", 7)
	store.set_value(&"player.health", 42.5)
	store.set_value(&"player.title", "knight")
	store.set_value(&"world.build.channel", &"prod")
	store.set_value(&"session.intro.seen", true)  # SESSION (저장 안 됨)

	var sid := _slot("rt")
	var sr := flow.save_manual(sid, {"display_name": "Before Boss", "chapter": "Boss Gate"})
	_check("A.save_ok", sr["ok"], true)
	_check("A.error_empty", sr["error"], &"")
	_check_true("A.file_exists", FileAccess.file_exists(_slot_file(sid)))
	# metadata merge(provider base + caller override)가 report에 반영됐는지.
	_check("A.meta_chapter_override", sr["metadata"]["chapter"], "Boss Gate")
	_check("A.meta_playtime_kept", int(sr["metadata"]["play_time_seconds"]), 120)
	_check("A.meta_display_added", sr["metadata"]["display_name"], "Before Boss")
	# manager report passthrough(파일 경로/section 보존).
	_check_true("A.manager_has_path", sr["manager_report"].has("path"))
	_check_true("A.manager_has_sections", sr["manager_report"]["sections"].has("world_state"))

	# 저장 후 상태를 흩뜨린다.
	store.set_value(&"quest.main.stage", 0)
	store.set_value(&"player.health", 0.0)
	store.set_value(&"player.title", "")
	store.set_value(&"world.build.channel", &"x")

	var lr := flow.load_manual(sid)
	_check("A.load_ok", lr["ok"], true)
	# SAVE 값이 schema 타입으로 복원된다(파일 JSON 왕복 후에도).
	_check("A.stage", store.get_value(&"quest.main.stage"), 7)
	_check("A.stage_typeof", typeof(store.get_value(&"quest.main.stage")), TYPE_INT)
	_check("A.health", store.get_value(&"player.health"), 42.5)
	_check("A.title", store.get_value(&"player.title"), "knight")
	_check("A.channel", store.get_value(&"world.build.channel"), "prod")
	_check("A.channel_typeof", typeof(store.get_value(&"world.build.channel")), TYPE_STRING_NAME)
	# SESSION은 load에서 default로 시작.
	_check("A.session_default", store.get_value(&"session.intro.seen"), false)
	# load_manual이 manager report 정보를 손실 없이 전달.
	_check("A.load_source", lr["source"], &"primary")
	_check("A.load_recovered", lr["recovered_from_backup"], false)
	_check_true("A.load_has_restore", lr.has("restore"))

	rt.free()
	store.free()


func _test_store_not_ready_passthrough() -> void:
	print("[B] store not-ready capture 실패가 원본 manager report로 전달")
	var rt = _make_runtime(null)  # store 미주입 -> not ready
	var trio := _make_flow_with_world_state(rt)
	var flow: SaveFlow = trio[0]

	var sid := _slot("store_nr")
	var sr := flow.save_manual(sid)
	_check("B.save_ok", sr["ok"], false)
	# manager의 capture_failed가 facade error로 그대로 노출.
	_check("B.error", sr["error"], &"capture_failed")
	_check("B.manager_report_error", sr["manager_report"]["error"], &"capture_failed")
	# 원본 capture 하위 report(section reason)까지 보존.
	_check("B.section_reason", sr["manager_report"]["capture"]["section_reason"], &"store_not_ready")
	_check_true("B.no_file", not FileAccess.file_exists(_slot_file(sid)))
	# gate는 검사됐고(provider 없음 allow), metadata는 빌드됐다(provider 없음 -> {}).
	_check("B.gate_ok", sr["gate"]["ok"], true)

	rt.free()


func _test_session_not_ready_passthrough() -> void:
	print("[C] store ready지만 session not-ready capture 실패가 원본 manager report로 전달")
	var store := _make_store()  # initialize됨 -> store ready
	var rt = _make_runtime(store)  # start_new_game 미호출 -> session not ready
	var trio := _make_flow_with_world_state(rt)
	var flow: SaveFlow = trio[0]

	var sid := _slot("session_nr")
	var sr := flow.save_manual(sid)
	_check("C.save_ok", sr["ok"], false)
	_check("C.error", sr["error"], &"capture_failed")
	_check("C.section_reason", sr["manager_report"]["capture"]["section_reason"], &"session_not_ready")
	_check_true("C.no_file", not FileAccess.file_exists(_slot_file(sid)))

	rt.free()
	store.free()


func _test_backup_recovery_report_preserved() -> void:
	print("[D] backup recovery report가 SaveFlow.load_manual에서 보존된다")
	var store := _make_store()
	var rt = _make_runtime(store)
	var trio := _make_flow_with_world_state(rt)
	var flow: SaveFlow = trio[0]

	rt.start_new_game()
	# v1(primary) 저장: stage 11.
	store.set_value(&"quest.main.stage", 11)
	var sid := _slot("bak")
	_check("D.save1_ok", flow.save_manual(sid)["ok"], true)
	# v2 저장: 직전 유효 primary가 .bak으로 회전(bak=stage 11), primary=stage 22.
	store.set_value(&"quest.main.stage", 22)
	_check("D.save2_ok", flow.save_manual(sid)["ok"], true)
	_check_true("D.bak_exists", FileAccess.file_exists(SaveGameManager.SAVES_DIR + "/" + sid + ".json.bak"))

	# primary를 제거해 backup recovery 경로를 강제하고, 현재 상태를 흩뜨린다.
	_delete_primary(sid)
	store.set_value(&"quest.main.stage", 0)

	var lr := flow.load_manual(sid)
	_check("D.load_ok", lr["ok"], true)
	# recovery 정보가 facade를 거쳐도 손실되지 않는다.
	_check("D.recovered", lr["recovered_from_backup"], true)
	_check("D.source", lr["source"], &"backup")
	_check_true("D.has_restore", lr.has("restore"))
	# bak(v1)의 값(stage 11)으로 복원됐다.
	_check("D.stage_from_bak", store.get_value(&"quest.main.stage"), 11)

	rt.free()
	store.free()
