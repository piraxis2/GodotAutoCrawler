# DT-005 Step 5 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://Assets/Script/gds/world_state/tests/dt005_step5_provider_seam_test.tscn
#
# 검증 범위 (provider seam):
# - DialoguePlayer가 read 상태 provider를 주입받는 경계(미지정/fake/Store 주입)
# - WorldStateStore가 read/mutation provider 계약을 구현
# - DialogueManager -> DialogueUI -> DialoguePlayer provider 전달
# - 기존 Variable/Expression/Branch 데이터 평가 동작 유지
extends Node

const UI_SCENE := "res://addons/dialogtool/UI/Dialogue_UI.tscn"
const SVT := StateDefinition.StateValueType

var _failures: int = 0


# duck-typed read provider 계약 구현(테스트 주입용). calls로 접근 횟수를 센다.
class FakeReadProvider:
	var data: Dictionary
	var calls: int = 0
	func _init(d: Dictionary = {}) -> void:
		data = d
	func has_state(key: StringName) -> bool:
		calls += 1
		return data.has(key)
	func read_state(key: StringName) -> Variant:
		calls += 1
		return data.get(key)
	func try_read_state(key: StringName, fallback: Variant = null) -> Variant:
		calls += 1
		return data.get(key, fallback)


func _ready() -> void:
	_test_provider_unset()
	_test_fake_read_provider()
	_test_store_as_provider()
	_test_variable_branch_eval()
	_test_expression_branch_eval()
	await _test_ui_passthrough()
	await _test_manager_passthrough()
	await _test_ui_same_frame_replace()
	await _test_manager_same_frame_replace()

	if _failures == 0:
		print("[DT-005 Step5] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-005 Step5] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


func _state_def(key: StringName, vtype: int, default_value: Variant) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	return d


func _state_schema(defs: Array) -> StateSchema:
	var s := StateSchema.new()
	var typed: Array[StateDefinition] = []
	for d in defs:
		typed.append(d)
	s.definitions = typed
	return s


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


# Start -> Say -> End. Say에서 대기한다.
func _say_resource(text: String) -> DialogueGraphResource:
	return _resource(
		{0: _n(&"start"), 1: _n(&"say", {"text": text}), 2: _n(&"end")},
		[_c(0, 0, 1, 0), _c(1, 0, 2, 0)])


# 한 player로 resource를 실행하고 첫 display_text의 say 문자열을 반환한다(트리에 붙이지 않음).
func _run_capture_say(res: DialogueGraphResource) -> Variant:
	var player := DialoguePlayer.new()
	var captured := {"say": null}
	player.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "display_text" and captured["say"] == null:
			captured["say"] = req.get("say"))
	player.start_dialogue(res)
	var say = captured["say"]
	player.free()
	return say


# --- 시나리오 ---------------------------------------------------------

func _test_provider_unset() -> void:
	print("[A] provider 미지정")
	var player := DialoguePlayer.new()
	_check("A.has_provider", player.has_read_state_provider(), false)
	_check("A.has_state", player.has_state(&"quest.stage"), false)
	_check("A.read_null", player.read_state(&"quest.stage"), null)
	_check("A.try_fallback", player.try_read_state(&"quest.stage", 7), 7)
	player.free()


func _test_fake_read_provider() -> void:
	print("[B] fake read provider 주입")
	var player := DialoguePlayer.new()
	var fake := FakeReadProvider.new({&"quest.stage": 3, &"flag.on": true})
	player.set_read_state_provider(fake)
	_check("B.has_provider", player.has_read_state_provider(), true)
	_check("B.get_provider", player.get_read_state_provider() == fake, true)
	_check("B.has_state", player.has_state(&"quest.stage"), true)
	_check("B.read", player.read_state(&"quest.stage"), 3)
	_check("B.read_bool", player.read_state(&"flag.on"), true)
	_check("B.try_missing", player.try_read_state(&"nope.nope", -1), -1)
	_check("B.has_missing", player.has_state(&"nope.nope"), false)
	player.free()


func _test_store_as_provider() -> void:
	print("[C] WorldStateStore를 read provider로 주입")
	var store := WorldStateStore.new()
	store.schema = _state_schema([_state_def(&"actor.affinity", SVT.INT, 0)])
	store.initialize()
	store.set_value(&"actor.affinity", 42)

	var player := DialoguePlayer.new()
	player.set_read_state_provider(store)
	_check("C.has", player.has_state(&"actor.affinity"), true)
	_check("C.read", player.read_state(&"actor.affinity"), 42)
	_check("C.try_missing", player.try_read_state(&"nope.nope", -1), -1)
	# Store는 mutation provider 계약도 구현한다(여기서 Player엔 주입하지 않음).
	_check("C.store_set_state", store.set_state(&"actor.affinity", 50), OK)
	_check("C.read_after_set", player.read_state(&"actor.affinity"), 50)
	player.free()
	store.free()


func _test_variable_branch_eval() -> void:
	print("[D] Variable -> Branch 데이터 평가 유지(provider 미지정)")
	# 0 start, 1 branch, 2 say true, 3 say false, 4 variable, 5 end
	# start->branch는 to_port=1(데이터 입력 0과 충돌 방지), variable->branch 데이터는 to_port=0.
	var base_nodes := {
		0: _n(&"start"),
		1: _n(&"branch"),
		2: _n(&"say", {"text": "true-branch"}),
		3: _n(&"say", {"text": "false-branch"}),
		5: _n(&"end"),
	}
	var conns := [
		_c(0, 0, 1, 1),   # start flow -> branch
		_c(4, 0, 1, 0),   # variable data -> branch 조건 입력 0
		_c(1, 0, 2, 0),   # branch true -> say true
		_c(1, 1, 3, 0),   # branch false -> say false
		_c(2, 0, 5, 0),
		_c(3, 0, 5, 0),
	]
	var nodes_true := base_nodes.duplicate()
	nodes_true[4] = _n(&"variable", {"value": true})
	_check("D.true", _run_capture_say(_resource(nodes_true, conns)), "true-branch")

	var nodes_false := base_nodes.duplicate()
	nodes_false[4] = _n(&"variable", {"value": false})
	_check("D.false", _run_capture_say(_resource(nodes_false, conns)), "false-branch")


func _test_expression_branch_eval() -> void:
	print("[E] Expression -> Branch 데이터 평가 유지")
	# 6 variable(x) -> 4 expression(x > 0) -> 1 branch 조건
	var nodes := {
		0: _n(&"start"),
		1: _n(&"branch"),
		2: _n(&"say", {"text": "expr-true"}),
		3: _n(&"say", {"text": "expr-false"}),
		4: _n(&"expression", {"expression": "x > 0", "inputs": ["x"]}),
		5: _n(&"end"),
		6: _n(&"variable", {"value": 1}),
	}
	var conns := [
		_c(0, 0, 1, 1),   # start flow -> branch
		_c(6, 0, 4, 0),   # variable x -> expression 입력 0
		_c(4, 0, 1, 0),   # expression -> branch 조건 입력 0
		_c(1, 0, 2, 0),
		_c(1, 1, 3, 0),
		_c(2, 0, 5, 0),
		_c(3, 0, 5, 0),
	]
	_check("E.expr_true", _run_capture_say(_resource(nodes, conns)), "expr-true")


func _test_ui_passthrough() -> void:
	print("[F] DialogueUI가 provider를 Player까지 전달")
	var ui: DialogueUI = load(UI_SCENE).instantiate()
	add_child(ui)
	await get_tree().process_frame
	var fake := FakeReadProvider.new({&"quest.stage": 3})
	ui.play(_say_resource("hi"), fake)
	await get_tree().process_frame  # deferred start_dialogue
	_check_true("F.provider_set", ui.dialogue_player.get_read_state_provider() == fake)
	_check("F.read", ui.dialogue_player.read_state(&"quest.stage"), 3)
	ui.queue_free()
	await get_tree().process_frame


func _test_manager_passthrough() -> void:
	print("[G] DialogueManager -> UI -> Player provider 전달")
	var fake := FakeReadProvider.new({&"x": 9})
	DialogueManager.play(_say_resource("hi"), fake)
	await get_tree().process_frame
	await get_tree().process_frame  # deferred start
	_check("G.playing", DialogueManager.is_playing(), true)
	var player: DialoguePlayer = DialogueManager._ui.dialogue_player
	_check_true("G.provider_set", player.get_read_state_provider() == fake)
	_check("G.read", player.read_state(&"x"), 9)
	DialogueManager._dismiss()
	await get_tree().process_frame


# 같은 프레임에 한 UI로 play()를 두 번 호출하면 마지막 것만 시작한다(latest-wins).
# 먼저 큐된 대화가 나중 provider로 평가되지 않아야 한다(resource/provider 결합 유지).
func _test_ui_same_frame_replace() -> void:
	print("[H] 같은 프레임 UI 연속 play -> latest-wins, provider 결합 유지")
	var ui: DialogueUI = load(UI_SCENE).instantiate()
	add_child(ui)
	await get_tree().process_frame
	var says: Array = []
	ui.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "display_text":
			says.append(req.get("say")))
	var p_a := FakeReadProvider.new({&"k": 1})
	var p_b := FakeReadProvider.new({&"k": 2})
	ui.play(_say_resource("A"), p_a)
	ui.play(_say_resource("B"), p_b)
	await get_tree().process_frame  # 단일 deferred dispatch
	# 마지막 호출(B)만 시작하고 provider도 B여야 한다. A는 시작/평가되지 않는다.
	_check_true("H.provider_b", ui.dialogue_player.get_read_state_provider() == p_b)
	_check("H.read", ui.dialogue_player.read_state(&"k"), 2)
	_check("H.only_b_started", says, ["B"])
	ui.queue_free()
	await get_tree().process_frame


# Manager 연속 교체: 폐기된 A는 시작/평가되지 않고, 최종 활성 UI가 providerB로 B를 시작·유지한다.
func _test_manager_same_frame_replace() -> void:
	print("[I] Manager 연속 교체 -> 폐기 UI 미실행, 최종 UI가 providerB 유지")
	var p_a := FakeReadProvider.new({&"k": 10})
	var p_b := FakeReadProvider.new({&"k": 20})

	# Manager.dialogue_started 발행 횟수와 외부 Say를 센다.
	var started := {"n": 0}
	var ext_says: Array = []
	var started_cb := func(): started["n"] += 1
	var say_cb := func(req: Dictionary):
		if req.get("type") == "display_text":
			ext_says.append(req.get("say"))
	DialogueManager.dialogue_started.connect(started_cb)
	DialogueManager.ui_request.connect(say_cb)

	DialogueManager.play(_say_resource("A"), p_a)
	DialogueManager.play(_say_resource("B"), p_b)
	await get_tree().process_frame
	await get_tree().process_frame

	_check("I.playing", DialogueManager.is_playing(), true)
	# 폐기된 A는 시작되지 않으므로 dialogue_started는 정확히 1회.
	_check("I.started_once", started["n"], 1)
	# 외부로 나간 Say는 B 하나뿐.
	_check("I.external_say", ext_says, ["B"])
	# 폐기된 A의 provider는 한 번도 접근되지 않는다.
	_check("I.providerA_calls", p_a.calls, 0)

	var player: DialoguePlayer = DialogueManager._ui.dialogue_player
	_check_true("I.provider_b", player.get_read_state_provider() == p_b)
	_check("I.read", player.read_state(&"k"), 20)

	DialogueManager.dialogue_started.disconnect(started_cb)
	DialogueManager.ui_request.disconnect(say_cb)
	DialogueManager._dismiss()
	await get_tree().process_frame
