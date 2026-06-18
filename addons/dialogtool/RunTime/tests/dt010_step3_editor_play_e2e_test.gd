# DT-010 Step 3 검증용 헤드리스 e2e 테스트(Editor Play E2E and Docs).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt010_step3_editor_play_e2e_test.tscn
#
# 목표: 실제 에디터 Play에 가까운 경로 — 동봉 sample dialogue를 실제 Dialogue_UI 씬(그 안의 child
#       DialoguePlayer)으로 debug-hint self-start해 Take→Rich / Leave→Poor를 재현한다.
#
# 실제 서브프로세스와의 일치점: 에디터 Play는 사용자 --scene(보통 DialoguePlayer를 품은 UI)을 main
# scene으로 띄우고, child DialoguePlayer._ready(debug 분기, 부모보다 먼저)가 preview provider 주입 +
# start_dialogue.call_deferred로 self-start한다. 부모 UI._ready가 signal을 연결한 뒤 deferred start가
# 발화하므로 첫 ui_request를 놓치지 않는다. DialogueManager.play/UI.play 경로는 타지 않는다(이중 UI 없음).
#
# Step 1(bare player) 대비 추가 커버: DialogueUI 공존(렌더 경로 say.text)에서도 provider 주입이 동작하고
# 이중 start/이중 provider 충돌이 없음을 확인한다.
#
# 제외: 실제 GUI 클릭/원격 디버그 Output 채널(수동 절차), 옵션 C 구현(후속).
extends Node

const UI_SCENE := "res://addons/dialogtool/UI/Dialogue_UI.tscn"
const SAMPLE_PATH := "res://addons/dialogtool/examples/sample_dialogues/sample_world_state_dialogue.tres"
const AFFINITY := &"actor.example.affinity"

var _failures: int = 0
var _tracked_stores: Array = []


func _ready() -> void:
	_install_watchdog(45.0)
	await _run_all()
	for s in _tracked_stores:
		if is_instance_valid(s):
			s.free()
	if _failures == 0:
		print("[DT-010 Step3] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-010 Step3] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-010 Step3] WATCHDOG TIMEOUT after %.0fs — --import 선행 확인." % seconds)
		get_tree().quit(2))


func _run_all() -> void:
	await _test_take_rich()
	await _test_leave_poor()


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# 실제 Dialogue_UI 씬을 debug-hint로 띄워 sample을 self-start하고 choice를 구동한다.
# 반환: { store, say_via_request, say_label }.
func _run_editor_play(choice_index: int) -> Dictionary:
	var prev_args: Dictionary = DialogueToolUtil.cmd_arguments.duplicate()
	DialogueToolUtil.cmd_arguments["is_dialogue_debug_mod"] = "true"
	DialogueToolUtil.cmd_arguments["dialogue_resource"] = SAMPLE_PATH

	var says: Array = []
	var ui: DialogueUI = load(UI_SCENE).instantiate()
	# UI가 중계하는 ui_request로 say를 캡처한다(player.ui_request → UI.ui_request.emit).
	ui.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "display_text":
			says.append(req.get("say")))
	add_child(ui)   # child player._ready(debug 분기) → provider 주입 + deferred self-start, 이후 UI._ready

	# deferred start + start→choice 도달까지 대기.
	await get_tree().process_frame
	await get_tree().process_frame

	var player: DialoguePlayer = ui.dialogue_player
	var store = player.get_read_state_provider()
	var same_instance: bool = store != null and store == player.get_mutation_state_provider()
	var waiting := player.waiting_for

	player.select_choice(choice_index)
	await get_tree().process_frame
	await get_tree().process_frame

	var say_label := str(ui.say.text)
	if store != null:
		_tracked_stores.append(store)
	var result := {
		"store": store,
		"same_instance": same_instance,
		"waiting_at_choice": waiting == &"choice",
		"say_via_request": says[-1] if says.size() > 0 else null,
		"say_label": say_label,
	}
	ui.queue_free()
	await get_tree().process_frame
	DialogueToolUtil.cmd_arguments = prev_args
	return result


func _test_take_rich() -> void:
	print("[A] 실제 Dialogue_UI 씬 debug Play: Take → StateAdd(+50) → state_condition true → 'Rich'")
	var r := await _run_editor_play(0)
	_check_true("A.provider_injected_in_ui_player", r["store"] != null)
	_check_true("A.read_eq_mutation_provider", r["same_instance"])
	_check_true("A.reached_choice_wait", r["waiting_at_choice"])
	_check("A.say_via_request", r["say_via_request"], "Rich")
	_check("A.say_label_rendered", r["say_label"], "Rich")
	if r["store"] != null:
		_check("A.affinity_after_take", r["store"].read_state(AFFINITY), 50)


func _test_leave_poor() -> void:
	print("[B] 실제 Dialogue_UI 씬 debug Play: Leave → mutation 없음 → state_condition false → 'Poor'")
	var r := await _run_editor_play(1)
	_check_true("B.provider_injected_in_ui_player", r["store"] != null)
	_check("B.say_via_request", r["say_via_request"], "Poor")
	_check("B.say_label_rendered", r["say_label"], "Poor")
	if r["store"] != null:
		_check("B.affinity_unchanged", r["store"].read_state(AFFINITY), 0)
