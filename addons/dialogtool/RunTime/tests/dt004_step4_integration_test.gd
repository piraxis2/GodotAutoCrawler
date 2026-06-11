# DT-004 Step 4 통합 런타임/UI/수명주기 테스트.
# 실행: godot --headless res://addons/dialogtool/RunTime/tests/dt004_step4_integration_test.tscn
#
# 통합 시나리오를 실제 DialogueUI/DialogueManager를 통해 실행해 Effect(portrait_state)와
# Say/Choice가 서로 간섭하지 않고, 반복/교체/재진입에서 Portrait 상태가 남지 않으며,
# 기존 직렬/무 Portrait 그래프가 회귀 없이 동작하는지 검증한다.
extends Node

const UI_SCENE := "res://addons/dialogtool/UI/Dialogue_UI.tscn"

var _failures: int = 0


func _ready() -> void:
	await _run_all()
	if _failures == 0:
		print("[DT-004 Step4-Integration] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-004 Step4-Integration] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _run_all() -> void:
	await _test_core_scenario()
	await _test_regression_serial()
	await _test_regression_no_portrait()
	await _test_regression_legacy_tres()
	await _test_lifecycle_repeat()
	await _test_lifecycle_replace()
	await _test_lifecycle_effect_callback_replace()
	await _test_lifecycle_reentry()


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


# --- 스냅샷 빌더 ------------------------------------------------------

func _n(type: StringName, params: Dictionary = {}) -> Dictionary:
	return {"type": type, "params": params}


func _c(from_id: int, to_id: int, kind: String = "", from_port: int = 0) -> Dictionary:
	var d := {"from_node_id": from_id, "from_port": from_port, "to_node_id": to_id, "to_port": 0}
	if kind != "":
		d["kind"] = kind
	return d


# 통합 시나리오: Start[effect: show left/right, flow: Say], Say[effect: expr left/hide right, flow: Choice], Choice->End.
func _make_full() -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = {
		0: _n(&"start"),
		1: _n(&"portrait_show", {"slot": "left"}),
		2: _n(&"portrait_show", {"slot": "right"}),
		3: _n(&"say", {"text": "hello"}),
		4: _n(&"portrait_expression", {"slot": "left", "expression": "happy"}),
		5: _n(&"portrait_hide", {"slot": "right"}),
		6: _n(&"choice", {"choices": ["ok"]}),
		7: _n(&"end"),
	}
	var conns: Array[Dictionary] = [
		_c(0, 1, "effect"), _c(0, 2, "effect"), _c(0, 3),
		_c(3, 4, "effect"), _c(3, 5, "effect"), _c(3, 6),
		_c(6, 7),
	]
	res.runtime_connections = conns
	res.start_node_id = 0
	return res


func _make_serial() -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = {
		0: _n(&"start"),
		1: _n(&"portrait_show", {"slot": "center"}),
		2: _n(&"say", {"text": "hi"}),
		3: _n(&"end"),
	}
	var conns: Array[Dictionary] = [_c(0, 1), _c(1, 2), _c(2, 3)]
	res.runtime_connections = conns
	res.start_node_id = 0
	return res


func _make_no_portrait() -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = {
		0: _n(&"start"),
		1: _n(&"say", {"text": "hi"}),
		2: _n(&"end"),
	}
	var conns: Array[Dictionary] = [_c(0, 1), _c(1, 2)]
	res.runtime_connections = conns
	res.start_node_id = 0
	return res


# 식별 가능한 Say 텍스트를 가진 단순 그래프(start -> say(text) -> end). 교체 회귀에서
# OLD/NEW 요청을 내용으로 구분하기 위해 사용한다.
func _make_marked(text: String) -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = {
		0: _n(&"start"),
		1: _n(&"say", {"text": text}),
		2: _n(&"end"),
	}
	var conns: Array[Dictionary] = [_c(0, 1), _c(1, 2)]
	res.runtime_connections = conns
	res.start_node_id = 0
	return res


func _summarize_marked(req: Dictionary) -> String:
	match req.get("type", "?"):
		"portrait_state": return "portrait:%s" % req.get("slot")
		"display_text": return "say:%s" % req.get("say", "")
		"offer_choice": return "choice"
		_: return req.get("type", "?")


func _summarize(req: Dictionary) -> String:
	match req.get("type", "?"):
		"portrait_state": return "%s:%s" % [req.get("action"), req.get("slot")]
		"display_text": return "say"
		"offer_choice": return "choice"
		_: return req.get("type", "?")


func _make_ui() -> DialogueUI:
	var ui: DialogueUI = load(UI_SCENE).instantiate()
	add_child(ui)
	await get_tree().process_frame
	return ui


# --- 시나리오 ---------------------------------------------------------

func _test_core_scenario() -> void:
	print("[B] 통합: 두 Effect 지점 + Say/Choice 간섭 없음 (DialogueUI)")
	var ui := await _make_ui()
	var log: Array = []
	ui.ui_request.connect(func(r: Dictionary): log.append(_summarize(r)))

	ui.play(_make_full())
	await get_tree().process_frame  # deferred start_dialogue

	# Start Effect 적용 후 Say 표시. Say는 Portrait를 건드리지 않는다.
	_check("B.start.left_shown", ui._portrait_state.has("left"), true)
	_check("B.start.right_shown", ui._portrait_state.has("right"), true)
	_check("B.start.say_visible", ui.say_box.visible, true)
	_check("B.start.choice_hidden", ui.choice_box.visible, false)

	ui.dialogue_player.advance()  # Say -> Say Effect -> Choice

	_check("B.adv.left_kept", ui._portrait_state.has("left"), true)
	_check("B.adv.right_hidden", ui._portrait_state.has("right"), false)
	_check("B.adv.left_expression", ui._portrait_state["left"].get("expression"), "happy")
	_check("B.adv.right_rect_hidden", ui._portraits.get_node("right").visible, false)
	_check("B.adv.choice_visible", ui.choice_box.visible, true)
	_check("B.adv.say_hidden", ui.say_box.visible, false)

	ui.dialogue_player.select_choice(0)  # Choice -> End -> 종료

	_check("B.end.portraits_cleared", ui._portrait_state.is_empty(), true)
	# Effect와 Say/Choice가 정확히 한 번씩, 중복 없이 발행됐는지.
	_check("B.request_log", log, ["show:left", "show:right", "say", "expression:left", "hide:right", "choice"])

	ui.queue_free()
	await get_tree().process_frame


func _test_regression_serial() -> void:
	print("[D1] 회귀: 기존 직렬 Portrait -> Say")
	var ui := await _make_ui()
	ui.play(_make_serial())
	await get_tree().process_frame
	_check("D1.center_shown", ui._portrait_state.has("center"), true)
	_check("D1.say_visible", ui.say_box.visible, true)
	ui.dialogue_player.advance()  # Say -> End
	_check("D1.ended_cleared", ui._portrait_state.is_empty(), true)
	ui.queue_free()
	await get_tree().process_frame


func _test_regression_no_portrait() -> void:
	print("[D2] 회귀: Portrait 없는 그래프")
	var ui := await _make_ui()
	ui.play(_make_no_portrait())
	await get_tree().process_frame
	_check("D2.no_portraits", ui._portrait_state.is_empty(), true)
	_check("D2.say_visible", ui.say_box.visible, true)
	ui.dialogue_player.advance()  # Say -> End
	_check("D2.ended", ui._portrait_state.is_empty(), true)
	ui.queue_free()
	await get_tree().process_frame


func _test_regression_legacy_tres() -> void:
	print("[D3] 회귀: 기존 pride_and_prejudice.tres 로드·실행(데이터 보존)")
	var res: Resource = ResourceLoader.load("res://pride_and_prejudice.tres", "", ResourceLoader.CACHE_MODE_IGNORE)
	_check("D3.loaded", res is DialogueGraphResource, true)
	if not (res is DialogueGraphResource):
		return

	# 데이터 보존: 노드 타입 구성과 legacy Say 필드가 그대로 로드돼야 한다.
	var type_counts := {}
	var first_say_speaker := ""
	var first_say_has_portrait_field := false
	for id in res.runtime_nodes:
		var node = res.runtime_nodes[id]
		var t = node.get("type")
		type_counts[t] = type_counts.get(t, 0) + 1
		if t == &"say" and first_say_speaker == "":
			var p: Dictionary = node.get("params", {})
			first_say_speaker = str(p.get("speaker", ""))
			first_say_has_portrait_field = p.has("portrait")
	_check("D3.start_count", type_counts.get(&"start", 0), 1)
	_check("D3.say_count", type_counts.get(&"say", 0), 8)
	_check("D3.end_count", type_counts.get(&"end", 0), 1)
	_check("D3.legacy_speaker_preserved", first_say_speaker, "엘리자베스")
	_check("D3.legacy_portrait_field_preserved", first_say_has_portrait_field, true)

	# 실제 실행: 종료까지 진행, Effect(portrait_state) 요청은 발생하지 않아야 한다(legacy=Effect 없음).
	var ui := await _make_ui()
	var saw_portrait := {"v": false}
	ui.ui_request.connect(func(r: Dictionary):
		if r.get("type") == "portrait_state":
			saw_portrait.v = true)
	ui.play(res)
	await get_tree().process_frame
	_check("D3.first_say_visible", ui.say_box.visible, true)
	_check("D3.first_speaker_rendered", ui.speaker.text, "엘리자베스")

	# 모든 Say를 지나 End까지 진행(무한 루프 방지 상한).
	for i in range(30):
		if ui.dialogue_player.current_node_id == -1:
			break
		ui.dialogue_player.advance()
		await get_tree().process_frame
	_check("D3.ran_to_end", ui.dialogue_player.current_node_id, -1)
	_check("D3.no_portrait_requests", saw_portrait.v, false)
	_check("D3.no_data_loss_portraits_clear", ui._portrait_state.is_empty(), true)

	ui.queue_free()
	await get_tree().process_frame


func _test_lifecycle_repeat() -> void:
	print("[C1] 수명주기: 같은 UI 반복 실행 시 이전 Portrait 잔존 없음")
	var ui := await _make_ui()
	# 1회차: 끝까지 진행.
	ui.play(_make_full())
	await get_tree().process_frame
	ui.dialogue_player.advance()
	ui.dialogue_player.select_choice(0)
	_check("C1.run1_cleared", ui._portrait_state.is_empty(), true)
	# 2회차: 같은 UI 재사용. play가 Portrait를 먼저 정리하므로 깨끗한 상태로 시작.
	ui.play(_make_full())
	await get_tree().process_frame
	_check("C1.run2_left", ui._portrait_state.has("left"), true)
	_check("C1.run2_right", ui._portrait_state.has("right"), true)
	_check("C1.run2_no_stale_center", ui._portrait_state.has("center"), false)
	ui.queue_free()
	await get_tree().process_frame


func _test_lifecycle_replace() -> void:
	print("[C2] 수명주기: 실행 중 다른 대화로 교체(DialogueManager)")
	DialogueManager.play(_make_full())
	await get_tree().process_frame
	await get_tree().process_frame
	var ui1 = DialogueManager._ui
	_check("C2.first_portraits", ui1._portrait_state.size(), 2)

	# 교체: Portrait 없는 대화로. 이전 Portrait 상태가 새 UI에 남으면 안 된다.
	DialogueManager.play(_make_no_portrait())
	await get_tree().process_frame
	await get_tree().process_frame
	var ui2 = DialogueManager._ui
	_check("C2.new_ui", ui2 != ui1, true)
	_check("C2.clean", ui2._portrait_state.is_empty(), true)
	_check("C2.playing", DialogueManager.is_playing(), true)

	ui2.dialogue_player.advance()  # Say -> End
	await get_tree().process_frame
	_check("C2.ended", DialogueManager.is_playing(), false)


func _test_lifecycle_effect_callback_replace() -> void:
	print("[C4] 수명주기: Effect 콜백 중 즉시 교체 시 OLD 차단 / NEW 전달(source guard 회귀)")
	# DT-002 P1 조건: portrait_state 콜백 안에서 play()로 교체하면, 이전 Player가 같은 실행
	# 루프를 계속해 stale Say를 발행한다. DialogueManager의 source guard가 그것을 버려야 한다.
	var log: Array = []
	var state := {"replaced": false}
	var new_res := _make_marked("NEW")
	var cb := func(req: Dictionary):
		log.append(_summarize_marked(req))
		if not state.replaced and req.get("type") == "portrait_state":
			state.replaced = true
			DialogueManager.play(new_res)  # Effect 콜백 한가운데서 즉시 교체
	DialogueManager.ui_request.connect(cb)

	DialogueManager.play(_make_full())  # Start Effect(show left/right) 후 Say "hello"
	for i in range(6):
		await get_tree().process_frame
	DialogueManager.ui_request.disconnect(cb)

	# OLD의 첫 Effect(show:left)는 교체 전에 전달되지만, 교체 후 OLD의 후속 요청은 차단돼야 한다.
	_check("C4.old_first_effect_delivered", log.has("portrait:left"), true)
	_check("C4.old_later_effect_blocked", log.has("portrait:right"), false)
	_check("C4.old_stale_say_blocked", log.has("say:hello"), false)
	_check("C4.new_delivered", log.has("say:NEW"), true)

	# 정리.
	if DialogueManager.is_playing():
		DialogueManager._ui.dialogue_player.advance()
		await get_tree().process_frame


func _test_lifecycle_reentry() -> void:
	print("[C3] 수명주기: 종료 callback에서 새 대화 시작(재진입)")
	var simple := _make_no_portrait()
	var cb := func(): DialogueManager.play(simple)
	DialogueManager.dialogue_end.connect(cb)

	DialogueManager.play(_make_full())
	await get_tree().process_frame
	await get_tree().process_frame
	var ui1 = DialogueManager._ui
	ui1.dialogue_player.advance()         # Say Effect -> Choice
	ui1.dialogue_player.select_choice(0)  # Choice -> End -> dialogue_end -> cb가 새 대화 시작
	await get_tree().process_frame
	await get_tree().process_frame

	_check("C3.reentry_playing", DialogueManager.is_playing(), true)
	_check("C3.reentry_clean", DialogueManager._ui._portrait_state.is_empty(), true)

	DialogueManager.dialogue_end.disconnect(cb)
	if DialogueManager.is_playing():
		DialogueManager._ui.dialogue_player.advance()  # 정리
		await get_tree().process_frame
