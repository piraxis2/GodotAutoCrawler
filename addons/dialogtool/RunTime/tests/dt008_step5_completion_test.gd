# DT-008 Step 5 검증용 헤드리스 테스트(Conditional Choice Editor and Completion Review).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt008_step5_completion_test.tscn
#
# 1) 에디터: State Condition boolean output ↔ Choice 항목별 Data 입력 연결이 저장/재로드 후 보존되고,
#    Choice resize(항목 수 변경)가 남은 항목의 condition/Flow 연결을 잘못 재배치하지 않는지(Design Risk 2).
# 2) 런타임: Branch + conditional Choice 복합 그래프가 실제 WorldStateStore 상태에 따라 같은 evaluator
#    계약으로 동작하는지(완료 판정 e2e).
#
# fixture: 실제 dialoguetool_main.tscn(editor.gd @onready 형제 UI 완비 → 0 ERROR 종료).
extends Node

const MAIN_SCENE := "res://addons/dialogtool/dialoguetool_main.tscn"
const SCHEMA_PATH := "res://Assets/Script/gds/world_state/world_state_schema.tres"
const OP := StateCondition.Operator
const BOOLEAN := DialogueNode.port_type.boolean
const DATA := DialogueNode.port_type.data

var _failures: int = 0
var _stores: Array = []


func _ready() -> void:
	await _test_choice_condition_roundtrip_and_resize()
	_test_composite_branch_choice_e2e()

	for s in _stores:
		if is_instance_valid(s):
			s.free()
	if _failures == 0:
		print("[DT-008 Step5] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-008 Step5] FAILED: %d assertion(s)" % _failures)
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


func _find_by_id(ge: GraphEdit, id: int) -> DialogueNode:
	for child in ge.get_children():
		if child is DialogueNode and child.id == id:
			return child
	return null


func _has_conn(conns: Array, from_id: int, fp: int, to_id: int, tp: int) -> bool:
	for c in conns:
		if c.get("from_node_id") == from_id and c.get("from_port") == fp \
				and c.get("to_node_id") == to_id and c.get("to_port") == tp:
			return true
	return false


func _make_store() -> WorldStateStore:
	var schema: StateSchema = ResourceLoader.load(SCHEMA_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var store := WorldStateStore.new()
	store.schema = schema
	store.initialize()
	_stores.append(store)
	return store


func _state(key: StringName, op: int, expected: Variant) -> StateCondition:
	var s := StateCondition.new()
	s.key = key
	s.operator = op
	s.expected_value = expected
	return s


func _cset(root) -> ConditionSet:
	var cs := ConditionSet.new()
	cs.root = root
	return cs


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


# --- 1) 에디터: 조건 연결 round-trip + resize 보존 ----------------------

func _test_choice_condition_roundtrip_and_resize() -> void:
	print("[A] Choice 조건 연결 저장/재로드 + resize 보존")
	var graph_path := "res://__dt008_step5_choice.tres"
	var ge := await _make_editor()

	# Choice(id 1) 3항목 + State Condition 2개(id 2,3) + End 3개(id 10,11,12).
	var choice_def := ChoiceDef.new()
	choice_def.choices = ["A", "B", "C"] as Array[String]
	_add_def_node(ge, choice_def, 1)
	_add_def_node(ge, WorldStateConditionDef.new(), 2)
	_add_def_node(ge, WorldStateConditionDef.new(), 3)
	_add_def_node(ge, EndDef.new(), 10)
	_add_def_node(ge, EndDef.new(), 11)
	_add_def_node(ge, EndDef.new(), 12)
	await get_tree().process_frame
	await get_tree().process_frame

	# 포트 계약: 항목 i 데이터 입력 = i+1, flow 출력 = i.
	# cond2 -> 항목0 데이터(port 1), cond3 -> 항목2 데이터(port 3).
	ge.connect_node("2", 0, "1", 1)
	ge.connect_node("3", 0, "1", 3)
	# 항목 flow 출력 0,1,2 -> End 10,11,12.
	ge.connect_node("1", 0, "10", 0)
	ge.connect_node("1", 1, "11", 0)
	ge.connect_node("1", 2, "12", 0)

	var cap: DialogueGraphResource = ge.capture_current_graphedit()
	_check_true("A.cap_cond0", _has_conn(cap.runtime_connections, 2, 0, 1, 1))
	_check_true("A.cap_cond2", _has_conn(cap.runtime_connections, 3, 0, 1, 3))
	_check_true("A.cap_flow0", _has_conn(cap.runtime_connections, 1, 0, 10, 0))
	_check_true("A.cap_flow1", _has_conn(cap.runtime_connections, 1, 1, 11, 0))
	_check_true("A.cap_flow2", _has_conn(cap.runtime_connections, 1, 2, 12, 0))
	# 포트 타입 호환: state_condition output boolean, choice 데이터 입력 data.
	var choice_node := _find_by_id(ge, 1)
	var cond_node := _find_by_id(ge, 2)
	_check("A.cond_out_boolean", cond_node.get_output_port_type(0), BOOLEAN)
	_check("A.choice_in1_data", choice_node.get_input_port_type(1), DATA)

	# 저장 -> 재로드 -> 재캡처: 조건/Flow 연결 보존.
	_check("A.save", ResourceSaver.save(cap, graph_path), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(graph_path, "", ResourceLoader.CACHE_MODE_IGNORE)
	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame
	var recap: DialogueGraphResource = ge2.capture_current_graphedit()
	_check_true("A.reload_cond0", _has_conn(recap.runtime_connections, 2, 0, 1, 1))
	_check_true("A.reload_cond2", _has_conn(recap.runtime_connections, 3, 0, 1, 3))
	_check_true("A.reload_flow2", _has_conn(recap.runtime_connections, 1, 2, 12, 0))
	_free_editor(ge2)

	# resize: Choice를 2항목으로 줄인다. 항목2(데이터 port 3 / flow port 2)는 사라지고,
	# 항목0의 조건(port 1)과 항목0/1 flow(port 0/1)는 그대로 보존돼야 한다(잘못 재배치 금지).
	choice_node.update_item(2)
	await get_tree().process_frame
	var cap2: DialogueGraphResource = ge.capture_current_graphedit()
	_check_true("A.resize_keep_cond0", _has_conn(cap2.runtime_connections, 2, 0, 1, 1))
	_check_true("A.resize_drop_cond2", not _has_conn(cap2.runtime_connections, 3, 0, 1, 3))
	_check_true("A.resize_keep_flow0", _has_conn(cap2.runtime_connections, 1, 0, 10, 0))
	_check_true("A.resize_keep_flow1", _has_conn(cap2.runtime_connections, 1, 1, 11, 0))
	_check_true("A.resize_drop_flow2", not _has_conn(cap2.runtime_connections, 1, 2, 12, 0))
	# 남은 항목0 조건이 다른 항목으로 옮겨가지 않았는지(여전히 to_port 1, 항목1 데이터 port 2엔 연결 없음).
	_check_true("A.resize_no_misroute", not _has_conn(cap2.runtime_connections, 2, 0, 1, 2))

	# resize 후 저장/재로드도 보존.
	_check("A.resize_save", ResourceSaver.save(cap2, graph_path), OK)
	var reloaded2: DialogueGraphResource = ResourceLoader.load(graph_path, "", ResourceLoader.CACHE_MODE_IGNORE)
	var ge3 := await _make_editor()
	ge3.load_resource(reloaded2)
	await get_tree().process_frame
	await get_tree().process_frame
	var recap2: DialogueGraphResource = ge3.capture_current_graphedit()
	_check_true("A.resize_reload_cond0", _has_conn(recap2.runtime_connections, 2, 0, 1, 1))
	_check_true("A.resize_reload_flow1", _has_conn(recap2.runtime_connections, 1, 1, 11, 0))
	_check_true("A.resize_reload_no_item2", not _has_conn(recap2.runtime_connections, 1, 2, 12, 0))
	_free_editor(ge3)

	_free_editor(ge)
	if FileAccess.file_exists(graph_path):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(graph_path))
	await get_tree().process_frame


# --- 2) 런타임: 복합 Branch + conditional Choice e2e -------------------

class _ChoiceCapture:
	var offers: Array = []
	var says: Array = []


# 복합 그래프를 직접 구성한 runtime snapshot으로 실행한다.
#   Start -> Branch(stage>=3)
#     true  -> Choice[ "always"(무조건), "cond"(affinity>=10) ]
#     false -> Say "LOW_STAGE"
#   Choice 항목0 -> Say "CHOSE_ALWAYS", 항목1 -> Say "CHOSE_COND"
func _composite_graph() -> DialogueGraphResource:
	var nodes := {
		0: _n(&"start"),
		1: _n(&"branch"),
		2: _n(&"state_condition", {"condition_set": _cset(_state(&"quest.main.stage", OP.GREATER_EQUAL, 3))}),
		3: _n(&"choice", {"choices": ["always", "cond"]}),
		4: _n(&"state_condition", {"condition_set": _cset(_state(&"actor.example.affinity", OP.GREATER_EQUAL, 10))}),
		5: _n(&"say", {"text": "LOW_STAGE"}),
		6: _n(&"say", {"text": "CHOSE_ALWAYS"}),
		7: _n(&"say", {"text": "CHOSE_COND"}),
		99: _n(&"end"),
	}
	var conns := [
		_c(0, 0, 1, 1),     # start -> branch flow-in
		_c(2, 0, 1, 0),     # stage 조건 -> branch 데이터 입력
		_c(1, 0, 3, 0),     # branch true -> choice flow-in
		_c(1, 1, 5, 0),     # branch false -> Say LOW_STAGE
		_c(4, 0, 3, 2),     # affinity 조건 -> choice 항목1 데이터 입력(port 2)
		_c(3, 0, 6, 0),     # choice 항목0 -> CHOSE_ALWAYS
		_c(3, 1, 7, 0),     # choice 항목1 -> CHOSE_COND
		_c(5, 0, 99, 0), _c(6, 0, 99, 0), _c(7, 0, 99, 0),
	]
	return _resource(nodes, conns)


func _run(store: WorldStateStore) -> _ChoiceCapture:
	var player := DialoguePlayer.new()
	var res := _composite_graph()
	player.dialogue_resource = res
	player.set_read_state_provider(store)
	var cap := _ChoiceCapture.new()
	player.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "offer_choice":
			cap.offers.append(req.get("choices"))
		elif req.get("type") == "display_text":
			cap.says.append(req.get("say")))
	player.start_dialogue(res)
	cap.set_meta("player", player)
	return cap


func _test_composite_branch_choice_e2e() -> void:
	print("[B] 복합 Branch + conditional Choice e2e(실제 Store)")

	# B1: default(stage 0) -> branch false -> LOW_STAGE.
	var s1 := _make_store()
	var c1 := _run(s1)
	_check("B1.low_stage", c1.says[0], "LOW_STAGE")
	(c1.get_meta("player") as DialoguePlayer).free()

	# B2: stage 5, affinity 0 -> branch true -> choice, cond 항목 숨김 -> ["always"].
	var s2 := _make_store()
	s2.set_value(&"quest.main.stage", 5)
	var c2 := _run(s2)
	_check("B2.offer", c2.offers[0], ["always"])
	var p2 := c2.get_meta("player") as DialoguePlayer
	p2.select_choice(0)
	_check("B2.chose_always", c2.says.back(), "CHOSE_ALWAYS")
	p2.free()

	# B3: stage 5, affinity 10 -> branch true -> choice, 둘 다 -> ["always","cond"], cond 선택.
	var s3 := _make_store()
	s3.set_value(&"quest.main.stage", 5)
	s3.set_value(&"actor.example.affinity", 10)
	var c3 := _run(s3)
	_check("B3.offer", c3.offers[0], ["always", "cond"])
	var p3 := c3.get_meta("player") as DialoguePlayer
	p3.select_choice(1)   # visible 1 -> 원래 항목1 -> CHOSE_COND
	_check("B3.chose_cond", c3.says.back(), "CHOSE_COND")
	p3.free()

	# B4: Branch와 Choice가 같은 Store 변화에 일관 반응(stage 떨어지면 다시 false flow).
	var s4 := _make_store()
	s4.set_value(&"quest.main.stage", 5)
	s4.set_value(&"actor.example.affinity", 10)
	var c4a := _run(s4)
	_check("B4.true_offer", c4a.offers.size(), 1)
	(c4a.get_meta("player") as DialoguePlayer).free()
	s4.reset_value(&"quest.main.stage")   # 0으로 복귀
	var c4b := _run(s4)
	_check("B4.false_after_reset", c4b.says[0], "LOW_STAGE")
	_check("B4.no_offer", c4b.offers.size(), 0)
	(c4b.get_meta("player") as DialoguePlayer).free()
