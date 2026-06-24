# DT-015 Step 1 검증용 헤드리스 테스트(Dialogue Integrated Regression Graph Step 1).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/dialogtool/RunTime/tests/dt015_step1_integrated_graph_test.tscn
#
# 목표:
# - 한 test-owned `DialogueGraphResource` 안에서 `Start`, `Say`, `Choice`, `Variable`, `Expression`, `Branch`, `End` 조합을 검증하는 headless Step 1 테스트를 추가한다.
# - canonical graph는 Strong/Weak/Leave 세 선택 경로를 가진다.
# - Step 1은 runtime graph + save/reload 검증까지만 한다.
#
# 제외: 에디터 authored graph 작성/capture/save/reload (Step 2 범위).

extends Node

const TEST_SAVE_PATH := "res://__dt015_integrated_graph.tres"

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
		print("[DT-015 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-015 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# 데드락 및 무한 루프 방지를 위한 watchdog
func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-015 Step1] WATCHDOG TIMEOUT after %.0fs — 테스트 시간 초과로 강제 종료합니다." % seconds)
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


# 1. Canonical Graph 리소스 수동 빌드
func _build_canonical_resource() -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.start_node_id = 0
	
	res.runtime_nodes = {
		0: {"id": 0, "type": &"start", "params": {}},
		1: {"id": 1, "type": &"say", "params": {"text": "Intro"}},
		2: {"id": 2, "type": &"choice", "params": {"choices": ["Strong", "Weak", "Leave"]}},
		
		# Strong Route
		3: {"id": 3, "type": &"branch", "params": {}},
		4: {"id": 4, "type": &"variable", "params": {"value": 7}},
		5: {"id": 5, "type": &"expression", "params": {"expression": "strength >= 5", "inputs": ["strength"]}},
		6: {"id": 6, "type": &"say", "params": {"text": "Strong success"}},
		7: {"id": 7, "type": &"say", "params": {"text": "Strong fail"}},
		
		# Weak Route
		9: {"id": 9, "type": &"branch", "params": {}},
		10: {"id": 10, "type": &"variable", "params": {"value": 3}},
		11: {"id": 11, "type": &"expression", "params": {"expression": "strength >= 5", "inputs": ["strength"]}},
		12: {"id": 12, "type": &"say", "params": {"text": "Weak success"}},
		13: {"id": 13, "type": &"say", "params": {"text": "Weak fail"}},
		
		# Leave Route
		14: {"id": 14, "type": &"say", "params": {"text": "Leave"}},
		
		# End Node
		8: {"id": 8, "type": &"end", "params": {}}
	}
	
	# 연결 정보 정의
	var conns: Array[Dictionary] = [
		{"from_node_id": 0, "from_port": 0, "to_node_id": 1, "to_port": 0},
		{"from_node_id": 1, "from_port": 0, "to_node_id": 2, "to_port": 0},
		
		# Choice outputs (Choice item flow output port = item index)
		{"from_node_id": 2, "from_port": 0, "to_node_id": 3, "to_port": 1}, # Strong -> Branch Flow input port (1)
		{"from_node_id": 2, "from_port": 1, "to_node_id": 9, "to_port": 1}, # Weak -> Branch Flow input port (1)
		{"from_node_id": 2, "from_port": 2, "to_node_id": 14, "to_port": 0}, # Leave -> Say "Leave" Flow input port (0)
		
		# Strong Branch
		{"from_node_id": 3, "from_port": 0, "to_node_id": 6, "to_port": 0}, # Branch true (0) -> Say Strong success
		{"from_node_id": 3, "from_port": 1, "to_node_id": 7, "to_port": 0}, # Branch false (1) -> Say Strong fail
		{"from_node_id": 6, "from_port": 0, "to_node_id": 8, "to_port": 0},
		{"from_node_id": 7, "from_port": 0, "to_node_id": 8, "to_port": 0},
		
		# Strong Data (Variable output port 0 -> Expression input port 0 -> Branch condition Data input port 0)
		{"from_node_id": 4, "from_port": 0, "to_node_id": 5, "to_port": 0}, # Variable -> Expression
		{"from_node_id": 5, "from_port": 0, "to_node_id": 3, "to_port": 0}, # Expression -> Branch condition
		
		# Weak Branch
		{"from_node_id": 9, "from_port": 0, "to_node_id": 12, "to_port": 0}, # Branch true (0) -> Say Weak success
		{"from_node_id": 9, "from_port": 1, "to_node_id": 13, "to_port": 0}, # Branch false (1) -> Say Weak fail
		{"from_node_id": 12, "from_port": 0, "to_node_id": 8, "to_port": 0},
		{"from_node_id": 13, "from_port": 0, "to_node_id": 8, "to_port": 0},
		
		# Weak Data
		{"from_node_id": 10, "from_port": 0, "to_node_id": 11, "to_port": 0}, # Variable -> Expression
		{"from_node_id": 11, "from_port": 0, "to_node_id": 9, "to_port": 0}, # Expression -> Branch condition
		
		# Leave Route
		{"from_node_id": 14, "from_port": 0, "to_node_id": 8, "to_port": 0}
	]
	
	res.runtime_connections = conns
	
	return res


# 2. 특정 Choice 분기 테스트 헬퍼
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


# 3. 저장 & 로드 테스트 헬퍼
func _test_save_reload(res: DialogueGraphResource) -> DialogueGraphResource:
	var err := ResourceSaver.save(res, TEST_SAVE_PATH)
	_check("ResourceSaver.save status", err, OK)
	
	var reloaded := ResourceLoader.load(TEST_SAVE_PATH, "", ResourceLoader.CACHE_MODE_IGNORE) as DialogueGraphResource
	_check_true("Reloaded resource is not null", reloaded != null)
	if reloaded == null:
		return null
		
	_check("Reloaded start_node_id", reloaded.start_node_id, res.start_node_id)
	_check("Reloaded runtime_nodes count", reloaded.runtime_nodes.size(), res.runtime_nodes.size())
	_check("Reloaded runtime_connections count", reloaded.runtime_connections.size(), res.runtime_connections.size())
	
	return reloaded


# 4. Choice Flow 보존 여부 단언
func _test_choice_flow_preservation(reloaded: DialogueGraphResource) -> void:
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


# 5. Negative sanity 테스트 (Expression 입력 미연결)
func _build_negative_sanity_resource() -> DialogueGraphResource:
	var res := _build_canonical_resource()
	
	# 4 (Variable 7) -> 5 (Expression) 연결을 삭제한다.
	var new_conns: Array[Dictionary] = []
	for conn in res.runtime_connections:
		if conn.from_node_id == 4 and conn.to_node_id == 5:
			continue
		new_conns.append(conn)
	res.runtime_connections = new_conns
	
	return res


func _test_negative_sanity() -> void:
	print("[Negative Sanity] Running variant with disconnected Expression input")
	var res = _build_negative_sanity_resource()
	
	_setup_manager_listeners()
	DialogueManager._dismiss()
	DialogueManager.play(res)
	
	await get_tree().process_frame
	await get_tree().process_frame
	
	var player = DialogueManager._ui.dialogue_player
	player.advance()
	await get_tree().process_frame
	await get_tree().process_frame
	
	# select Strong (index 0) -> 원래는 "Strong success"가 나와야 하지만,
	# 입력이 끊겼으므로 Expression 에러 -> Branch fail-closed -> "Strong fail"로 진행되어야 함.
	# SCRIPT ERROR가 발생하지 않음을 기대함.
	player.select_choice(0)
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check_true("Outcome Say request received for negative sanity", _ui_requests.size() > 2)
	if _ui_requests.size() > 2:
		var req = _ui_requests.back()
		_check("Outcome text is Strong fail (fail-closed)", req.get("say"), "Strong fail")
		
	player.advance()
	await get_tree().process_frame
	await get_tree().process_frame
	
	_check_true("Dialogue end emitted for negative sanity", _dialogue_end_emitted)


# 전체 시나리오 조율
func _run_all() -> void:
	var res := _build_canonical_resource()
	
	print("[1] Strong route test")
	await _run_route(res, 0, "Strong success")
	
	print("[2] Weak route test")
	await _run_route(res, 1, "Weak fail")
	
	print("[3] Leave route test")
	await _run_route(res, 2, "Leave")
	
	print("[4] Save and reload test")
	var reloaded := _test_save_reload(res)
	
	if reloaded != null:
		print("[5] Choice flow preservation test")
		_test_choice_flow_preservation(reloaded)
		
		print("[6] Reloaded resource Strong route test")
		await _run_route(reloaded, 0, "Strong success")
		
		print("[7] Reloaded resource Weak route test")
		await _run_route(reloaded, 1, "Weak fail")
		
		print("[8] Reloaded resource Leave route test")
		await _run_route(reloaded, 2, "Leave")
		
	print("[9] Negative sanity test")
	await _test_negative_sanity()


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
