# DT-013 Step 2 검증용 헤드리스 에디터 테스트(State Read Authoring and Resource Round-Trip).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt013_step2_editor_roundtrip_test.tscn
#
# 실제 dialoguetool_main.tscn(전체 UI 트리)을 fixture로 띄워 editor.gd GraphEdit에서 WorldStateReadNode를
# 생성/로드하며 노드 목록 노출, key/type capture, data output 포트, boolean 입력 연결, 저장 validation,
# .tres 왕복 보존을 검증한다.
#
# 검증(DT-013 Step 2 Completion Criteria):
# - 노드 목록(DialogueNodeItemList)에 State Read("WorldStateRead") 노출 + state_read 어댑터 등록.
# - key/type 입력이 runtime params(key/value_type)로 보존된다.
# - output port는 generic data 1개이며, data↔boolean 연결이 호환된다(Branch boolean 입력 연결).
# - invalid key/type(StateSchema.KEY_PATTERN 기준 matrix + 허용 5타입 밖)은 저장 validation에서 차단된다.
# - .tres 저장 -> cache ignore reload -> 재캡처에서 key/type/connection이 보존된다.
extends Node

const MAIN_SCENE := "res://addons/dialogtool/dialoguetool_main.tscn"
const DATA := DialogueNode.port_type.data
const BOOLEAN := DialogueNode.port_type.boolean
const GRAPH_PATH := "res://__dt013_step2_graph.tres"

var _failures: int = 0


func _ready() -> void:
	await _run_all()
	if _failures == 0:
		print("[DT-013 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-013 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _run_all() -> void:
	await _test_item_list_and_registry()
	await _test_capture_params()
	await _test_output_port_and_boolean_connect()
	await _test_save_validation_matrix()
	await _test_summary_label()
	await _test_roundtrip()
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


func _read_def(key: StringName, vtype: int) -> WorldStateReadDef:
	var d := WorldStateReadDef.new()
	d.key = key
	d.value_type = vtype
	return d


func _params_of(res: DialogueGraphResource, id: int) -> Dictionary:
	return res.runtime_nodes.get(id, {}).get("params", {})


func _conn(connections: Array, from_id: int, to_id: int) -> Variant:
	for c in connections:
		if c.get("from_node_id") == from_id and c.get("to_node_id") == to_id:
			return c
	return null


# --- 시나리오 ---------------------------------------------------------

func _test_item_list_and_registry() -> void:
	print("[A] 노드 목록 노출 + state_read 어댑터 등록")
	var il := DialogueNodeItemList.new()
	add_child(il)
	await get_tree().process_frame
	var names: Array = []
	for i in il.item_count:
		names.append(il.get_item_text(i))
	# class_name "WorldStateReadDef"에서 "Def"를 떼어 "WorldStateRead"로 노출된다(StateSet/WorldStateCondition 규칙).
	_check_true("A.has_WorldStateRead", "WorldStateRead" in names)
	il.queue_free()
	await get_tree().process_frame

	_check_true("A.registry_has_state_read", NodeTypeRegistry.has_adapter(&"state_read"))
	_check_true("A.registry_adapter_nonnull", NodeTypeRegistry.get_adapter(&"state_read") != null)


func _test_capture_params() -> void:
	print("[B] key/type 입력이 runtime params로 보존")
	var ge := await _make_editor()
	var node := _add_def_node(ge, _read_def(&"quest.main.stage", TYPE_INT), 1)
	await get_tree().process_frame
	await get_tree().process_frame

	# apply_params가 UI에 값을 반영했는지(노드 접근자).
	_check("B.applied_key", node.get_key(), &"quest.main.stage")
	_check("B.applied_type", node.get_value_type(), TYPE_INT)

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	var params := _params_of(captured, 1)
	_check("B.param_key", params.get("key"), &"quest.main.stage")
	_check("B.param_value_type", params.get("value_type"), TYPE_INT)
	_check("B.runtime_type", captured.runtime_nodes.get(1, {}).get("type"), &"state_read")

	_free_editor(ge)
	await get_tree().process_frame


func _test_output_port_and_boolean_connect() -> void:
	print("[C] output port = generic data 1개, data↔boolean 연결 + Branch 입력 연결 capture")
	var ge := await _make_editor()
	var node := _add_def_node(ge, _read_def(&"session.intro.seen", TYPE_BOOL), 1)
	_add_def_node(ge, BranchDef.new(), 2)
	await get_tree().process_frame
	await get_tree().process_frame

	_check("C.out_count", node.get_output_port_count(), 1)
	_check("C.out0_data", node.get_output_port_type(0), DATA)
	_check("C.in_count", node.get_input_port_count(), 0)
	# editor.gd가 등록한 data↔boolean 교차 호환(Branch boolean 조건 입력 연결 가능).
	_check_true("C.data_to_boolean_valid", ge.is_valid_connection_type(DATA, BOOLEAN))

	# State Read data output(port 0) -> Branch boolean 조건 입력(port 0).
	ge.connect_node("1", 0, "2", 0)
	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	_check_true("C.conn_captured", _conn(captured.runtime_connections, 1, 2) != null)
	var c := _conn(captured.runtime_connections, 1, 2)
	_check("C.conn_from_port", c.get("from_port"), 0)
	_check("C.conn_to_port", c.get("to_port"), 0)
	# value 카테고리 연결이라 kind=="effect"가 아니다.
	_check_true("C.conn_not_effect", c.get("kind", "") != DialogueGraphResource.CONNECTION_KIND_EFFECT)

	_free_editor(ge)
	await get_tree().process_frame


func _test_save_validation_matrix() -> void:
	print("[D] invalid key/type 저장 차단(StateSchema.KEY_PATTERN matrix + 허용 5타입)")
	# validate_structure 단위 검사: invalid key matrix.
	for bad_key in ["quest", "Quest.main", "quest..main", "1quest.main", ""]:
		var def := _read_def(StringName(bad_key), TYPE_INT)
		_check_true("D.key_invalid[%s]" % bad_key, not def.validate_structure().is_empty())
	# valid key는 통과.
	_check("D.key_valid", _read_def(&"quest.main.stage", TYPE_INT).validate_structure(), "")
	# value_type matrix: 허용 5타입 통과, 그 외 차단.
	for ok_t in WorldStateReadDef.READ_VALUE_TYPES:
		_check("D.type_ok[%d]" % ok_t, _read_def(&"quest.main.stage", ok_t).validate_structure(), "")
	for bad_t in [TYPE_NIL, TYPE_VECTOR2, TYPE_ARRAY]:
		_check_true("D.type_invalid[%d]" % bad_t, not _read_def(&"quest.main.stage", bad_t).validate_structure().is_empty())

	# editor.gd 저장 validation 통합: invalid key 노드는 _validate_runtime_snapshot이 차단(fatal).
	var ge := await _make_editor()
	var bad_node := _add_def_node(ge, _read_def(&"", TYPE_INT), 1)
	await get_tree().process_frame
	await get_tree().process_frame
	bad_node.key_edit.text = "quest"   # 단일 segment → invalid
	var cap_bad: DialogueGraphResource = ge.capture_current_graphedit()
	_check("D.editor_blocks_bad_key", ge._validate_runtime_snapshot(cap_bad), false)

	# 유효 key/type 노드만 있으면 저장 validation 통과(true).
	bad_node.key_edit.text = "quest.main.stage"
	var cap_ok: DialogueGraphResource = ge.capture_current_graphedit()
	_check("D.editor_allows_valid", ge._validate_runtime_snapshot(cap_ok), true)

	# invalid value_type(UI로는 못 만들지만 손상 .tres 방어): capture 후 def 타입을 망가뜨리면 차단.
	cap_ok.nodes[1].definition.value_type = TYPE_VECTOR2
	_check("D.editor_blocks_bad_type", ge._validate_runtime_snapshot(cap_ok), false)

	_free_editor(ge)
	await get_tree().process_frame


func _test_summary_label() -> void:
	print("[E] summary label: '<key> : <TYPE>' / 'No State Key'")
	var ge := await _make_editor()
	var node := _add_def_node(ge, _read_def(&"player.gold", TYPE_INT), 1)
	await get_tree().process_frame
	await get_tree().process_frame
	_check("E.summary_keyed", node.summary_label.text, "player.gold : INT")
	_check("E.summary_white", node.summary_label.modulate, Color.WHITE)

	# key를 비우면 No State Key + invalid 색.
	node.set_key(&"")
	_check("E.summary_empty", node.summary_label.text, "No State Key")
	_check_true("E.summary_empty_not_white", node.summary_label.modulate != Color.WHITE)

	# 타입 변경이 summary에 반영.
	node.set_key(&"world.build.channel")
	node.set_value_type(TYPE_STRING_NAME)
	_check("E.summary_strname", node.summary_label.text, "world.build.channel : STRING_NAME")

	_free_editor(ge)
	await get_tree().process_frame


func _test_roundtrip() -> void:
	print("[F] .tres 저장 → reload → 재캡처에서 key/type/connection 보존")
	var ge := await _make_editor()
	var node := _add_def_node(ge, _read_def(&"player.health", TYPE_FLOAT), 1)
	_add_def_node(ge, BranchDef.new(), 2)
	await get_tree().process_frame
	await get_tree().process_frame
	ge.connect_node("1", 0, "2", 0)

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	# 저장 전 validation 통과 확인(유효 그래프).
	_check("F.pre_validate", ge._validate_runtime_snapshot(captured), true)
	_check("F.graph_save", ResourceSaver.save(captured, GRAPH_PATH), OK)

	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame

	var r_node := _find_by_id(ge2, 1)
	_check_true("F.reload_is_read_def", r_node.definition is WorldStateReadDef)
	_check("F.reload_out0_data", r_node.get_output_port_type(0), DATA)
	# 재로드한 노드 UI 값.
	_check("F.reload_key", r_node.get_key(), &"player.health")
	_check("F.reload_type", r_node.get_value_type(), TYPE_FLOAT)

	var recap: DialogueGraphResource = ge2.capture_current_graphedit()
	var params := _params_of(recap, 1)
	_check("F.recap_key", params.get("key"), &"player.health")
	_check("F.recap_value_type", params.get("value_type"), TYPE_FLOAT)
	_check_true("F.recap_conn", _conn(recap.runtime_connections, 1, 2) != null)

	_free_editor(ge)
	_free_editor(ge2)
	await get_tree().process_frame


func _cleanup() -> void:
	if FileAccess.file_exists(GRAPH_PATH):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(GRAPH_PATH))
