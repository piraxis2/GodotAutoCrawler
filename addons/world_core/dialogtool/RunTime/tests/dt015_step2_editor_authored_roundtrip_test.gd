# DT-015 Step 2 검증용 헤드리스 테스트(Editor Authored Round-Trip).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/dialogtool/RunTime/tests/dt015_step2_editor_authored_roundtrip_test.tscn
#
# 목표:
# - 실제 dialoguetool_main.tscn fixture에서 canonical graph를 에디터 노드로 동적 작성한다.
# - capture_current_graphedit() -> ResourceSaver.save -> CACHE_MODE_IGNORE reload -> runtime execution을 검증한다.
# - 에디터 authored graph가 Step 1과 동일하게 Strong/Weak/Leave 세 경로의 Say sequence와 End 도달 결과를 내는지 입증한다.

extends Node

const MAIN_SCENE := "res://addons/world_core/dialogtool/dialoguetool_main.tscn"
const TEST_SAVE_PATH := "res://__dt015_step2_graph.tres"

var _failures: int = 0
var _ui_requests: Array[Dictionary] = []
var _dialogue_end_emitted := false


func _ready() -> void:
	# 30초 watchdog 타이머 설정 (데드락 방지)
	_install_watchdog(30.0)
	
	# 모든 테스트 시나리오 실행
	await _run_all()
	
	# 테스트 자원 정리
	_cleanup()
	
	if _failures == 0:
		print("[DT-015 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-015 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# 데드락 및 무한 루프 방지를 위한 watchdog
func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-015 Step2] WATCHDOG TIMEOUT after %.0fs — 테스트 시간 초과로 강제 종료합니다." % seconds)
		_cleanup()
		get_tree().quit(2))


# 단언문(Assertion) 헬퍼
func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# UI 요청 캡처용 리스너 설정
func _setup_manager_listeners() -> void:
	_ui_requests.clear()
	_dialogue_end_emitted = false
	if DialogueManager.ui_request.is_connected(_on_ui_request):
		DialogueManager.ui_request.disconnect(_on_ui_request)
	if DialogueManager.dialogue_end.is_connected(_on_dialogue_end):
		DialogueManager.dialogue_end.disconnect(_on_dialogue_end)
		
	DialogueManager.ui_request.connect(_on_ui_request)
	DialogueManager.dialogue_end.connect(_on_dialogue_end)


func _on_ui_request(req: Dictionary) -> void:
	_ui_requests.append(req)


func _on_dialogue_end() -> void:
	_dialogue_end_emitted = true


# --- 에디터 씬 구동 헬퍼 ---

func _make_editor() -> GraphEdit:
	var root: Node = load(MAIN_SCENE).instantiate()
	add_child(root)
	await get_tree().process_frame
	await get_tree().process_frame
	return root.find_child("GraphEdit", true, false)


func _free_editor(ge: GraphEdit) -> void:
	var root: Node = ge
	while root.get_parent() != null and root.get_parent() != self:
		root = root.get_parent()
	root.queue_free()


func _add_def_node(ge: GraphEdit, def: DialogueDefinition, id: int) -> DialogueNode:
	var node: DialogueNode = load(def._get_dialogue_node()).instantiate()
	def.node_id = id
	def.graph_resource = weakref(ge.graph_resource)
	node.definition = def
	node.name = str(id)
	node.id = id
	ge.add_child(node)
	node.set_owner(ge)
	return node


# 1. 에디터 노드 생성 및 연결을 통한 그래프 작성 (캡처 후 저장)
func _test_editor_authored_build() -> void:
	print("[1] Editor Authored Graph Build")
	var ge := await _make_editor()
	_check_true("GraphEdit is valid", ge != null)
	
	# 0: Start
	var start_node = _add_def_node(ge, StartDef.new(), 0)
	
	# 1: Say "Intro"
	var intro_def = SayDef.new()
	intro_def.speaker = "Narrator"
	intro_def.say_text = "Intro"
	var intro_node = _add_def_node(ge, intro_def, 1)
	
	# 2: Choice ["Strong", "Weak", "Leave"]
	var choice_def = ChoiceDef.new()
	var choice_node = _add_def_node(ge, choice_def, 2)
	choice_node.slider.value = 3
	choice_node.update_item(3)
	
	var choice_items = []
	for child in choice_node.get_children():
		if child is ChoiceItem:
			choice_items.append(child)
	choice_items[0].text_edit.text = "Strong"
	choice_items[1].text_edit.text = "Weak"
	choice_items[2].text_edit.text = "Leave"
	
	# 3: Branch (Strong)
	var branch_strong_node = _add_def_node(ge, BranchDef.new(), 3)
	
	# 4: Variable (7)
	var var_strong_def = VariableDef.new()
	var_strong_def.variable_name = "strength"
	var_strong_def.variable_type = VariableDef.AllowedVariables.INT
	var_strong_def.variable = 7
	var var_strong_node = _add_def_node(ge, var_strong_def, 4)
	
	# 5: Expression (A >= 5)
	var expr_strong_def = ExpressionValueDef.new()
	expr_strong_def.expression_string = "A >= 5"
	var expr_strong_node = _add_def_node(ge, expr_strong_def, 5)
	expr_strong_node.slider.value = 1
	expr_strong_node.on_slider_update(1)
	
	# 6: Say "Strong success"
	var say_strong_success_def = SayDef.new()
	say_strong_success_def.speaker = "Narrator"
	say_strong_success_def.say_text = "Strong success"
	var say_strong_success_node = _add_def_node(ge, say_strong_success_def, 6)
	
	# 7: Say "Strong fail"
	var say_strong_fail_def = SayDef.new()
	say_strong_fail_def.speaker = "Narrator"
	say_strong_fail_def.say_text = "Strong fail"
	var say_strong_fail_node = _add_def_node(ge, say_strong_fail_def, 7)
	
	# 8: End
	var end_node = _add_def_node(ge, EndDef.new(), 8)
	
	# 9: Branch (Weak)
	var branch_weak_node = _add_def_node(ge, BranchDef.new(), 9)
	
	# 10: Variable (3)
	var var_weak_def = VariableDef.new()
	var_weak_def.variable_name = "strength"
	var_weak_def.variable_type = VariableDef.AllowedVariables.INT
	var_weak_def.variable = 3
	var var_weak_node = _add_def_node(ge, var_weak_def, 10)
	
	# 11: Expression (A >= 5)
	var expr_weak_def = ExpressionValueDef.new()
	expr_weak_def.expression_string = "A >= 5"
	var expr_weak_node = _add_def_node(ge, expr_weak_def, 11)
	expr_weak_node.slider.value = 1
	expr_weak_node.on_slider_update(1)
	
	# 12: Say "Weak success"
	var say_weak_success_def = SayDef.new()
	say_weak_success_def.speaker = "Narrator"
	say_weak_success_def.say_text = "Weak success"
	var say_weak_success_node = _add_def_node(ge, say_weak_success_def, 12)
	
	# 13: Say "Weak fail"
	var say_weak_fail_def = SayDef.new()
	say_weak_fail_def.speaker = "Narrator"
	say_weak_fail_def.say_text = "Weak fail"
	var say_weak_fail_node = _add_def_node(ge, say_weak_fail_def, 13)
	
	# 14: Say "Leave"
	var say_leave_def = SayDef.new()
	say_leave_def.speaker = "Narrator"
	say_leave_def.say_text = "Leave"
	var say_leave_node = _add_def_node(ge, say_leave_def, 14)
	
	await get_tree().process_frame
	await get_tree().process_frame
	
	# --- 에디터 연결 설정 ---
	# Flow connections
	ge.connect_node("0", 0, "1", 0)
	ge.connect_node("1", 0, "2", 0)
	
	# Choice outputs (Choice port = item index)
	ge.connect_node("2", 0, "3", 1) # Strong -> Branch Flow input port (1)
	ge.connect_node("2", 1, "9", 1) # Weak -> Branch Flow input port (1)
	ge.connect_node("2", 2, "14", 0) # Leave -> Say "Leave" (0)
	
	# Strong Branch flow
	ge.connect_node("3", 0, "6", 0)
	ge.connect_node("3", 1, "7", 0)
	ge.connect_node("6", 0, "8", 0)
	ge.connect_node("7", 0, "8", 0)
	
	# Strong Data connections
	ge.connect_node("4", 0, "5", 0) # Variable -> Expression
	ge.connect_node("5", 0, "3", 0) # Expression -> Branch condition
	
	# Weak Branch flow
	ge.connect_node("9", 0, "12", 0)
	ge.connect_node("9", 1, "13", 0)
	ge.connect_node("12", 0, "8", 0)
	ge.connect_node("13", 0, "8", 0)
	
	# Weak Data connections
	ge.connect_node("10", 0, "11", 0) # Variable -> Expression
	ge.connect_node("11", 0, "9", 0) # Expression -> Branch condition
	
	# Leave flow
	ge.connect_node("14", 0, "8", 0)
	
	await get_tree().process_frame
	await get_tree().process_frame
	
	# 캡처 검증
	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	_check_true("Editor pre-validation status", ge._validate_runtime_snapshot(captured))
	
	# 파일 저장
	var err := ResourceSaver.save(captured, TEST_SAVE_PATH)
	_check("ResourceSaver.save status for authored graph", err, OK)
	
	_free_editor(ge)
	await get_tree().process_frame


# 2. 로드 및 노드/연결 상태 단언
func _test_save_reload() -> DialogueGraphResource:
	print("[2] Save & Reload verify")
	var reloaded := ResourceLoader.load(TEST_SAVE_PATH, "", ResourceLoader.CACHE_MODE_IGNORE) as DialogueGraphResource
	_check_true("Reloaded resource is valid", reloaded != null)
	if reloaded == null:
		return null
		
	_check("Reloaded start_node_id", reloaded.start_node_id, 0)
	_check("Reloaded runtime_nodes count", reloaded.runtime_nodes.size(), 15)
	_check("Reloaded runtime_connections count", reloaded.runtime_connections.size(), 18)
	
	# Choice 텍스트 파라미터가 보존되었는지 단언
	var choice_params = reloaded.runtime_nodes[2]["params"]
	_check("Choice params choices list", choice_params.get("choices"), ["Strong", "Weak", "Leave"])
	
	# Expression 입력 키가 A로 보존되었는지 단언 (자동 입력명 A 검사)
	var expr_strong_params = reloaded.runtime_nodes[5]["params"]
	_check("Strong Expression inputs list", expr_strong_params.get("inputs"), ["A"])
	_check("Strong Expression string", expr_strong_params.get("expression"), "A >= 5")
	
	var expr_weak_params = reloaded.runtime_nodes[11]["params"]
	_check("Weak Expression inputs list", expr_weak_params.get("inputs"), ["A"])
	_check("Weak Expression string", expr_weak_params.get("expression"), "A >= 5")
	
	return reloaded


# 3. Choice Flow 보존 여부 단언
func _test_choice_flow_preservation(reloaded: DialogueGraphResource) -> void:
	print("[3] Choice Flow preservation verify")
	# Choice 노드는 ID 2
	var next_0 = reloaded.get_runtime_next_node_id(2, 0)
	var next_1 = reloaded.get_runtime_next_node_id(2, 1)
	var next_2 = reloaded.get_runtime_next_node_id(2, 2)
	
	_check("Choice port 0 (Strong) routes to Strong Branch", next_0, 3)
	_check("Choice port 1 (Weak) routes to Weak Branch", next_1, 9)
	_check("Choice port 2 (Leave) routes to Leave Say", next_2, 14)
	
	# Leave Say node(14) 타입 확인
	var leave_node = reloaded.runtime_nodes.get(14, {})
	_check("Leave node type is say", leave_node.get("type"), &"say")
	
	# Leave Say(14) -> End(8) 직결 확인 (Branch 우회 단언)
	var leave_next = reloaded.get_runtime_next_node_id(14, 0)
	_check("Leave Say routes to End", leave_next, 8)


# 4. 특정 Choice 분기 테스트 헬퍼
func _run_route(res: DialogueGraphResource, choice_idx: int, expected_say: String) -> void:
	_setup_manager_listeners()
	DialogueManager._dismiss()
	
	DialogueManager.play(res)
	
	# deferred start 대기
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check_true("Intro Say request received", _ui_requests.size() > 0)
	if _ui_requests.size() > 0:
		var req = _ui_requests.back()
		_check("First request type is display_text", req.get("type"), "display_text")
		_check("First text is Intro", req.get("say"), "Intro")
		
	# Advance
	var player = DialogueManager._ui.dialogue_player
	player.advance()
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check_true("Choice request received", _ui_requests.size() > 1)
	if _ui_requests.size() > 1:
		var req = _ui_requests.back()
		_check("Second request type is offer_choice", req.get("type"), "offer_choice")
		_check("Choices are correct", req.get("choices"), ["Strong", "Weak", "Leave"])
		
	# Select choice
	player.select_choice(choice_idx)
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check_true("Branch outcome Say request received", _ui_requests.size() > 2)
	if _ui_requests.size() > 2:
		var req = _ui_requests.back()
		_check("Third request type is display_text", req.get("type"), "display_text")
		_check("Outcome text is correct", req.get("say"), expected_say)
		
	# Final advance to End
	player.advance()
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check_true("Dialogue end emitted", _dialogue_end_emitted)


# 전체 시나리오 조율
func _run_all() -> void:
	# 1. 에디터 상에서 노드 배치 및 캡처 저장
	await _test_editor_authored_build()
	
	# 2. 로드 검증
	var reloaded := _test_save_reload()
	if reloaded == null:
		return
		
	# 3. Choice Flow 보존 확인
	_test_choice_flow_preservation(reloaded)
	
	# 4. 3개 분기 정상 실행 확인
	print("[4] Reloaded Authored Graph Strong route test")
	await _run_route(reloaded, 0, "Strong success")
	
	print("[5] Reloaded Authored Graph Weak route test")
	await _run_route(reloaded, 1, "Weak fail")
	
	print("[6] Reloaded Authored Graph Leave route test")
	await _run_route(reloaded, 2, "Leave")


# 리소스 클린업
func _cleanup() -> void:
	if DialogueManager.ui_request.is_connected(_on_ui_request):
		DialogueManager.ui_request.disconnect(_on_ui_request)
	if DialogueManager.dialogue_end.is_connected(_on_dialogue_end):
		DialogueManager.dialogue_end.disconnect(_on_dialogue_end)
		
	DialogueManager._dismiss()
	
	var dir := DirAccess.open("res://")
	if dir and dir.file_exists(TEST_SAVE_PATH):
		dir.remove(TEST_SAVE_PATH)
