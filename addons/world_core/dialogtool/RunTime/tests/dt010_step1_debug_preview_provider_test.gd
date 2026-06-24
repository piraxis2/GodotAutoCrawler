# DT-010 Step 1 검증용 헤드리스 테스트(Debug Provider Injection).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/dialogtool/RunTime/tests/dt010_step1_debug_preview_provider_test.tscn
#
# 목표: 에디터 Play/Debug 서브프로세스에서 DialoguePlayer가 addon example store를 read/mutation
#       provider로 주입받아 WorldStateCondition / StateAdd를 provider_missing 없이 실행함을 검증한다.
#
# 확정 계약(ADR-012 / DT-010 Step 1):
# - provider source = addon example store(examples/world_state_schema_example.tres), read·mutation 같은 인스턴스.
# - 주입 위치 = DialoguePlayer._ready() debug 분기(is_dialogue_debug_hint), start_dialogue 직전 동기 주입.
# - parse-safety = class_name만 참조, bare WorldState autoload 식별자 없음.
# - failure policy = schema load/invalid/init 실패 시 provider 미주입 + push_error, 기존 fail-closed 유지.
# - lifecycle = 프로세스 격리 의존(매 player가 example schema default에서 시작).
# - 일반 게임 경로(debug 분기 밖)는 영향 없음.
#
# 제외: 에디터 GUI Play UX, schema picker, /root/WorldState 옵션, reset UI, DT-010 Step 2~4.
extends Node

const SAMPLE_PATH := "res://addons/world_core/dialogtool/examples/sample_dialogues/sample_world_state_dialogue.tres"
const AFFINITY := &"actor.example.affinity"
const INVALID_SCHEMA_PATH := "res://__dt010_invalid_schema.tres"

var _failures: int = 0
var _tracked_stores: Array = []


func _ready() -> void:
	_install_watchdog(45.0)
	await _run_all()
	for s in _tracked_stores:
		if is_instance_valid(s):
			s.free()
	_cleanup()
	if _failures == 0:
		print("[DT-010 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-010 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-010 Step1] WATCHDOG TIMEOUT after %.0fs — --import 선행 확인." % seconds)
		get_tree().quit(2))


func _run_all() -> void:
	_test_helper_success()
	_test_helper_failure_paths()
	await _test_debug_injection_take_rich()
	await _test_debug_injection_leave_poor()
	await _test_process_isolation_default_start()
	await _test_normal_path_no_injection()


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# --- A. helper 성공 ---------------------------------------------------

func _test_helper_success() -> void:
	print("[A] preview helper: example schema로 store ready, affinity default 0, provider 메서드 사용 가능")
	var store := DialogueDebugPreviewProvider.make_preview_store()
	_check_true("A.store_not_null", store != null)
	if store == null:
		return
	_tracked_stores.append(store)
	_check("A.ready", store.is_store_ready(), true)
	# read provider 계약
	_check("A.has_state", store.has_state(AFFINITY), true)
	_check("A.read_default", store.read_state(AFFINITY), 0)
	_check("A.try_read_fallback", store.try_read_state(&"no.such.key", -1), -1)
	# mutation provider 계약(actual store 변경)
	var add_report: Dictionary = store.add_state(AFFINITY, 50)
	_check("A.add_applied", add_report.get("applied"), true)
	_check("A.add_new", add_report.get("new_value"), 50)
	_check("A.read_after_add", store.read_state(AFFINITY), 50)
	var batch_report: Dictionary = store.apply_state_batch([{"key": AFFINITY, "value": 7}])
	_check("A.batch_applied", batch_report.get("applied"), true)
	_check("A.read_after_batch", store.read_state(AFFINITY), 7)


# --- B. helper 실패 경로(fail-closed, no crash) ------------------------

func _test_helper_failure_paths() -> void:
	print("[B] helper 실패: load 실패 / StateSchema 아님 / invalid schema 모두 null, 크래시 없음")
	# B1. 존재하지 않는 경로 → load null → provider 미생성.
	var s1 := DialogueDebugPreviewProvider.make_preview_store("res://__dt010_nonexistent.tres")
	_check_true("B1.nonexistent_null", s1 == null)

	# B2. StateSchema가 아닌 리소스(샘플 대화 = DialogueGraphResource) → 타입 가드로 null.
	var s2 := DialogueDebugPreviewProvider.make_preview_store(SAMPLE_PATH)
	_check_true("B2.wrong_type_null", s2 == null)

	# B3. invalid StateSchema(schema_version 0) 저장 후 → initialize 실패로 null.
	var bad := StateSchema.new()
	bad.schema_version = 0   # invalid → is_valid() false → initialize() false
	_check("B3.save_invalid", ResourceSaver.save(bad, INVALID_SCHEMA_PATH), OK)
	var s3 := DialogueDebugPreviewProvider.make_preview_store(INVALID_SCHEMA_PATH)
	_check_true("B3.invalid_null", s3 == null)
	if s1 != null: _tracked_stores.append(s1)
	if s2 != null: _tracked_stores.append(s2)
	if s3 != null: _tracked_stores.append(s3)


# --- C/D. 실제 debug 분기 주입(sample dialogue e2e) --------------------

# is_dialogue_debug_hint() 서브프로세스를 시뮬레이션해 sample dialogue를 self-start하고 choice를
# 구동한 뒤 결과를 수집한다. 실제 DialoguePlayer._ready debug 분기(_inject_debug_preview_provider 포함)를
# 그대로 탄다.
func _run_debug_player(resource_path: String, choice_index: int) -> Dictionary:
	var prev_args: Dictionary = DialogueToolUtil.cmd_arguments.duplicate()
	DialogueToolUtil.cmd_arguments["is_dialogue_debug_mod"] = "true"
	DialogueToolUtil.cmd_arguments["dialogue_resource"] = resource_path

	var says: Array = []
	var mutation_reports: Array = []
	var condition_reports: Array = []
	var player := DialoguePlayer.new()
	player.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "display_text":
			says.append(req.get("say")))
	player.state_mutation_evaluated.connect(func(_e: int, r: Dictionary): mutation_reports.append(r))
	player.condition_evaluated.connect(func(_c: int, _u: int, r: Dictionary): condition_reports.append(r))
	add_child(player)   # _ready debug 분기 → provider 주입 + start_dialogue.call_deferred

	# deferred start + start→choice 도달까지 대기.
	await get_tree().process_frame
	await get_tree().process_frame

	var store = player.get_read_state_provider()
	var same_instance: bool = store != null and store == player.get_mutation_state_provider()

	# choice 대기 상태에서 선택 → 항목 effect 실행 후 branch로 진행.
	player.select_choice(choice_index)
	await get_tree().process_frame
	await get_tree().process_frame

	var result := {
		"say": says[-1] if says.size() > 0 else null,
		"store": store,
		"same_instance": same_instance,
		"mutation_reports": mutation_reports,
		"condition_reports": condition_reports,
	}
	if store != null:
		_tracked_stores.append(store)
	player.free()
	# cmd_arguments 복원(다른 테스트 오염 방지).
	DialogueToolUtil.cmd_arguments = prev_args
	return result


func _test_debug_injection_take_rich() -> void:
	print("[C] debug 분기 주입: Take → StateAdd(+50) → state_condition(affinity>=10) true → 'Rich'")
	var r := await _run_debug_player(SAMPLE_PATH, 0)
	_check_true("C.provider_injected", r["store"] != null)
	_check_true("C.read_eq_mutation_provider", r["same_instance"])
	_check("C.say", r["say"], "Rich")
	if r["store"] != null:
		_check("C.affinity_after_take", r["store"].read_state(AFFINITY), 50)
	# StateAdd가 provider_missing이 아니라 실제 store를 변경했는가.
	_check("C.mutation_count", r["mutation_reports"].size(), 1)
	if r["mutation_reports"].size() == 1:
		_check("C.mutation_applied", r["mutation_reports"][0].get("applied"), true)
		_check("C.mutation_no_provider_missing", r["mutation_reports"][0].get("error"), &"")
		_check("C.mutation_new", r["mutation_reports"][0].get("new_value"), 50)
	# WorldStateCondition이 provider_missing이 아니라 실제 valid pass 결과를 냈는가.
	_check_true("C.condition_evaluated", r["condition_reports"].size() >= 1)
	if r["condition_reports"].size() >= 1:
		var cr: Dictionary = r["condition_reports"][-1]
		_check("C.condition_valid", cr.get("valid"), true)
		_check("C.condition_passed", cr.get("passed"), true)
		_check_true("C.condition_touched_provider", int(cr.get("read_count", 0)) > 0)


func _test_debug_injection_leave_poor() -> void:
	print("[D] debug 분기 주입: Leave → mutation 없음 → state_condition false → 'Poor'")
	var r := await _run_debug_player(SAMPLE_PATH, 1)
	_check_true("D.provider_injected", r["store"] != null)
	_check("D.say", r["say"], "Poor")
	if r["store"] != null:
		_check("D.affinity_unchanged", r["store"].read_state(AFFINITY), 0)
	_check("D.no_mutation", r["mutation_reports"].size(), 0)
	# 조건은 valid하지만 논리상 false(provider_missing 아님).
	if r["condition_reports"].size() >= 1:
		var cr: Dictionary = r["condition_reports"][-1]
		_check("D.condition_valid", cr.get("valid"), true)
		_check("D.condition_passed", cr.get("passed"), false)


func _test_process_isolation_default_start() -> void:
	print("[E] 프로세스 격리 proxy: 매 player가 새 default store로 시작(이전 run mutation 미전파)")
	# 첫 run에서 Take로 affinity를 50까지 올린다.
	var r1 := await _run_debug_player(SAMPLE_PATH, 0)
	_check("E.run1_affinity", r1["store"].read_state(AFFINITY) if r1["store"] != null else -1, 50)
	# 두 번째 player는 별도 store를 새로 구성하므로 default 0에서 시작해 Leave면 0 유지.
	var r2 := await _run_debug_player(SAMPLE_PATH, 1)
	_check_true("E.distinct_store", r1["store"] != r2["store"])
	_check("E.run2_affinity", r2["store"].read_state(AFFINITY) if r2["store"] != null else -1, 0)
	_check("E.run2_say", r2["say"], "Poor")


func _test_normal_path_no_injection() -> void:
	print("[F] 일반 게임 경로(debug hint 없음): provider 미주입 — debug 분기 밖 영향 없음")
	var prev_args: Dictionary = DialogueToolUtil.cmd_arguments.duplicate()
	# debug hint를 명시적으로 끈다(혹시 남은 상태 방어).
	DialogueToolUtil.cmd_arguments = {}
	var player := DialoguePlayer.new()
	add_child(player)
	await get_tree().process_frame
	await get_tree().process_frame
	_check("F.no_read_provider", player.has_read_state_provider(), false)
	_check("F.no_mutation_provider", player.has_mutation_state_provider(), false)
	player.free()
	DialogueToolUtil.cmd_arguments = prev_args


func _cleanup() -> void:
	if FileAccess.file_exists(INVALID_SCHEMA_PATH):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(INVALID_SCHEMA_PATH))
