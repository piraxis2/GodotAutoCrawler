# DT-012 Step 2 검증용 헤드리스 에디터 테스트(WorldStateCondition Node Display).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt012_step2_node_display_test.tscn
#
# 실제 dialoguetool_main.tscn(전체 UI 트리)을 fixture로 띄워 editor.gd GraphEdit에서
# WorldStateConditionNode를 생성/로드하며 summary label/tooltip/invalid 표시를 검증한다.
#
# 검증(DT-012 Step 2 Completion Criteria):
# - 외부 .tres ConditionSet -> path(picker)뿐 아니라 readable summary(label) 표시.
# - inline ConditionSet도 summary 표시.
# - description은 structural valid일 때만 우선, null/invalid는 description과 무관하게 구분 표시.
# - 저장 -> 재로드 -> 에디터 load 후 summary 동일 + condition_set/connection 보존.
# - boolean output 포트(port 0)는 summary label 추가 후에도 그대로(슬롯 인덱스 회귀 없음).
# - 긴 summary는 잘리고(label.text) full text는 tooltip으로, 노드 폭은 폭주하지 않는다.
extends Node

const MAIN_SCENE := "res://addons/dialogtool/dialoguetool_main.tscn"
const OP := StateCondition.Operator
const LG := ConditionGroup.Logic
const BOOLEAN := DialogueNode.port_type.boolean
const CS_PATH := "res://__dt012_step2_cs.tres"
const GRAPH_PATH := "res://__dt012_step2_graph.tres"

var _failures: int = 0


func _ready() -> void:
	await _run_all()
	if _failures == 0:
		print("[DT-012 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-012 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _run_all() -> void:
	await _test_external_summary()
	await _test_inline_summary()
	await _test_null_and_invalid_distinct()
	await _test_long_summary_clipped()
	await _test_roundtrip_and_port()
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


func _conn(connections: Array, from_id: int, to_id: int) -> Variant:
	for c in connections:
		if c.get("from_node_id") == from_id and c.get("to_node_id") == to_id:
			return c
	return null


func _leaf_set(key: StringName, op: int, val: Variant, desc := "") -> ConditionSet:
	var leaf := StateCondition.new()
	leaf.key = key
	leaf.operator = op
	leaf.expected_value = val
	var cs := ConditionSet.new()
	cs.root = leaf
	cs.description = desc
	return cs


# --- 시나리오 ---------------------------------------------------------

func _test_external_summary() -> void:
	print("[A] 외부 ConditionSet: path(picker) + readable summary(label), description 없으면 구조 요약")
	var cs := _leaf_set(&"actor.example.affinity", OP.GREATER_EQUAL, 10)
	_check("A.save", ResourceSaver.save(cs, CS_PATH), OK)
	var cs_ext: ConditionSet = ResourceLoader.load(CS_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)

	var ge := await _make_editor()
	var def := WorldStateConditionDef.new()
	def.condition_set = cs_ext
	var node := _add_def_node(ge, def, 1)
	await get_tree().process_frame
	await get_tree().process_frame

	_check("A.label_summary", node.summary_label.text, "actor.example.affinity >= 10")
	_check("A.valid_white", node.summary_label.modulate, Color.WHITE)
	# picker는 여전히 path를 유지(참조 정체성).
	_check("A.picker_path", node.picker.text, CS_PATH)
	# tooltip에 path와 full summary 병기.
	_check_true("A.tooltip_path", node.summary_label.tooltip_text.contains(CS_PATH))
	_check_true("A.tooltip_full", node.summary_label.tooltip_text.contains("actor.example.affinity >= 10"))

	_free_editor(ge)
	await get_tree().process_frame


func _test_inline_summary() -> void:
	print("[B] inline ConditionSet도 summary 표시 + description 우선(valid)")
	var ge := await _make_editor()
	var def := WorldStateConditionDef.new()
	def.condition_set = _leaf_set(&"quest.main.stage", OP.EQUAL, 3, "메인 퀘스트 3단계")
	var node := _add_def_node(ge, def, 1)
	await get_tree().process_frame
	await get_tree().process_frame

	# structural valid + description → description 우선.
	_check("B.label_summary", node.summary_label.text, "메인 퀘스트 3단계")
	# full 구조 요약은 tooltip에.
	_check_true("B.tooltip_structure", node.summary_label.tooltip_text.contains("quest.main.stage == 3"))
	_check("B.valid_white", node.summary_label.modulate, Color.WHITE)

	_free_editor(ge)
	await get_tree().process_frame


func _test_null_and_invalid_distinct() -> void:
	print("[C] null / invalid(description 있어도) 그래프 위에서 구분")
	var ge := await _make_editor()

	# null
	var null_def := WorldStateConditionDef.new()
	null_def.condition_set = null
	var null_node := _add_def_node(ge, null_def, 1)

	# invalid: root_null인 set + description(설명이 invalid를 가리면 안 됨)
	var bad := ConditionSet.new()
	bad.root = null
	bad.description = "이 설명은 invalid를 가리면 안 된다"
	var bad_def := WorldStateConditionDef.new()
	bad_def.condition_set = bad
	var bad_node := _add_def_node(ge, bad_def, 2)
	await get_tree().process_frame
	await get_tree().process_frame

	_check("C.null_summary", null_node.summary_label.text, "No ConditionSet")
	_check_true("C.null_not_white", null_node.summary_label.modulate != Color.WHITE)

	_check("C.invalid_summary", bad_node.summary_label.text, "Invalid: root_null")
	_check_true("C.invalid_not_white", bad_node.summary_label.modulate != Color.WHITE)
	_check_true("C.desc_hidden", not bad_node.summary_label.text.contains("설명"))

	_free_editor(ge)
	await get_tree().process_frame


func _test_long_summary_clipped() -> void:
	print("[D] 긴 summary: label.text 잘림 + full tooltip + 노드 폭 폭주 없음")
	var ge := await _make_editor()
	var kids: Array[ConditionClause] = []
	for i in 20:
		var leaf := StateCondition.new()
		leaf.key = &"quest.main.flag%d" % i
		leaf.operator = OP.EQUAL
		leaf.expected_value = i
		kids.append(leaf)
	var grp := ConditionGroup.new()
	grp.logic = LG.ALL
	grp.children = kids
	var cs := ConditionSet.new()
	cs.root = grp
	var def := WorldStateConditionDef.new()
	def.condition_set = cs
	var node := _add_def_node(ge, def, 1)
	await get_tree().process_frame
	await get_tree().process_frame

	_check_true("D.text_truncated", node.summary_label.text.length() <= ConditionSummary.DEFAULT_MAX_LENGTH)
	_check_true("D.text_ellipsis", node.summary_label.text.ends_with(ConditionSummary.ELLIPSIS))
	_check_true("D.label_clip", node.summary_label.clip_text)
	# full text는 tooltip으로 확인 가능(마지막 leaf까지).
	_check_true("D.tooltip_full", node.summary_label.tooltip_text.contains("quest.main.flag19 == 19)"))
	# 노드 폭이 full summary 길이만큼 폭주하지 않는다(clip_text + custom_minimum로 제한).
	_check_true("D.node_width_bounded", node.size.x <= 600.0)

	_free_editor(ge)
	await get_tree().process_frame


func _test_roundtrip_and_port() -> void:
	print("[E] 저장→재로드 summary 동일 + condition_set/connection/boolean 포트 보존")
	var cs := _leaf_set(&"actor.example.affinity", OP.GREATER_EQUAL, 10, "친밀도 충분")
	_check("E.cs_save", ResourceSaver.save(cs, CS_PATH), OK)
	var cs_ext: ConditionSet = ResourceLoader.load(CS_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)

	var ge := await _make_editor()
	var def := WorldStateConditionDef.new()
	def.condition_set = cs_ext
	var node := _add_def_node(ge, def, 1)
	_add_def_node(ge, BranchDef.new(), 2)
	await get_tree().process_frame
	await get_tree().process_frame

	# description 우선 표시(저장 전).
	_check("E.pre_summary", node.summary_label.text, "친밀도 충분")
	# boolean output port가 summary label 추가 후에도 port 0.
	_check("E.out_count", node.get_output_port_count(), 1)
	_check("E.out0_boolean", node.get_output_port_type(0), BOOLEAN)

	ge.connect_node("1", 0, "2", 0)
	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	# capture params 보존(우리 변경이 condition_set 캡처를 깨지 않음).
	var cap_cs = captured.runtime_nodes.get(1, {}).get("params", {}).get("condition_set")
	_check_true("E.cap_cs_is_set", cap_cs is ConditionSet)
	_check_true("E.cap_conn", _conn(captured.runtime_connections, 1, 2) != null)

	_check("E.graph_save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame

	var r_node := _find_by_id(ge2, 1)
	# 재로드 후 summary 동일.
	_check("E.reload_summary", r_node.summary_label.text, "친밀도 충분")
	_check("E.reload_out0_boolean", r_node.get_output_port_type(0), BOOLEAN)
	var recap: DialogueGraphResource = ge2.capture_current_graphedit()
	var r_cs = recap.runtime_nodes.get(1, {}).get("params", {}).get("condition_set")
	_check_true("E.reload_cs_is_set", r_cs is ConditionSet)
	_check_true("E.reload_conn", _conn(recap.runtime_connections, 1, 2) != null)

	_free_editor(ge)
	_free_editor(ge2)
	await get_tree().process_frame


func _cleanup() -> void:
	for p in [CS_PATH, GRAPH_PATH]:
		if FileAccess.file_exists(p):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(p))
