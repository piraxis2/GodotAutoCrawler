# DT-016 Step 1 검증용 헤드리스 테스트(DialogueManager Lifecycle Regression Step 1).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/dialogtool/RunTime/tests/dt016_step1_manager_lifecycle_test.tscn
#
# 목표:
# - 게임 코드 진입점인 `DialogueManager.play(...)` 기준으로 반복 실행/교체/same-frame latest-wins/
#   callback 재진입/stale signal 차단/provider tuple isolation 계약을 전용 회귀 matrix로 고정한다.
# - 모든 graph는 runtime-only `DialogueGraphResource`를 코드에서 만든다(영구 .tres 추가 없음).
# - 제품 코드 변경 없이 테스트만 추가한다.
#
# 공통 규칙:
# - Primary path는 항상 `DialogueManager.play(...)`.
# - Say 진행은 `DialogueManager._ui.dialogue_player.advance()`, Choice 선택은
#   `..._ui.dialogue_player.select_choice(0)`로 직접 한다(렌더 텍스트/Button 클릭에 의존하지 않음).
# - 관찰은 `DialogueManager.ui_request`/`dialogue_started`/`dialogue_end` signal log/count로만 한다.
# - 교체/`_dismiss()` 직후 stale old player 호출은 반드시 같은 프레임 valid window 안에서 한다
#   (play(NEW)/_dismiss() -> await 없이 is_instance_valid 단언 -> 즉시 stale 호출 -> 그 다음 await).
#
# 예상 경고(SCRIPT ERROR 아님): 시나리오 [5]는 effect_then_say fixture가 빈 texture_path를 가진
# portrait_show Effect를 발행하므로 DialoguePlayer가 "portrait_show node N has empty texture_path"
# push_warning을 1회 남긴다. portrait 렌더는 DT-016 Non-Goal이라 의도된 경고다.

extends Node

var _failures: int = 0

# DialogueManager signal 공통 recorder(매 시나리오 시작 시 _begin()으로 리셋).
var _req_log: Array = []
var _started_n: int = 0
var _end_n: int = 0


# test-only spy mutation provider(DT-016 fixture #4). OLD/NEW provider 호출 count를 구분한다.
# 계약 reflection을 통과하려면 두 메서드 모두 인자를 untyped로 선언해야 한다.
# 특히 add_state(key, delta)의 delta는 untyped 필수다(delta: int 등 typed면 _method_accepts의
# {"types": []} spec을 못 지나 provider_contract_invalid가 됨).
class SpyMutationProvider extends RefCounted:
	var apply_calls: int = 0
	var add_calls: int = 0

	func apply_state_batch(changes):
		apply_calls += 1
		# set path 계약 shape: applied + diff.
		return {"applied": true, "diff": []}

	func add_state(key, delta):
		add_calls += 1
		# add path 계약 shape: applied + old/new value.
		return {"applied": true, "old_value": 0, "new_value": delta}


func _ready() -> void:
	_install_watchdog(45.0)
	_connect_recorders()
	await _run_all()
	_disconnect_recorders()
	DialogueManager._dismiss()
	if _failures == 0:
		print("[DT-016 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-016 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _run_all() -> void:
	await _test_repeat_after_end()
	await _test_replace_while_waiting_say()
	await _test_replace_while_waiting_choice()
	await _test_same_frame_latest_wins()
	await _test_request_callback_reentry()
	await _test_end_callback_reentry()
	await _test_provider_isolation_on_replacement()
	await _test_dismiss_null_safety()


# --- 데드락 방지 watchdog ----------------------------------------------

func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-016 Step1] WATCHDOG TIMEOUT after %.0fs — --import 선행 확인." % seconds)
		_disconnect_recorders()
		DialogueManager._dismiss()
		get_tree().quit(2))


# --- 단언 헬퍼 ----------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# --- DialogueManager signal recorder -----------------------------------

func _connect_recorders() -> void:
	if not DialogueManager.ui_request.is_connected(_rec_req):
		DialogueManager.ui_request.connect(_rec_req)
	if not DialogueManager.dialogue_started.is_connected(_rec_started):
		DialogueManager.dialogue_started.connect(_rec_started)
	if not DialogueManager.dialogue_end.is_connected(_rec_end):
		DialogueManager.dialogue_end.connect(_rec_end)


func _disconnect_recorders() -> void:
	if DialogueManager.ui_request.is_connected(_rec_req):
		DialogueManager.ui_request.disconnect(_rec_req)
	if DialogueManager.dialogue_started.is_connected(_rec_started):
		DialogueManager.dialogue_started.disconnect(_rec_started)
	if DialogueManager.dialogue_end.is_connected(_rec_end):
		DialogueManager.dialogue_end.disconnect(_rec_end)


func _rec_req(req: Dictionary) -> void:
	_req_log.append(_summ(req))


func _rec_started() -> void:
	_started_n += 1


func _rec_end() -> void:
	_end_n += 1


# request payload를 비교 가능한 짧은 문자열로 요약한다.
func _summ(req: Dictionary) -> String:
	match req.get("type", "?"):
		"display_text": return "say:" + str(req.get("say", ""))
		"offer_choice": return "choice"
		"portrait_state": return "portrait:" + str(req.get("action", ""))
		_: return str(req.get("type", "?"))


func _count(arr: Array, v) -> int:
	var n := 0
	for x in arr:
		if x == v:
			n += 1
	return n


# 시나리오 시작/종료 정리. _begin은 recorder를 리셋하고 이전 대화를 폐기한다.
func _begin() -> void:
	DialogueManager._dismiss()
	_req_log.clear()
	_started_n = 0
	_end_n = 0


func _end_test() -> void:
	DialogueManager._dismiss()
	await get_tree().process_frame


# 두 process frame 대기(deferred start 반영용).
func _f2() -> void:
	await get_tree().process_frame
	await get_tree().process_frame


# --- graph fixtures(runtime-only) --------------------------------------

func _n(type: StringName, params: Dictionary = {}) -> Dictionary:
	return {"type": type, "params": params}


func _c(from_id: int, to_id: int, kind: String = "", from_port: int = 0, to_port: int = 0) -> Dictionary:
	var d := {"from_node_id": from_id, "from_port": from_port, "to_node_id": to_id, "to_port": to_port}
	if kind != "":
		d["kind"] = kind
	return d


func _make_resource(nodes: Dictionary, conns: Array) -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = nodes
	var typed: Array[Dictionary] = []
	for c in conns:
		typed.append(c)
	res.runtime_connections = typed
	res.start_node_id = 0
	return res


# fixture 1: Start -> Say(text) -> End. Say 노드 id = 1.
func _say_then_end(text: String) -> DialogueGraphResource:
	return _make_resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": text}),
		2: _n(&"end"),
	}, [_c(0, 1), _c(1, 2)])


# fixture 2: Start -> Choice([label]) -> End. Choice 노드 id = 1, 항목0 flow(port0) -> End.
func _choice_then_end(label: String) -> DialogueGraphResource:
	return _make_resource({
		0: _n(&"start"),
		1: _n(&"choice", {"choices": [label]}),
		2: _n(&"end"),
	}, [_c(0, 1), _c(1, 2, "", 0, 0)])


# fixture 3: Start -> portrait_show(effect) + Say(old_text) -> End.
# Start의 비대기 Effect로 portrait_state 요청을 먼저 발행한 뒤 Flow로 Say(old_text)에 도달한다.
# (new_text는 호출자가 별도 say_then_end로 구성하므로 여기서는 받기만 한다.)
# portrait_show는 빈 texture_path라 DialoguePlayer가 expected warning 1회를 남긴다(렌더는 Non-Goal).
func _effect_then_say(old_text: String, _new_text: String = "") -> DialogueGraphResource:
	return _make_resource({
		0: _n(&"start"),
		1: _n(&"portrait_show", {"slot": "center"}),
		2: _n(&"say", {"text": old_text}),
		3: _n(&"end"),
	}, [_c(0, 1, "effect"), _c(0, 2), _c(2, 3)])


# fixture 4: Start -> state_add Effect -> Say(text) -> End.
# Start의 비대기 Effect로 mutation provider의 add_state를 1회 호출한다(spy로 OLD/NEW 구분).
func _provider_effect_graph(text: String) -> DialogueGraphResource:
	return _make_resource({
		0: _n(&"start"),
		1: _n(&"state_add", {"key": &"x", "delta": 1}),
		2: _n(&"say", {"text": text}),
		3: _n(&"end"),
	}, [_c(0, 1, "effect"), _c(0, 2), _c(2, 3)])


# --- 시나리오 ----------------------------------------------------------

# [1] Repeat after end: 같은 resource를 끝까지 실행한 뒤 다시 실행해도 이전 상태가 새지 않는다.
func _test_repeat_after_end() -> void:
	print("[1] Repeat after end")
	_begin()
	var res := _say_then_end("A")

	# Run 1
	DialogueManager.play(res)
	await _f2()
	_check("1.run1_say_A_once", _count(_req_log, "say:A"), 1)
	_check("1.run1_started", _started_n, 1)
	var p1: DialoguePlayer = DialogueManager._ui.dialogue_player
	_check("1.run1_waiting", p1.waiting_for, &"text")
	_check("1.run1_node_is_say", p1.current_node_id, 1)
	p1.advance()
	await _f2()
	_check("1.run1_end", _end_n, 1)

	# Run 2 (동형 resource 재실행) — Say A가 다시 정확히 1회.
	DialogueManager.play(res)
	await _f2()
	_check("1.run2_started", _started_n, 2)
	_check("1.run2_say_A_total_twice", _count(_req_log, "say:A"), 2)
	var p2: DialoguePlayer = DialogueManager._ui.dialogue_player
	_check("1.run2_waiting", p2.waiting_for, &"text")
	_check("1.run2_node_is_say", p2.current_node_id, 1)
	p2.advance()
	await _f2()
	_check("1.run2_end", _end_n, 2)
	# started/end count가 run 수(2)와 일치.
	_check("1.counts_match_runs", [_started_n, _end_n], [2, 2])
	await _end_test()


# [2] Replace while waiting for Say: OLD Say 대기 중 교체. stale old advance가 새 이벤트를 안 만든다.
func _test_replace_while_waiting_say() -> void:
	print("[2] Replace while waiting for Say")
	_begin()
	DialogueManager.play(_say_then_end("OLD"))
	await _f2()
	var old_player: DialoguePlayer = DialogueManager._ui.dialogue_player
	_check("2.old_waiting_text", old_player.waiting_for, &"text")
	var pre_size := _req_log.size()
	var pre_end := _end_n

	# 교체 후 같은 프레임 valid window에서 stale advance.
	DialogueManager.play(_say_then_end("NEW"))
	_check_true("2.old_player_valid_window", is_instance_valid(old_player))
	old_player.advance()
	_check("2.stale_advance_no_new_request", _req_log.size(), pre_size)
	_check("2.stale_advance_no_new_end", _end_n, pre_end)

	await _f2()
	_check("2.old_say_once_only", _count(_req_log, "say:OLD"), 1)
	_check("2.new_say_once", _count(_req_log, "say:NEW"), 1)
	# OLD의 종료 signal은 source guard로 차단되어 Manager end count에 안 잡힌다.
	_check("2.end_zero_after_stale", _end_n, 0)
	var new_player: DialoguePlayer = DialogueManager._ui.dialogue_player
	_check_true("2.distinct_players", new_player != old_player)
	new_player.advance()
	await _f2()
	_check("2.new_end_once", _end_n, 1)
	await _end_test()


# [3] Replace/stale-select while waiting for Choice: OLD Choice 대기 중 교체 + stale select.
func _test_replace_while_waiting_choice() -> void:
	print("[3] Replace/stale-select while waiting for Choice")
	_begin()
	DialogueManager.play(_choice_then_end("OLD_CHOICE"))
	await _f2()
	var old_player: DialoguePlayer = DialogueManager._ui.dialogue_player
	_check("3.old_waiting_choice", old_player.waiting_for, &"choice")
	_check("3.old_choice_offered", _count(_req_log, "choice"), 1)
	var pre_size := _req_log.size()
	var pre_end := _end_n

	DialogueManager.play(_say_then_end("NEW"))
	_check_true("3.old_player_valid_window", is_instance_valid(old_player))
	old_player.select_choice(0)
	_check("3.stale_select_no_new_request", _req_log.size(), pre_size)
	_check("3.stale_select_no_new_end", _end_n, pre_end)

	await _f2()
	_check("3.new_say_once", _count(_req_log, "say:NEW"), 1)
	_check("3.end_zero_after_stale", _end_n, 0)
	var new_player: DialoguePlayer = DialogueManager._ui.dialogue_player
	new_player.advance()
	await _f2()
	_check("3.new_end_once", _end_n, 1)
	await _end_test()


# [4] Same-frame latest-wins before deferred start: 같은 프레임 연속 play는 NEW만 시작한다.
# (provider side effect isolation은 시나리오 [7]이 spy로 검증한다.)
func _test_same_frame_latest_wins() -> void:
	print("[4] Same-frame latest-wins before deferred start")
	_begin()
	DialogueManager.play(_say_then_end("OLD"))
	DialogueManager.play(_say_then_end("NEW"))  # 같은 프레임 교체 — OLD deferred start 취소
	await _f2()
	_check("4.request_log_new_only", _req_log, ["say:NEW"])
	_check("4.old_say_zero", _count(_req_log, "say:OLD"), 0)
	_check("4.started_once", _started_n, 1)
	_check("4.no_end_yet", _end_n, 0)
	DialogueManager._ui.dialogue_player.advance()
	await _f2()
	_check("4.new_end_once", _end_n, 1)
	await _end_test()


# [5] ui_request callback reentry source guard: portrait_state callback에서 즉시 교체.
# old 후속 Say는 source guard로 차단되고 NEW Say만 전달되며 active _ui는 NEW다.
func _test_request_callback_reentry() -> void:
	print("[5] ui_request callback reentry source guard")
	_begin()
	var new_res := _say_then_end("NEW")
	var state := {"replaced": false}
	var reentry := func(req: Dictionary) -> void:
		if not state.replaced and req.get("type") == "portrait_state":
			state.replaced = true
			DialogueManager.play(new_res)  # request callback 한가운데서 즉시 교체
	DialogueManager.ui_request.connect(reentry)

	DialogueManager.play(_effect_then_say("OLD_AFTER_EFFECT"))
	var old_ui = DialogueManager._ui
	for i in range(6):
		await get_tree().process_frame
	DialogueManager.ui_request.disconnect(reentry)

	# old first effect(portrait_state)는 callback을 트리거하려고 전달될 수 있다.
	_check_true("5.portrait_seen", _req_log.has("portrait:show"))
	# 교체 이후 old player의 후속 Say는 차단된다.
	_check("5.old_after_effect_blocked", _count(_req_log, "say:OLD_AFTER_EFFECT"), 0)
	# NEW Say는 정확히 1회.
	_check("5.new_say_once", _count(_req_log, "say:NEW"), 1)
	# active _ui는 NEW UI.
	_check_true("5.ui_is_new", DialogueManager._ui != old_ui)
	_check_true("5.playing", DialogueManager.is_playing())
	await _end_test()


# [6] dialogue_end callback reentry: 종료 callback에서 새 대화를 시작해도 지워지지 않는다.
func _test_end_callback_reentry() -> void:
	print("[6] dialogue_end callback reentry")
	_begin()
	var next_res := _say_then_end("NEXT")
	var state := {"fired": false}
	# one-shot: 첫 end에서만 재진입한다(NEXT 종료가 같은 listener를 재발화하지 않도록).
	var reentry := func() -> void:
		if not state.fired:
			state.fired = true
			DialogueManager.play(next_res)
	DialogueManager.dialogue_end.connect(reentry)

	DialogueManager.play(_say_then_end("FIRST"))
	await _f2()
	_check("6.first_say_once", _count(_req_log, "say:FIRST"), 1)
	DialogueManager._ui.dialogue_player.advance()  # FIRST end -> reentry가 NEXT 시작
	await _f2()
	_check_true("6.playing_after_first_end", DialogueManager.is_playing())
	_check("6.first_end_counted", _end_n, 1)
	var pn: DialoguePlayer = DialogueManager._ui.dialogue_player
	_check("6.next_waiting_text", pn.waiting_for, &"text")
	_check("6.next_say_once", _count(_req_log, "say:NEXT"), 1)
	pn.advance()  # NEXT end
	await _f2()
	_check("6.second_end_once", _end_n, 2)

	DialogueManager.dialogue_end.disconnect(reentry)
	await _end_test()


# [7] Provider tuple isolation on replacement: same-frame 교체에서 OLD provider는 0회, NEW만 호출.
func _test_provider_isolation_on_replacement() -> void:
	print("[7] Provider tuple isolation on replacement (same-frame latest-wins)")
	_begin()
	var old_spy := SpyMutationProvider.new()
	var new_spy := SpyMutationProvider.new()

	DialogueManager.play(_provider_effect_graph("OLD_ISO"), null, old_spy)
	DialogueManager.play(_provider_effect_graph("NEW_ISO"), null, new_spy)  # 같은 프레임 교체
	await _f2()

	# OLD는 deferred start 취소로 시작조차 안 했으므로 provider/say 모두 0회.
	_check("7.old_provider_zero", old_spy.add_calls, 0)
	_check("7.old_say_zero", _count(_req_log, "say:OLD_ISO"), 0)
	# NEW의 Start Effect가 add_state를 1회 호출하고 NEW Say가 1회 전달된다.
	_check("7.new_provider_called_once", new_spy.add_calls, 1)
	_check("7.new_say_once", _count(_req_log, "say:NEW_ISO"), 1)
	_check("7.started_once", _started_n, 1)
	DialogueManager._ui.dialogue_player.advance()
	await _f2()
	_check("7.new_end_once", _end_n, 1)
	await _end_test()


# [8] Dismiss/null safety: _dismiss() 후 is_playing false. stale old 호출이 새 이벤트를 안 만든다.
func _test_dismiss_null_safety() -> void:
	print("[8] Dismiss/null safety")
	_begin()
	DialogueManager.play(_say_then_end("OLD"))
	await _f2()
	var old_player: DialoguePlayer = DialogueManager._ui.dialogue_player
	_check_true("8.playing_before_dismiss", DialogueManager.is_playing())
	_check("8.old_waiting_text", old_player.waiting_for, &"text")
	var pre_size := _req_log.size()
	var pre_end := _end_n

	DialogueManager._dismiss()
	_check_true("8.not_playing_after_dismiss", not DialogueManager.is_playing())
	# same-frame valid window: queue_free는 프레임 끝에 실제 해제되므로 아직 valid.
	_check_true("8.old_player_valid_window", is_instance_valid(old_player))
	old_player.advance()
	_check("8.stale_no_new_request", _req_log.size(), pre_size)
	_check("8.stale_no_new_end", _end_n, pre_end)

	await _f2()
	_check("8.still_no_new_request", _req_log.size(), pre_size)
	_check("8.still_no_new_end", _end_n, pre_end)
	await _end_test()
