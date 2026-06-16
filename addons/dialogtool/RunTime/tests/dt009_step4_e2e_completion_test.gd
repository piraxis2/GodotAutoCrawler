# DT-009 Step 4 검증용 헤드리스 e2e 완료 테스트(End-to-End Integration and Completion Review).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt009_step4_e2e_completion_test.tscn
#
# 실제 DialogueManager → DialogueUI → DialoguePlayer → WorldStateStore 전체 경로로,
# Choice 선택이 항목별 mutation Effect를 실행하고 그 결과를 다음 Branch(state_condition)가 즉시 읽는
# 전체 RPG 대화 흐름을 검증한다(read/mutation provider 양쪽 주입).
#
# 통합 그래프: Start → Choice["take"/"leave"] → Branch(gold>=150) → Say "Rich"/"Poor" → End
#   - "take"(항목0) Effect: state_add(gold, +50). "leave"(항목1): Effect 없음.
#
# 검증: 선택 전후 Store 값·mutation report·다음 조건 결과·Effect 저장 순서 일치, 실패 Effect의 값 불변,
#       반복 실행·same-frame 교체(폐기 provider mutation 0회)·provider 누락·read-only 실패.
extends Node

const OP := StateCondition.Operator
const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const GRAPH_PATH := "res://__dt009_step4_graph.tres"
const MAIN_SCENE := "res://addons/dialogtool/dialoguetool_main.tscn"
const EFFECT := DialogueNode.port_type.effect

var _failures: int = 0
var _stores: Array = []


func _ready() -> void:
	_install_watchdog(45.0)
	await _run_all()
	for s in _stores:
		if is_instance_valid(s):
			s.free()
	_cleanup()
	if _failures == 0:
		print("[DT-009 Step4] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-009 Step4] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-009 Step4] WATCHDOG TIMEOUT after %.0fs — --import 선행 확인." % seconds)
		get_tree().quit(2))


func _run_all() -> void:
	await _test_take_mutates_then_branch_true()
	await _test_leave_no_mutation_branch_false()
	await _test_repeat_consistency()
	await _test_latest_wins_discarded_no_mutation()
	await _test_provider_missing()
	await _test_read_only_failure()
	await _test_editor_authored_roundtrip_runs()


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


func _n(type: StringName, params: Dictionary = {}) -> Dictionary:
	return {"type": type, "params": params}


func _c(from_id: int, to_id: int, kind: String = "", from_port: int = 0, to_port: int = 0, choice_index: int = -1) -> Dictionary:
	var d := {"from_node_id": from_id, "from_port": from_port, "to_node_id": to_id, "to_port": to_port}
	if kind != "":
		d["kind"] = kind
	if choice_index >= 0:
		d["choice_index"] = choice_index
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


func _state_def(key: StringName, vtype: int, default_value: Variant, writable: bool = true) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	d.writable = writable
	return d


func _make_store(gold_writable: bool = true) -> WorldStateStore:
	var s := StateSchema.new()
	s.schema_version = 1
	var typed: Array[StateDefinition] = []
	typed.append(_state_def(&"player.gold", VT.INT, 100, gold_writable))
	s.definitions = typed
	var store := WorldStateStore.new()
	store.schema = s
	store.initialize()
	_stores.append(store)
	return store


func _gold_ge_150() -> ConditionSet:
	var sc := StateCondition.new()
	sc.key = &"player.gold"
	sc.operator = OP.GREATER_EQUAL
	sc.expected_value = 150
	var cs := ConditionSet.new()
	cs.root = sc
	return cs


# Start → Choice → Branch(gold>=150) → Say Rich/Poor → End. 항목0 Effect: add gold +50.
func _e2e_graph() -> DialogueGraphResource:
	var nodes := {
		0: _n(&"start"),
		1: _n(&"choice", {"choices": ["take", "leave"]}),
		2: _n(&"branch"),
		3: _n(&"state_condition", {"condition_set": _gold_ge_150()}),
		4: _n(&"say", {"text": "Rich"}),
		5: _n(&"say", {"text": "Poor"}),
		6: _n(&"end"),
		10: _n(&"state_add", {"key": &"player.gold", "delta": 50}),
	}
	var conns := [
		_c(0, 1),
		_c(1, 2, "", 0, 1),            # 항목0 flow → branch flow-in
		_c(1, 2, "", 1, 1),            # 항목1 flow → branch flow-in
		_c(1, 10, "effect", 0, 0, 0),  # 항목0 effect → add gold +50
		_c(3, 2, "", 0, 0),            # state_condition → branch 조건
		_c(2, 4, "", 0, 0),            # branch true → Rich
		_c(2, 5, "", 1, 0),            # branch false → Poor
		_c(4, 6), _c(5, 6),
	]
	return _make_resource(nodes, conns)


# DialogueManager로 그래프를 실행하고 choice 선택 후 Say를 수집한다.
func _run_e2e(res: DialogueGraphResource, read_provider, mutation_provider, visible_index: int) -> Dictionary:
	var says: Array = []
	var reports: Array = []
	var say_cb := func(req: Dictionary):
		if req.get("type") == "display_text":
			says.append(req.get("say"))
	DialogueManager.ui_request.connect(say_cb)
	DialogueManager.play(res, read_provider, mutation_provider)
	var player: DialoguePlayer = DialogueManager._ui.dialogue_player
	player.state_mutation_evaluated.connect(func(_e: int, r: Dictionary): reports.append(r))
	await get_tree().process_frame
	await get_tree().process_frame
	# 이제 choice 대기. 선택하면 항목 Effect 실행 후 Branch로 진행한다.
	player.select_choice(visible_index)
	await get_tree().process_frame
	await get_tree().process_frame
	var last_say = says[-1] if says.size() > 0 else null
	DialogueManager.ui_request.disconnect(say_cb)
	DialogueManager._dismiss()
	await get_tree().process_frame
	return {"say": last_say, "reports": reports}


# --- 시나리오 ---------------------------------------------------------

func _test_take_mutates_then_branch_true() -> void:
	print("[A] 'take' 선택 → add gold +50 → Branch(gold>=150) true → 'Rich'")
	var store := _make_store()
	var r := await _run_e2e(_e2e_graph(), store, store, 0)
	_check("A.gold", store.get_value(&"player.gold"), 150)
	_check("A.say", r["say"], "Rich")
	_check("A.report_count", r["reports"].size(), 1)
	if r["reports"].size() == 1:
		_check("A.report_op", r["reports"][0]["operation"], "add")
		_check("A.report_old", r["reports"][0]["old_value"], 100)
		_check("A.report_new", r["reports"][0]["new_value"], 150)


func _test_leave_no_mutation_branch_false() -> void:
	print("[B] 'leave' 선택 → mutation 없음 → Branch false → 'Poor'")
	var store := _make_store()
	var r := await _run_e2e(_e2e_graph(), store, store, 1)
	_check("B.gold", store.get_value(&"player.gold"), 100)
	_check("B.say", r["say"], "Poor")
	_check("B.no_report", r["reports"].size(), 0)


func _test_repeat_consistency() -> void:
	print("[C] 반복 실행 일관성(각 실행이 자기 Store로 동일 결과)")
	var s1 := _make_store()
	var r1 := await _run_e2e(_e2e_graph(), s1, s1, 0)
	_check("C.run1_gold", s1.get_value(&"player.gold"), 150)
	_check("C.run1_say", r1["say"], "Rich")
	var s2 := _make_store()
	var r2 := await _run_e2e(_e2e_graph(), s2, s2, 0)
	_check("C.run2_gold", s2.get_value(&"player.gold"), 150)
	_check("C.run2_say", r2["say"], "Rich")


func _test_latest_wins_discarded_no_mutation() -> void:
	print("[D] same-frame 교체(latest-wins) → 폐기 provider mutation 0회")
	var discarded := _make_store()
	var active := _make_store()
	var vc := {"n": 0}
	discarded.value_changed.connect(func(_k, _o, _n): vc["n"] += 1)

	DialogueManager.play(_e2e_graph(), discarded, discarded)
	var player_a: DialoguePlayer = DialogueManager._ui.dialogue_player
	DialogueManager.play(_e2e_graph(), active, active)   # 같은 프레임 교체 → 첫 대화 폐기
	var player_b: DialoguePlayer = DialogueManager._ui.dialogue_player
	await get_tree().process_frame
	await get_tree().process_frame
	# 활성 대화에서 take 선택.
	player_b.select_choice(0)
	await get_tree().process_frame
	await get_tree().process_frame

	_check_true("D.distinct_players", player_a != player_b)
	_check("D.discarded_no_mutation", vc["n"], 0)
	_check("D.discarded_unchanged", discarded.get_value(&"player.gold"), 100)
	_check("D.active_mutated", active.get_value(&"player.gold"), 150)
	DialogueManager._dismiss()
	await get_tree().process_frame


func _test_provider_missing() -> void:
	print("[E] mutation provider 누락 → state_add provider_missing, 값 불변, Branch false → 'Poor'")
	var store := _make_store()
	# read provider만 주입(mutation 누락). 자동 승격 없음 → mutation 실패 + Flow 계속.
	var r := await _run_e2e(_e2e_graph(), store, null, 0)
	_check("E.gold_unchanged", store.get_value(&"player.gold"), 100)
	_check("E.say", r["say"], "Poor")
	_check("E.report_error", r["reports"][0]["error"] if r["reports"].size() > 0 else &"none", &"provider_missing")


func _test_read_only_failure() -> void:
	print("[F] read-only gold → state_add read_only 실패, 값 불변, Branch false → 'Poor'")
	var store := _make_store(false)   # gold read-only
	var r := await _run_e2e(_e2e_graph(), store, store, 0)
	_check("F.gold_unchanged", store.get_value(&"player.gold"), 100)
	_check("F.say", r["say"], "Poor")
	_check("F.report_error", r["reports"][0]["error"] if r["reports"].size() > 0 else &"none", &"read_only")


func _test_editor_authored_roundtrip_runs() -> void:
	print("[G] 에디터 authored 그래프 save→reload→DialogueManager 실행에서 항목별 mutation 적용")
	var root: Node = load(MAIN_SCENE).instantiate()
	add_child(root)
	await get_tree().process_frame
	await get_tree().process_frame
	var ge: GraphEdit = root.find_child("GraphEdit", true, false)

	var start: DialogueNode = null
	for child in ge.get_children():
		if child is DialogueNode and child.definition is StartDef:
			start = child
	# Start → Choice(2) → Say "done"; 항목0 effect → state_add gold +50.
	var choice := _add_def_node(ge, _choice_def(["take", "leave"]), 50)
	var say_node := _add_def_node(ge, SayDef.new(), 51)
	_add_def_node(ge, _add_def(&"player.gold", TYPE_INT, 50), 52)
	await get_tree().process_frame
	await get_tree().process_frame

	var sflow := _port(start, true, DialogueNode.port_type.flow)
	ge.connect_node(str(start.id), sflow, "50", 0)                        # start → choice
	ge.connect_node("50", 0, "51", _port(say_node, false, DialogueNode.port_type.flow))  # item0 flow → say
	ge.connect_node("50", 1, "51", _port(say_node, false, DialogueNode.port_type.flow))  # item1 flow → say
	ge.connect_node("50", choice.effect_port_for_choice_index(0), "52", _port(_find_by_id(ge, 52), false, EFFECT))

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	_check("G.save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	root.queue_free()
	await get_tree().process_frame

	# 재로드 그래프를 DialogueManager로 실행하고 take 선택 → gold +50.
	var store := _make_store()
	DialogueManager.play(reloaded, store, store)
	var player: DialoguePlayer = DialogueManager._ui.dialogue_player
	await get_tree().process_frame
	await get_tree().process_frame
	player.select_choice(0)
	await get_tree().process_frame
	_check("G.take_gold", store.get_value(&"player.gold"), 150)
	DialogueManager._dismiss()
	await get_tree().process_frame

	# leave 선택은 mutation 없음(새 Store).
	var store2 := _make_store()
	DialogueManager.play(reloaded, store2, store2)
	var player2: DialoguePlayer = DialogueManager._ui.dialogue_player
	await get_tree().process_frame
	await get_tree().process_frame
	player2.select_choice(1)
	await get_tree().process_frame
	_check("G.leave_gold", store2.get_value(&"player.gold"), 100)
	DialogueManager._dismiss()
	await get_tree().process_frame


# 에디터 노드 추가 헬퍼.
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


func _find_by_id(ge: GraphEdit, id: int) -> DialogueNode:
	for child in ge.get_children():
		if child is DialogueNode and child.id == id:
			return child
	return null


func _port(node: DialogueNode, is_output: bool, ptype: int) -> int:
	if is_output:
		for i in node.get_output_port_count():
			if node.get_output_port_type(i) == ptype:
				return i
	else:
		for i in node.get_input_port_count():
			if node.get_input_port_type(i) == ptype:
				return i
	return -1


func _choice_def(items: Array) -> ChoiceDef:
	var d := ChoiceDef.new()
	var typed: Array[String] = []
	for s in items:
		typed.append(s)
	d.choices = typed
	return d


func _add_def(key: StringName, dtype: int, delta: Variant) -> StateAddDef:
	var d := StateAddDef.new()
	d.key = key
	d.delta_type = dtype
	d.delta = delta
	return d


func _cleanup() -> void:
	if FileAccess.file_exists(GRAPH_PATH):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(GRAPH_PATH))
