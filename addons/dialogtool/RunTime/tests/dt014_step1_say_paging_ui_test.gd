# DT-014 Step 1 검증용 헤드리스 테스트(Real-UI Say Paging Regression Tests).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt014_step1_say_paging_ui_test.tscn
#
# 목표: 실제 Dialogue_UI.tscn 클릭 경로에서 한 줄/여러 줄/빈 줄/CRLF Say의 누적, 클릭 순서,
#       Flow 진행, 노드 전환 시 초기화, 반복/교체 시 상태 누수 없음을 단언하여
#       DT-003 Say 줄 누적 표시 기능을 회귀 검증한다.
extends Node

const UI_SCENE := "res://addons/dialogtool/UI/Dialogue_UI.tscn"

var _failures: int = 0


func _ready() -> void:
	_install_watchdog(15.0)
	await _run_all()

	if _failures == 0:
		print("[DT-014 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-014 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-014 Step1] WATCHDOG TIMEOUT after %.0fs" % seconds)
		get_tree().quit(2))


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# --- 헬퍼 함수들 ---

func _n(type: StringName, params: Dictionary = {}) -> Dictionary:
	return {"type": type, "params": params}


func _c(from_id: int, from_port: int, to_id: int, to_port: int) -> Dictionary:
	return {"from_node_id": from_id, "from_port": from_port, "to_node_id": to_id, "to_port": to_port}


func _resource(nodes: Dictionary, conns: Array) -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = nodes
	var typed: Array[Dictionary] = []
	for c in conns:
		typed.append(c)
	res.runtime_connections = typed
	res.start_node_id = 0
	return res


func _click_button(ui: DialogueUI) -> void:
	# Button.pressed.emit()을 호출하여 실제 연결된 핸들러(_on_button_pressed)를 시뮬레이션
	ui.get_node("Button").pressed.emit()
	# 타이핑 완성 및 advance 처리는 동기적이지만, 안전을 위해 1 process_frame 대기
	await get_tree().process_frame


func _setup_ui() -> DialogueUI:
	var ui: DialogueUI = load(UI_SCENE).instantiate()
	add_child(ui)
	# 타이핑 자동 진행을 차단하여 테스트 결정성을 확보함
	ui.say.set_process(false)
	return ui


func _cleanup_ui(ui: DialogueUI) -> void:
	if is_instance_valid(ui):
		ui.queue_free()
	await get_tree().process_frame


# --- 테스트 케이스 ---

func _run_all() -> void:
	await _test_case_1_single_line()
	await _test_case_2_multi_line_lf()
	await _test_case_3_middle_empty_line()
	await _test_case_3b_trailing_empty_line()
	await _test_case_4_crlf_normalization()
	await _test_case_6_reset_on_node_transition()
	await _test_case_7_no_leak_on_replay_or_replace()


# 1. 한 줄 Say(회귀 보존)
func _test_case_1_single_line() -> void:
	print("[1] 한 줄 Say: Hello")
	var ui := _setup_ui()
	
	# Start(0) -> Say(1) -> End(2)
	var res := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "Hello"}),
		2: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0)
	])
	
	ui.play(res)
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check("1.initial_text", ui.say.text, "Hello")
	_check("1.waiting_for", ui.dialogue_player.waiting_for, &"text")
	_check_true("1.visible_ratio_less_than_1", ui.say.visible_ratio < 1.0)
	
	# 클릭 1: 타이핑 완성
	await _click_button(ui)
	_check("1.click1_text", ui.say.text, "Hello")
	_check("1.click1_ratio", ui.say.visible_ratio, 1.0)
	_check("1.click1_waiting", ui.dialogue_player.waiting_for, &"text")
	_check("1.click1_current_node", ui.dialogue_player.current_node_id, 1)
	
	# 클릭 2: 다음 노드(End)로 진행
	await _click_button(ui)
	_check("1.click2_waiting", ui.dialogue_player.waiting_for, &"none")
	_check("1.click2_current_node", ui.dialogue_player.current_node_id, -1)
	_check_true("1.click2_ui_hidden", not ui.say_box.visible)
	
	await _cleanup_ui(ui)


# 2. 여러 줄 LF 누적
func _test_case_2_multi_line_lf() -> void:
	print("[2] 여러 줄 LF 누적: A\\nB\\nC")
	var ui := _setup_ui()
	
	var res := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "A\nB\nC"}),
		2: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0)
	])
	
	ui.play(res)
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check("2.line1_text", ui.say.text, "A")
	_check_true("2.line1_ratio_less_1", ui.say.visible_ratio < 1.0)
	_check("2.line1_waiting", ui.dialogue_player.waiting_for, &"text")
	
	# 클릭 1: 라인 1 타이핑 완성
	await _click_button(ui)
	_check("2.click1_text", ui.say.text, "A")
	_check("2.click1_ratio", ui.say.visible_ratio, 1.0)
	_check("2.click1_waiting", ui.dialogue_player.waiting_for, &"text")
	
	# 클릭 2: 라인 2 시작 (A\nB 누적)
	await _click_button(ui)
	_check("2.click2_text", ui.say.text, "A\nB")
	_check_true("2.click2_ratio_less_1", ui.say.visible_ratio < 1.0)
	_check("2.click2_waiting", ui.dialogue_player.waiting_for, &"text")
	
	# 클릭 3: 라인 2 타이핑 완성
	await _click_button(ui)
	_check("2.click3_text", ui.say.text, "A\nB")
	_check("2.click3_ratio", ui.say.visible_ratio, 1.0)
	
	# 클릭 4: 라인 3 시작 (A\nB\nC 누적)
	await _click_button(ui)
	_check("2.click4_text", ui.say.text, "A\nB\nC")
	_check_true("2.click4_ratio_less_1", ui.say.visible_ratio < 1.0)
	
	# 클릭 5: 라인 3 타이핑 완성
	await _click_button(ui)
	_check("2.click5_text", ui.say.text, "A\nB\nC")
	_check("2.click5_ratio", ui.say.visible_ratio, 1.0)
	_check("2.click5_waiting", ui.dialogue_player.waiting_for, &"text")
	_check("2.click5_current_node", ui.dialogue_player.current_node_id, 1)
	
	# 클릭 6: 마지막 줄 이후이므로 Flow 진행
	await _click_button(ui)
	_check("2.click6_waiting", ui.dialogue_player.waiting_for, &"none")
	_check("2.click6_current_node", ui.dialogue_player.current_node_id, -1)
	
	await _cleanup_ui(ui)


# 3. 빈 줄 포함(중간 빈 줄)
func _test_case_3_middle_empty_line() -> void:
	print("[3] 중간 빈 줄 보존: A\\n\\nC")
	var ui := _setup_ui()
	
	var res := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "A\n\nC"}),
		2: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0)
	])
	
	ui.play(res)
	await get_tree().process_frame
	await get_tree().process_frame
	
	# 라인 1: A
	_check("3.line1_text", ui.say.text, "A")
	await _click_button(ui) # 완성
	_check("3.line1_completed", ui.say.visible_ratio, 1.0)
	
	# 클릭 2: 라인 2 (빈 줄). visible_ratio는 즉시 1.0이어야 함
	await _click_button(ui)
	_check("3.line2_text", ui.say.text, "A\n")
	_check("3.line2_ratio", ui.say.visible_ratio, 1.0)
	_check("3.line2_waiting", ui.dialogue_player.waiting_for, &"text")
	
	# 클릭 3: 라인 3 (C)로 바로 진행 (빈 줄은 이미 완성 상태이므로 추가 완성 클릭 불필요)
	await _click_button(ui)
	_check("3.line3_text", ui.say.text, "A\n\nC")
	_check_true("3.line3_ratio_less_1", ui.say.visible_ratio < 1.0)
	
	# 클릭 4: 라인 3 완성
	await _click_button(ui)
	_check("3.line3_completed_ratio", ui.say.visible_ratio, 1.0)
	_check("3.line3_waiting", ui.dialogue_player.waiting_for, &"text")
	
	# 클릭 5: Flow 진행
	await _click_button(ui)
	_check("3.flow_ended", ui.dialogue_player.current_node_id, -1)
	
	await _cleanup_ui(ui)


# 3b. 빈 줄 포함(끝 빈 줄)
func _test_case_3b_trailing_empty_line() -> void:
	print("[3b] 끝 빈 줄 보존: A\\nB\\n")
	var ui := _setup_ui()
	
	var res := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "A\nB\n"}),
		2: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0)
	])
	
	ui.play(res)
	await get_tree().process_frame
	await get_tree().process_frame
	
	# 라인 1: A
	_check("3b.line1_text", ui.say.text, "A")
	await _click_button(ui) # 완성
	
	# 라인 2: B
	await _click_button(ui)
	_check("3b.line2_text", ui.say.text, "A\nB")
	await _click_button(ui) # 완성
	_check("3b.line2_completed_ratio", ui.say.visible_ratio, 1.0)
	
	# 클릭 4: 라인 3 (끝 빈 줄). visible_ratio는 즉시 1.0이어야 함
	await _click_button(ui)
	_check("3b.line3_text", ui.say.text, "A\nB\n")
	_check("3b.line3_ratio", ui.say.visible_ratio, 1.0)
	_check("3b.line3_waiting", ui.dialogue_player.waiting_for, &"text")
	_check("3b.line3_current_node", ui.dialogue_player.current_node_id, 1)
	
	# 클릭 5: 끝 빈 줄 표시 완료 상태에서 클릭했으므로 Flow 진행
	await _click_button(ui)
	_check("3b.flow_ended", ui.dialogue_player.current_node_id, -1)
	
	await _cleanup_ui(ui)


# 4. CRLF 및 CR 정규화
func _test_case_4_crlf_normalization() -> void:
	print("[4] CRLF 및 CR 정규화: A\\r\\nB 및 C\\rD")
	var ui := _setup_ui()
	
	var res := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "A\r\nB"}),
		2: _n(&"say", {"text": "C\rD"}),
		3: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0),
		_c(2, 0, 3, 0)
	])
	
	ui.play(res)
	await get_tree().process_frame
	await get_tree().process_frame
	
	# 1번 노드(A\r\nB): 첫 줄 A
	_check("4.node1_line1_text", ui.say.text, "A")
	await _click_button(ui) # 완성
	await _click_button(ui) # 둘째 줄 B
	_check("4.node1_line2_text", ui.say.text, "A\nB")
	await _click_button(ui) # 완성
	
	# 1번 노드 종료 -> 2번 노드로 진행
	await _click_button(ui)
	_check("4.node2_line1_text", ui.say.text, "C")
	await _click_button(ui) # 완성
	await _click_button(ui) # 둘째 줄 D
	_check("4.node2_line2_text", ui.say.text, "C\nD")
	await _click_button(ui) # 완성
	
	# 2번 노드 종료 -> End
	await _click_button(ui)
	_check("4.flow_ended", ui.dialogue_player.current_node_id, -1)
	
	await _cleanup_ui(ui)


# 6. 노드 전환 시 줄/페이지 상태 초기화
func _test_case_6_reset_on_node_transition() -> void:
	print("[6] 노드 전환 시 줄/페이지 상태 초기화")
	var ui := _setup_ui()
	
	# 6.1 Say(여러 줄) -> Say(여러 줄)
	var res_say_say := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "A\nB"}),
		2: _n(&"say", {"text": "C\nD"}),
		3: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0),
		_c(2, 0, 3, 0)
	])
	
	ui.play(res_say_say)
	await get_tree().process_frame
	await get_tree().process_frame
	
	# A\nB 진행
	await _click_button(ui) # A 완성
	await _click_button(ui) # B 시작
	await _click_button(ui) # B 완성
	await _click_button(ui) # Say(1) -> Say(2) 전환
	
	# 두 번째 Say 시작 시 텍스트가 첫 Say 잔여 없이 깨끗하게 시작되는지 단언
	_check("6.say_say_transition_text", ui.say.text, "C")
	_check("6.say_say_transition_index", ui._say_line_index, 0)
	_check("6.say_say_transition_visible_text", ui._say_visible_text, "C")
	
	# D까지 완전히 끝내기
	await _click_button(ui)
	await _click_button(ui)
	await _click_button(ui)
	await _click_button(ui) # End
	
	# 6.2 Say(여러 줄) -> Choice
	var res_say_choice := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "A\nB"}),
		2: _n(&"choice", {"choices": ["Option 1"]}),
		3: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0),
		_c(2, 0, 3, 0) # Choice의 첫 번째 항목(index 0)의 flow 출력 포트는 port 0
	])
	
	ui.play(res_say_choice)
	await get_tree().process_frame
	await get_tree().process_frame
	
	await _click_button(ui) # A 완성
	await _click_button(ui) # B 시작
	await _click_button(ui) # B 완성
	await _click_button(ui) # Say(1) -> Choice(2) 전환
	
	# Choice 표시 시 페이징 상태 초기화 확인
	_check("6.choice_transition_index", ui._say_line_index, -1)
	_check("6.choice_transition_visible_text", ui._say_visible_text, "")
	_check_true("6.choice_transition_lines_empty", ui._say_lines.is_empty())
	
	# 선택지 선택해서 종료까지 진행
	ui._on_choice_button_pressed(0)
	await get_tree().process_frame
	
	# 6.3 Say(여러 줄) -> End 후 상태 초기화
	var res_say_end := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "A\nB"}),
		2: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0)
	])
	
	ui.play(res_say_end)
	await get_tree().process_frame
	await get_tree().process_frame
	
	await _click_button(ui) # A 완성
	await _click_button(ui) # B 시작
	await _click_button(ui) # B 완성
	await _click_button(ui) # Say(1) -> End(2)
	
	# 대화 종료 후 페이징 상태 초기화 확인
	_check("6.end_transition_index", ui._say_line_index, -1)
	_check("6.end_transition_visible_text", ui._say_visible_text, "")
	_check_true("6.end_transition_lines_empty", ui._say_lines.is_empty())
	
	await _cleanup_ui(ui)


# 7. 반복 실행/교체 시 줄 index 누수 없음
func _test_case_7_no_leak_on_replay_or_replace() -> void:
	print("[7] 반복 실행/교체 시 줄 index 누수 없음")
	var ui := _setup_ui()
	
	var res_multiple := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "A\nB\nC"}),
		2: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0)
	])
	
	var res_other := _resource({
		0: _n(&"start"),
		1: _n(&"say", {"text": "X\nY"}),
		2: _n(&"end")
	}, [
		_c(0, 0, 1, 0),
		_c(1, 0, 2, 0)
	])
	
	# 7.1 같은 ui로 끝까지 진행한 뒤 play() 다시 호출 시 누수 없음
	ui.play(res_multiple)
	await get_tree().process_frame
	await get_tree().process_frame
	
	await _click_button(ui) # A 완성
	await _click_button(ui) # B 시작
	await _click_button(ui) # B 완성
	await _click_button(ui) # C 시작
	await _click_button(ui) # C 완성
	await _click_button(ui) # End 종료
	
	# 새 대화 시작
	ui.play(res_other)
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check("7.replay_initial_text", ui.say.text, "X")
	_check("7.replay_initial_index", ui._say_line_index, 0)
	_check("7.replay_initial_visible_text", ui._say_visible_text, "X")
	
	await _click_button(ui) # X 완성
	await _click_button(ui) # Y 시작
	await _click_button(ui) # Y 완성
	await _click_button(ui) # End 종료
	
	# 7.2 중간 줄에서 멈춘 채 교체 호출 시 누수 없음
	ui.play(res_multiple)
	await get_tree().process_frame
	await get_tree().process_frame
	
	await _click_button(ui) # A 완성
	await _click_button(ui) # B 시작
	# B가 시작된 중간 상태에서 play()로 덮어쓰기
	ui.play(res_other)
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check("7.replace_initial_text", ui.say.text, "X")
	_check("7.replace_initial_index", ui._say_line_index, 0)
	_check("7.replace_initial_visible_text", ui._say_visible_text, "X")
	
	await _cleanup_ui(ui)
