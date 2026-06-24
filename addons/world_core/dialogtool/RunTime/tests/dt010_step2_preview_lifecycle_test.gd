# DT-010 Step 2 검증용 헤드리스 테스트(Preview Lifecycle and Reset Policy).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/dialogtool/RunTime/tests/dt010_step2_preview_lifecycle_test.tscn
#
# 목표: 에디터 Play 반복 실행이 ADR-012 D4 프로세스 격리 정책으로 예측 가능함을 검증한다(제품 코드 변경
#       없음 — coordinator/start_new_game 미도입, 별도 reset 로직 없음).
#
# 확정 정책(ADR-012 D4 / DT-010 Step 2):
# - Play마다 새 Godot 프로세스 → 매 player가 make_preview_store()로 새 store를 default에서 구성.
# - 연속 Play(=연속 프로세스)에서 이전 run의 mutation이 다음 run으로 전파되지 않는다.
# - bare store는 initialize() 후 SAVE/SESSION 모두 default(coordinator 없이 양쪽 lifetime 시작).
# - 1회 run 내 mutation은 누적 유지되어 다음 condition/branch가 변경값을 읽는다.
# - preview store는 /root/WorldState autoload와 별도 인스턴스(실제 save state 격리).
#
# Lifecycle 결정(Step 1 리뷰 P3 해소): preview store(Node)는 프로세스 격리가 정리 경계이므로 프로세스 내
# 명시적 free를 두지 않는다(debug Play=1회 프로세스, teardown에서 회수). 헤드리스 테스트는 test-owned로
# 추적·free한다. coordinator/reset UI는 도입하지 않는다.
#
# 제외: 에디터 GUI Play, schema picker, /root/WorldState 옵션, DT-010 Step 3.
extends Node

const SAMPLE_PATH := "res://addons/world_core/dialogtool/examples/sample_dialogues/sample_world_state_dialogue.tres"
const AFFINITY := &"actor.example.affinity"
const SESSION_KEY := &"session.intro.seen"
const QUEST_KEY := &"quest.main.stage"
const HEALTH_KEY := &"player.health"
const CHANNEL_KEY := &"world.build.channel"
const OP := StateCondition.Operator

var _failures: int = 0
var _tracked_stores: Array = []


func _ready() -> void:
	_install_watchdog(45.0)
	await _run_all()
	for s in _tracked_stores:
		if is_instance_valid(s):
			s.free()
	if _failures == 0:
		print("[DT-010 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-010 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-010 Step2] WATCHDOG TIMEOUT after %.0fs — --import 선행 확인." % seconds)
		get_tree().quit(2))


func _run_all() -> void:
	await _test_repeat_boot_each_default()
	_test_reinit_returns_to_default()
	_test_in_run_mutation_accumulates()
	_test_save_session_both_default()
	_test_isolated_from_autoload_store()


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# is_dialogue_debug_hint() 서브프로세스를 시뮬레이션해 실제 _ready debug 분기를 탄다.
# mutation 전(choice 대기 시점) affinity와 Take 선택 후 affinity를 모두 캡처한다.
func _run_debug_player_capture(resource_path: String, choice_index: int) -> Dictionary:
	var prev_args: Dictionary = DialogueToolUtil.cmd_arguments.duplicate()
	DialogueToolUtil.cmd_arguments["is_dialogue_debug_mod"] = "true"
	DialogueToolUtil.cmd_arguments["dialogue_resource"] = resource_path

	var player := DialoguePlayer.new()
	add_child(player)
	await get_tree().process_frame
	await get_tree().process_frame

	var store = player.get_read_state_provider()
	# mutation 전(choice 대기) 시점 affinity — 매 run default여야 한다.
	var pre_affinity: Variant = store.read_state(AFFINITY) if store != null else null

	player.select_choice(choice_index)
	await get_tree().process_frame
	await get_tree().process_frame

	# free 전에 값을 모두 캡처한다(store는 player 보유 — 프로세스 격리 lifecycle).
	var post_affinity: Variant = store.read_state(AFFINITY) if store != null else null
	if store != null:
		_tracked_stores.append(store)
	player.free()
	DialogueToolUtil.cmd_arguments = prev_args
	return {"store": store, "pre": pre_affinity, "post": post_affinity}


func _affinity_ge(threshold: int) -> ConditionSet:
	var sc := StateCondition.new()
	sc.key = AFFINITY
	sc.operator = OP.GREATER_EQUAL
	sc.expected_value = threshold
	var cs := ConditionSet.new()
	cs.root = sc
	return cs


# --- A. 반복 boot: 매 run이 default에서 시작(이전 mutation 미오염) -------

func _test_repeat_boot_each_default() -> void:
	print("[A] 반복 boot(=연속 프로세스 proxy): 매 run affinity default 0에서 시작, Take→50, 다음 run 0")
	var prev_stores: Array = []
	for i in range(3):
		var r := await _run_debug_player_capture(SAMPLE_PATH, 0)   # Take
		_check("A.run%d_pre_default" % i, r["pre"], 0)
		_check("A.run%d_post_take" % i, r["post"], 50)
		# 매 run이 별도 store 인스턴스여야 한다(프로세스 격리 proxy).
		for ps in prev_stores:
			_check_true("A.run%d_distinct_store" % i, r["store"] != ps)
		prev_stores.append(r["store"])


# --- B. store re-init이 default에서 시작함을 직접 단언 ------------------

func _test_reinit_returns_to_default() -> void:
	print("[B] store re-init: 변경된 store를 initialize()하면 default로 복귀")
	var store := DialogueDebugPreviewProvider.make_preview_store()
	_check_true("B.store_ready", store != null and store.is_store_ready())
	if store == null:
		return
	_tracked_stores.append(store)
	# 변경: affinity 50, session.intro.seen true.
	store.add_state(AFFINITY, 50)
	store.set_state(SESSION_KEY, true)
	_check("B.mutated_affinity", store.read_state(AFFINITY), 50)
	_check("B.mutated_session", store.read_state(SESSION_KEY), true)
	# re-init → default.
	_check("B.reinit_ok", store.initialize(), true)
	_check("B.reinit_ready", store.is_store_ready(), true)
	_check("B.reinit_affinity_default", store.read_state(AFFINITY), 0)
	_check("B.reinit_session_default", store.read_state(SESSION_KEY), false)


# --- C. 1회 run 내 mutation 누적 → 다음 condition이 누적값을 읽음 ---------

func _test_in_run_mutation_accumulates() -> void:
	print("[C] 1회 run 내 mutation 누적: add+50, add+50 → 100, condition(affinity>=100) pass")
	var store := DialogueDebugPreviewProvider.make_preview_store()
	if store == null:
		_check_true("C.store", false)
		return
	_tracked_stores.append(store)
	store.add_state(AFFINITY, 50)
	store.add_state(AFFINITY, 50)
	_check("C.accumulated", store.read_state(AFFINITY), 100)
	# 같은 preview store를 read provider로 condition 평가 → 누적값 반영.
	var report: Dictionary = ConditionEvaluator.evaluate(_affinity_ge(100), store)
	_check("C.condition_valid", report.get("valid"), true)
	_check("C.condition_passed", report.get("passed"), true)
	_check_true("C.condition_read", int(report.get("read_count", 0)) > 0)


# --- D. bare store는 SAVE/SESSION 모두 default(coordinator 불필요) -------

func _test_save_session_both_default() -> void:
	print("[D] bare store initialize(): SAVE+SESSION 모두 default(start_new_game/coordinator 미사용)")
	var store := DialogueDebugPreviewProvider.make_preview_store()
	if store == null:
		_check_true("D.store", false)
		return
	_tracked_stores.append(store)
	# SAVE lifetime keys.
	_check("D.save_quest_default", store.read_state(QUEST_KEY), 0)
	_check("D.save_health_default", store.read_state(HEALTH_KEY), 100.0)
	_check("D.save_affinity_default", store.read_state(AFFINITY), 0)
	_check("D.save_channel_default", store.read_state(CHANNEL_KEY), &"dev")
	# SESSION lifetime key — coordinator(start_new_game) 없이도 bare initialize로 default.
	_check("D.session_default", store.read_state(SESSION_KEY), false)


# --- E. preview store는 /root/WorldState autoload와 별도 인스턴스 --------

func _test_isolated_from_autoload_store() -> void:
	print("[E] preview store는 /root/WorldState autoload와 별도 인스턴스(실제 save state 격리)")
	var store := DialogueDebugPreviewProvider.make_preview_store()
	if store == null:
		_check_true("E.store", false)
		return
	_tracked_stores.append(store)
	# parse-safe runtime lookup(코드가 아니라 테스트에서만 autoload를 직접 본다).
	var autoload_store := get_node_or_null("/root/WorldState")
	_check_true("E.autoload_present", autoload_store != null)   # 이 프로젝트엔 등록됨
	_check_true("E.distinct_instance", store != autoload_store)
	# preview store mutation이 autoload store를 건드리지 않음을 단언.
	store.set_state(AFFINITY, 42)
	if autoload_store != null and autoload_store.has_method("read_state"):
		_check_true("E.autoload_unchanged", autoload_store.read_state(AFFINITY) != 42)
	_check("E.preview_changed", store.read_state(AFFINITY), 42)
