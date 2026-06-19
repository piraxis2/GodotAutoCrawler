# DT-008 Step 2 검증용 헤드리스 에디터 테스트(Editor Authoring and Resource Round-trip).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/dialogtool/RunTime/tests/dt008_step2_editor_roundtrip_test.tscn
#
# 실제 dialoguetool_main.tscn(전체 UI 트리)을 fixture로 띄워 그 안의 editor.gd GraphEdit을 쓴다.
# bare GraphEdit 대신 메인 씬을 쓰는 이유: editor.gd의 @onready 형제 UI(PathLabel/PopupMenu)가
# 존재해야 초기화 시 "Node not found" ERROR 없이 깨끗하게 동작한다(코드 리뷰 P2-2).
#
# 검증:
# - boolean output 포트(port 0)와 Branch boolean 조건 입력 / data 입력 호환.
# - 외부 ConditionSet 참조(A→B), inline ConditionSet(D), null(C)이 각각
#   Definition -> picker -> capture -> nodes/runtime_nodes -> save -> CACHE_MODE_IGNORE 재로드 ->
#   adapter apply -> recapture 를 통과하며 트리/node id/connection까지 보존되는지.
# - null ConditionSet도 저장 가능하고 런타임에서 fail-closed.
extends Node

const MAIN_SCENE := "res://addons/world_core/dialogtool/dialoguetool_main.tscn"
const OP := StateCondition.Operator
const BOOLEAN := DialogueNode.port_type.boolean
const DATA := DialogueNode.port_type.data
const CS_PATH := "res://__dt008_step2_cs.tres"
const GRAPH_PATH := "res://__dt008_step2_graph.tres"

var _failures: int = 0


func _ready() -> void:
	await _run_all()
	if _failures == 0:
		print("[DT-008 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-008 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _run_all() -> void:
	await _test_port_and_compatibility()
	await _test_external_reference_roundtrip()
	await _test_inline_condition_set_roundtrip()
	await _test_null_condition_set_roundtrip()
	_cleanup()


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# 전체 메인 씬을 띄우고 그 안의 editor.gd GraphEdit을 반환한다.
func _make_editor() -> GraphEdit:
	var root: Node = load(MAIN_SCENE).instantiate()
	add_child(root)
	# editor._ready가 Start 노드를 call_deferred로 추가하고 reset_camera한다 → 두 프레임 대기.
	await get_tree().process_frame
	await get_tree().process_frame
	return root.find_child("GraphEdit", true, false)


# _make_editor가 띄운 메인 씬 루트(= GraphEdit의 최상위 부모)를 통째로 정리한다.
func _free_editor(ge: GraphEdit) -> void:
	var root: Node = ge
	while root.get_parent() != null and root.get_parent() != self:
		root = root.get_parent()
	root.queue_free()


# def의 전용 노드 씬(_get_dialogue_node)으로 노드를 추가한다.
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


func _conn(connections: Array, from_id: int, to_id: int) -> Variant:
	for c in connections:
		if c.get("from_node_id") == from_id and c.get("to_node_id") == to_id:
			return c
	return null


func _path_of(cs) -> String:
	return (cs as ConditionSet).resource_path if cs is ConditionSet else "<not-a-set>"


# root이 EQUAL 0 leaf인 단순 set. provider quest.main.stage==0이면 passed=true.
func _sample_set() -> ConditionSet:
	var leaf := StateCondition.new()
	leaf.key = &"quest.main.stage"
	leaf.operator = OP.EQUAL
	leaf.expected_value = 0
	var cs := ConditionSet.new()
	cs.root = leaf
	cs.description = "step2 sample"
	return cs


class _FakeProvider:
	var data: Dictionary
	func _init(d: Dictionary = {}) -> void:
		data = d
	func has_state(key: StringName) -> bool:
		return data.has(key)
	func read_state(key: StringName) -> Variant:
		return data.get(key)


func _has_code(report: Dictionary, code: String) -> bool:
	for e in report.get("errors", []):
		if e.get("code") == code:
			return true
	return false


# --- 시나리오 ---------------------------------------------------------

func _test_port_and_compatibility() -> void:
	print("[A] boolean output 포트 + Branch/Data 연결 호환")
	var ge := await _make_editor()

	var def := WorldStateConditionDef.new()
	def.condition_set = null
	var node := _add_def_node(ge, def, 1)
	var branch := _add_def_node(ge, BranchDef.new(), 2)
	await get_tree().process_frame
	await get_tree().process_frame

	_check("A.out_count", node.get_output_port_count(), 1)
	_check("A.out0_boolean", node.get_output_port_type(0), BOOLEAN)
	# Branch 조건 입력(port 0)은 boolean 타입이다. 출력과 동일 타입이므로 GraphEdit이 기본 허용한다
	# (동일 타입 연결은 is_valid_connection_type 등록 목록과 무관 — test B/D가 실제 연결 확인).
	_check("A.branch_in0_boolean", branch.get_input_port_type(0), BOOLEAN)
	_check("A.out_eq_branch_in", node.get_output_port_type(0), branch.get_input_port_type(0))
	# 교차 타입 호환은 editor.gd가 명시 등록한다(boolean↔data: Choice/Variable data 입력과 연결용).
	_check_true("A.bool_data_valid", ge.is_valid_connection_type(BOOLEAN, DATA))
	_check_true("A.data_bool_valid", ge.is_valid_connection_type(DATA, BOOLEAN))

	_free_editor(ge)
	await get_tree().process_frame


func _test_external_reference_roundtrip() -> void:
	print("[B] 외부 ConditionSet 참조(ext_resource) + 연결 capture/save/reload 보존")
	var cs := _sample_set()
	_check("B.cs_save", ResourceSaver.save(cs, CS_PATH), OK)
	var cs_ext: ConditionSet = ResourceLoader.load(CS_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)

	var ge := await _make_editor()
	var def := WorldStateConditionDef.new()
	def.condition_set = cs_ext
	_add_def_node(ge, def, 1)
	_add_def_node(ge, BranchDef.new(), 2)
	await get_tree().process_frame
	await get_tree().process_frame

	# state_condition.boolean_out(port 0) -> branch.condition_in(port 0).
	ge.connect_node("1", 0, "2", 0)

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	var rt_node: Dictionary = captured.runtime_nodes.get(1, {})
	_check("B.cap_type", rt_node.get("type"), &"state_condition")
	var cap_cs = rt_node.get("params", {}).get("condition_set")
	_check_true("B.cap_cs_is_set", cap_cs is ConditionSet)
	_check("B.cap_cs_path", _path_of(cap_cs), CS_PATH)
	_check_true("B.cap_conn", _conn(captured.runtime_connections, 1, 2) != null)

	# 저장 파일에 ext_resource로 기록되는지(2중 중첩 참조 직렬화).
	_check("B.graph_save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var text := FileAccess.get_file_as_string(GRAPH_PATH)
	_check_true("B.has_ext_resource", text.contains("ext_resource") and text.contains("__dt008_step2_cs"))

	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame

	var recap: DialogueGraphResource = ge2.capture_current_graphedit()
	var r_node: Dictionary = recap.runtime_nodes.get(1, {})
	_check("B.reload_type", r_node.get("type"), &"state_condition")
	var r_cs = r_node.get("params", {}).get("condition_set")
	_check_true("B.reload_cs_is_set", r_cs is ConditionSet)
	_check("B.reload_cs_path", _path_of(r_cs), CS_PATH)
	_check_true("B.reload_conn", _conn(recap.runtime_connections, 1, 2) != null)

	var r_gnode := _find_by_id(ge2, 1)
	_check("B.reload_out0_boolean", r_gnode.get_output_port_type(0), BOOLEAN)
	var player := DialoguePlayer.new()
	player.dialogue_resource = recap
	player.set_read_state_provider(_FakeProvider.new({&"quest.main.stage": 0}))
	_check("B.runtime_eval", player._get_data_value(1, 2), true)   # stage 0 == 0
	player.free()

	_free_editor(ge)
	_free_editor(ge2)
	await get_tree().process_frame


func _test_inline_condition_set_roundtrip() -> void:
	print("[D] inline ConditionSet(sub_resource) 에디터 왕복 — 트리/node id/connection 보존")
	var ge := await _make_editor()
	var def := WorldStateConditionDef.new()
	# in-memory ConditionSet(저장 경로 없음) → 그래프 저장 시 sub_resource로 인라인.
	def.condition_set = _sample_set()
	_add_def_node(ge, def, 1)
	_add_def_node(ge, BranchDef.new(), 2)
	await get_tree().process_frame
	await get_tree().process_frame

	ge.connect_node("1", 0, "2", 0)

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	var cap_cs = captured.runtime_nodes.get(1, {}).get("params", {}).get("condition_set")
	_check_true("D.cap_inline_is_set", cap_cs is ConditionSet)
	_check("D.cap_inline_no_path", _path_of(cap_cs), "")   # inline → resource_path 비어 있음
	_check_true("D.cap_conn", _conn(captured.runtime_connections, 1, 2) != null)

	_check("D.graph_save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var text := FileAccess.get_file_as_string(GRAPH_PATH)
	_check_true("D.has_sub_resource", text.contains("sub_resource"))

	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame

	var recap: DialogueGraphResource = ge2.capture_current_graphedit()
	var r_node: Dictionary = recap.runtime_nodes.get(1, {})
	_check("D.reload_type", r_node.get("type"), &"state_condition")
	var r_cs = r_node.get("params", {}).get("condition_set")
	_check_true("D.reload_inline_is_set", r_cs is ConditionSet)
	# 저장·재로드 후 inline set은 외부 .tres가 아니라 그래프 파일의 built-in sub_resource다.
	# capture 시점엔 경로가 비어 있고(D.cap_inline_no_path), 재로드 시엔 `graph.tres::SubResId` 형태가
	# 된다. 외부 CS_PATH가 아니고 그래프에 내장됨을 확인한다(여전히 inline).
	var r_path := _path_of(r_cs)
	_check_true("D.reload_inline_is_subresource", r_path.contains("::") and r_path != CS_PATH)
	# 인라인 트리 보존: root leaf key/operator/expected typeof.
	var root = (r_cs as ConditionSet).root if r_cs is ConditionSet else null
	_check_true("D.reload_root_state", root is StateCondition)
	if root is StateCondition:
		_check("D.reload_key", (root as StateCondition).key, &"quest.main.stage")
		_check("D.reload_op", (root as StateCondition).operator, OP.EQUAL)
		_check("D.reload_expected_typeof", typeof((root as StateCondition).expected_value), TYPE_INT)
	# node id + connection 보존.
	_check_true("D.reload_node_id", recap.runtime_nodes.has(1))
	_check_true("D.reload_conn", _conn(recap.runtime_connections, 1, 2) != null)
	# 재로드된 inline set이 boolean 포트로 보존되고 런타임 평가에서 동작.
	var r_gnode := _find_by_id(ge2, 1)
	_check("D.reload_out0_boolean", r_gnode.get_output_port_type(0), BOOLEAN)
	var player := DialoguePlayer.new()
	player.dialogue_resource = recap
	player.set_read_state_provider(_FakeProvider.new({&"quest.main.stage": 0}))
	_check("D.runtime_eval", player._get_data_value(1, 2), true)
	player.free()

	_free_editor(ge)
	_free_editor(ge2)
	await get_tree().process_frame


func _test_null_condition_set_roundtrip() -> void:
	print("[C] null ConditionSet 저장 가능 + 런타임 fail-closed")
	var ge := await _make_editor()
	var def := WorldStateConditionDef.new()
	def.condition_set = null
	_add_def_node(ge, def, 1)
	await get_tree().process_frame
	await get_tree().process_frame

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	var rt_node: Dictionary = captured.runtime_nodes.get(1, {})
	_check("C.cap_type", rt_node.get("type"), &"state_condition")
	_check("C.cap_cs_null", rt_node.get("params", {}).get("condition_set"), null)

	_check("C.graph_save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var r_cs = reloaded.runtime_nodes.get(1, {}).get("params", {}).get("condition_set")
	_check("C.reload_cs_null", r_cs, null)

	# 런타임 fail-closed: null -> condition_set_null -> false.
	var player := DialoguePlayer.new()
	player.dialogue_resource = reloaded
	player.set_read_state_provider(_FakeProvider.new({}))
	var events: Array = []
	player.condition_evaluated.connect(func(_cid, _consumer, report): events.append(report))
	_check("C.runtime_false", player._get_data_value(1, -1), false)
	player.free()
	_check_true("C.fail_closed_code", events.size() == 1 and _has_code(events[0], "condition_set_null"))

	_free_editor(ge)
	await get_tree().process_frame


func _cleanup() -> void:
	for p in [CS_PATH, GRAPH_PATH]:
		if FileAccess.file_exists(p):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(p))
