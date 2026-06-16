# DT-009 Step 3 검증용 헤드리스 에디터 테스트(Editor Authoring and Resource Round-trip).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt009_step3_editor_roundtrip_test.tscn
#
# 실제 dialoguetool_main.tscn(전체 UI 트리)을 fixture로 띄워 editor.gd GraphEdit을 쓴다(DT-008 Step 2와 동일).
#
# 검증(ADR-010 D7 / Step 3 완료 조건):
# - StateSet/StateAdd 노드가 Effect 입력 포트를 가지며 Start의 Effect 출력에 연결된다.
# - StateAdd 타입 선택은 INT/FLOAT 2개뿐(다른 타입 literal을 만들 수 없음).
# - key/operation/value_type/value(typeof)/delta(typeof)/node id/Effect 연결(kind)이
#   capture→save→CACHE_MODE_IGNORE 재로드→re-capture에서 보존된다(Definition + runtime_nodes 양쪽).
# - 저장·재로드된 그래프가 실제 mutation provider로 런타임 실행돼 Store 값을 바꾼다(authoring→runtime 계약).
extends Node

const MAIN_SCENE := "res://addons/dialogtool/dialoguetool_main.tscn"
const EFFECT := DialogueNode.port_type.effect
const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const GRAPH_PATH := "res://__dt009_step3_graph.tres"

var _failures: int = 0
var _stores: Array = []


func _ready() -> void:
	_install_watchdog(40.0)
	await _run_all()
	for s in _stores:
		if is_instance_valid(s):
			s.free()
	_cleanup()
	if _failures == 0:
		print("[DT-009 Step3] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-009 Step3] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-009 Step3] WATCHDOG TIMEOUT after %.0fs — 미완료 종료(행 가능성). --import 선행 확인." % seconds)
		get_tree().quit(2))


func _run_all() -> void:
	await _test_item_list_exposes_state_nodes()
	await _test_ports_and_add_type_options()
	await _test_set_roundtrip()
	await _test_add_roundtrip()
	await _test_authored_resource_runs()
	await _test_invalid_literal_blocked_and_rejected()


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# str() 비교는 String/StringName, int/float를 구분하지 못하므로 typeof를 직접 단언한다.
func _check_typeof(name: String, value: Variant, expected_type: int) -> void:
	_check(name, typeof(value), expected_type)


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


func _find_start(ge: GraphEdit) -> DialogueNode:
	for child in ge.get_children():
		if child is DialogueNode and child.definition is StartDef:
			return child
	return null


func _port_of(gnode: DialogueNode, is_output: bool, ptype: int) -> int:
	if gnode == null:
		return -1
	if is_output:
		for i in gnode.get_output_port_count():
			if gnode.get_output_port_type(i) == ptype:
				return i
	else:
		for i in gnode.get_input_port_count():
			if gnode.get_input_port_type(i) == ptype:
				return i
	return -1


func _set_def(key: StringName, vtype: int, value: Variant) -> StateSetDef:
	var d := StateSetDef.new()
	d.key = key
	d.value_type = vtype
	d.value = value
	return d


func _add_def(key: StringName, dtype: int, delta: Variant) -> StateAddDef:
	var d := StateAddDef.new()
	d.key = key
	d.delta_type = dtype
	d.delta = delta
	return d


func _def_of(res: DialogueGraphResource, id: int) -> DialogueDefinition:
	return res.nodes.get(id, {}).get("definition")


func _rt_params(res: DialogueGraphResource, id: int) -> Dictionary:
	return res.runtime_nodes.get(id, {}).get("params", {})


func _conn(res: DialogueGraphResource, from_id: int, to_id: int) -> Variant:
	for c in res.runtime_connections:
		if c.get("from_node_id") == from_id and c.get("to_node_id") == to_id:
			return c
	return null


func _make_store() -> WorldStateStore:
	var s := StateSchema.new()
	s.schema_version = 1
	var typed: Array[StateDefinition] = []
	for d in [
		_state_def(&"player.gold", VT.INT, 100),
		_state_def(&"player.hp", VT.FLOAT, 10.0),
	]:
		typed.append(d)
	s.definitions = typed
	var store := WorldStateStore.new()
	store.schema = s
	store.initialize()
	_stores.append(store)
	return store


func _state_def(key: StringName, vtype: int, default_value: Variant) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	return d


# --- 시나리오 ---------------------------------------------------------

func _test_ports_and_add_type_options() -> void:
	print("[A] Effect 입력 포트 + StateAdd 타입 옵션은 INT/FLOAT뿐")
	var ge := await _make_editor()
	var set_node := _add_def_node(ge, _set_def(&"player.gold", TYPE_INT, 5), 10)
	var add_node := _add_def_node(ge, _add_def(&"player.gold", TYPE_INT, 1), 11)
	await get_tree().process_frame
	await get_tree().process_frame

	# Effect 입력 포트 존재(출력은 없음 — leaf).
	_check_true("A.set_has_effect_in", _port_of(set_node, false, EFFECT) != -1)
	_check_true("A.add_has_effect_in", _port_of(add_node, false, EFFECT) != -1)
	_check("A.set_no_effect_out", _port_of(set_node, true, EFFECT), -1)

	# StateAdd 타입 OptionButton은 정확히 INT/FLOAT 2개, StateSet은 5개.
	var add_widget = add_node.get_meta(&"state_effect_widget")
	var add_opt: OptionButton = add_widget.get_node("type").get_node("val")
	_check("A.add_type_count", add_opt.item_count, 2)
	_check("A.add_type0", int(add_opt.get_item_metadata(0)), TYPE_INT)
	_check("A.add_type1", int(add_opt.get_item_metadata(1)), TYPE_FLOAT)
	var set_widget = set_node.get_meta(&"state_effect_widget")
	var set_opt: OptionButton = set_widget.get_node("type").get_node("val")
	_check("A.set_type_count", set_opt.item_count, 5)

	_free_editor(ge)
	await get_tree().process_frame


func _test_set_roundtrip() -> void:
	print("[B] StateSet 5타입 capture→save→reload→recapture 보존(key/타입/typeof/연결)")
	var ge := await _make_editor()
	var start := _find_start(ge)
	_check_true("B.start_exists", start != null)
	var start_effect_out := _port_of(start, true, EFFECT)
	_check_true("B.start_effect_out", start_effect_out != -1)

	# 다양한 타입의 StateSet 노드.
	var specs := {
		20: [&"a.int", TYPE_INT, 200],
		21: [&"a.str", TYPE_STRING, "hello"],
		22: [&"a.sname", TYPE_STRING_NAME, &"angry"],
		23: [&"a.bool", TYPE_BOOL, true],
		24: [&"a.float", TYPE_FLOAT, 3.5],
	}
	for id in specs:
		var sp: Array = specs[id]
		_add_def_node(ge, _set_def(sp[0], sp[1], sp[2]), id)
	# Say도 Effect 출력 소스가 될 수 있음을 확인하기 위해 Say 노드를 추가한다(node 25).
	var say_node := _add_def_node(ge, SayDef.new(), 25)
	await get_tree().process_frame
	await get_tree().process_frame

	# node 20은 Say의 Effect 출력에서, 나머지는 Start의 Effect 출력에서 연결한다.
	var say_effect_out := _port_of(say_node, true, EFFECT)
	_check_true("B.say_effect_out", say_effect_out != -1)
	for id in specs:
		var node := _find_by_id(ge, id)
		var ein := _port_of(node, false, EFFECT)
		if id == 20:
			ge.connect_node(str(say_node.id), say_effect_out, str(id), ein)
		else:
			ge.connect_node(str(start.id), start_effect_out, str(id), ein)

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	_check("B.save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)

	# CACHE_MODE_IGNORE 재로드 직후, recapture 전에 Definition 필드 typeof를 직접 단언한다
	# (.tres가 타입을 보존했는지 — coerce 없이 순수 직렬화 보존을 검증).
	for id in specs:
		var sp_r: Array = specs[id]
		var rdef := _def_of(reloaded, id) as StateSetDef
		_check_true("B.%d_reload_is_set" % id, rdef is StateSetDef)
		if rdef is StateSetDef:
			_check("B.%d_reload_key" % id, rdef.key, sp_r[0])
			_check("B.%d_reload_value_type" % id, rdef.value_type, sp_r[1])
			_check_typeof("B.%d_reload_value_typeof" % id, rdef.value, sp_r[1])

	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame
	var recap: DialogueGraphResource = ge2.capture_current_graphedit()

	for id in specs:
		var sp: Array = specs[id]
		var def := _def_of(recap, id) as StateSetDef
		_check_true("B.%d_is_set_def" % id, def is StateSetDef)
		if def is StateSetDef:
			_check("B.%d_key" % id, def.key, sp[0])
			_check("B.%d_value_type" % id, def.value_type, sp[1])
			_check("B.%d_value" % id, def.value, sp[2])
			# recapture 후 Definition value typeof 직접 단언(String/StringName, int/float 구분).
			_check_typeof("B.%d_value_typeof" % id, def.value, sp[1])
		# runtime_nodes params의 value typeof 보존.
		var p := _rt_params(recap, id)
		_check("B.%d_rt_type" % id, recap.runtime_nodes.get(id, {}).get("type"), &"state_set")
		_check("B.%d_rt_value" % id, p.get("value"), sp[2])
		_check_typeof("B.%d_rt_typeof" % id, p.get("value"), sp[1])
		# Effect 연결(kind) + node id 보존. node 20은 Say 소스, 나머지는 Start 소스.
		var src_id: int = 25 if id == 20 else start.id
		var c = _conn(recap, src_id, id)
		_check_true("B.%d_conn" % id, c != null)
		if c != null:
			_check("B.%d_conn_kind" % id, c.get("kind"), "effect")

	_free_editor(ge)
	_free_editor(ge2)
	await get_tree().process_frame


func _test_add_roundtrip() -> void:
	print("[C] StateAdd INT/FLOAT capture→save→reload→recapture 보존")
	var ge := await _make_editor()
	var start := _find_start(ge)
	var start_effect_out := _port_of(start, true, EFFECT)

	_add_def_node(ge, _add_def(&"a.int", TYPE_INT, 25), 30)
	_add_def_node(ge, _add_def(&"a.float", TYPE_FLOAT, -2.5), 31)
	await get_tree().process_frame
	await get_tree().process_frame
	for id in [30, 31]:
		var node := _find_by_id(ge, id)
		ge.connect_node(str(start.id), start_effect_out, str(id), _port_of(node, false, EFFECT))

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	_check("C.save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)

	# 재로드 직후 Definition delta typeof 직접 단언(.tres 보존, recapture 전).
	var r30 := _def_of(reloaded, 30) as StateAddDef
	_check_true("C.30_reload_is_add", r30 is StateAddDef)
	if r30 is StateAddDef:
		_check_typeof("C.30_reload_delta_typeof", r30.delta, TYPE_INT)
	var r31 := _def_of(reloaded, 31) as StateAddDef
	if r31 is StateAddDef:
		_check_typeof("C.31_reload_delta_typeof", r31.delta, TYPE_FLOAT)

	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame
	var recap: DialogueGraphResource = ge2.capture_current_graphedit()

	var int_def := _def_of(recap, 30) as StateAddDef
	_check_true("C.30_is_add", int_def is StateAddDef)
	if int_def is StateAddDef:
		_check("C.30_key", int_def.key, &"a.int")
		_check("C.30_delta_type", int_def.delta_type, TYPE_INT)
		_check("C.30_delta", int_def.delta, 25)
		_check_typeof("C.30_delta_typeof", int_def.delta, TYPE_INT)
	_check("C.30_rt_type", recap.runtime_nodes.get(30, {}).get("type"), &"state_add")
	_check("C.30_rt_typeof", typeof(_rt_params(recap, 30).get("delta")), TYPE_INT)

	var float_def := _def_of(recap, 31) as StateAddDef
	if float_def is StateAddDef:
		_check("C.31_delta_type", float_def.delta_type, TYPE_FLOAT)
		_check("C.31_delta", float_def.delta, -2.5)
		_check_typeof("C.31_delta_typeof", float_def.delta, TYPE_FLOAT)
	_check("C.31_rt_typeof", typeof(_rt_params(recap, 31).get("delta")), TYPE_FLOAT)
	_check_true("C.30_conn", _conn(recap, start.id, 30) != null)
	_check_true("C.31_conn", _conn(recap, start.id, 31) != null)

	_free_editor(ge)
	_free_editor(ge2)
	await get_tree().process_frame


func _test_authored_resource_runs() -> void:
	print("[D] 저장·재로드된 authored 그래프가 mutation provider로 런타임 실행")
	var ge := await _make_editor()
	var start := _find_start(ge)
	var start_effect_out := _port_of(start, true, EFFECT)
	var start_flow_out := _port_of(start, true, DialogueNode.port_type.flow)

	# Start --flow--> End ; Start --effect--> StateSet(gold=200), StateAdd(gold,+5).
	var end_node := _add_def_node(ge, EndDef.new(), 40)
	_add_def_node(ge, _set_def(&"player.gold", TYPE_INT, 200), 41)
	_add_def_node(ge, _add_def(&"player.gold", TYPE_INT, 5), 42)
	await get_tree().process_frame
	await get_tree().process_frame

	ge.connect_node(str(start.id), start_flow_out, "40", _port_of(end_node, false, DialogueNode.port_type.flow))
	ge.connect_node(str(start.id), start_effect_out, "41", _port_of(_find_by_id(ge, 41), false, EFFECT))
	ge.connect_node(str(start.id), start_effect_out, "42", _port_of(_find_by_id(ge, 42), false, EFFECT))

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	_check("D.save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)

	var store := _make_store()
	var reports: Array = []
	var player := DialoguePlayer.new()
	player.state_mutation_evaluated.connect(func(_e, r): reports.append(r))
	player.set_mutation_state_provider(store)
	player.dialogue_resource = reloaded
	add_child(player)
	player.start_dialogue(reloaded)
	# set(200) → add(+5) = 205, 저장 순서대로.
	_check("D.gold_final", store.get_value(&"player.gold"), 205)
	_check("D.report_count", reports.size(), 2)
	if reports.size() == 2:
		_check("D.r0_op", reports[0]["operation"], "set")
		_check("D.r0_new", reports[0]["new_value"], 200)
		_check("D.r1_op", reports[1]["operation"], "add")
		_check("D.r1_new", reports[1]["new_value"], 205)
	player.free()

	_free_editor(ge)
	await get_tree().process_frame


func _test_item_list_exposes_state_nodes() -> void:
	print("[E] 노드 목록(DialogueNodeItemList)에 StateSet/StateAdd 노출 + Abstract 제외")
	var il := DialogueNodeItemList.new()
	add_child(il)
	await get_tree().process_frame
	var names: Array = []
	for i in il.item_count:
		names.append(il.get_item_text(i))
	_check_true("E.has_StateSet", "StateSet" in names)
	_check_true("E.has_StateAdd", "StateAdd" in names)
	_check_true("E.no_abstract_StateEffect", not ("StateEffect" in names))
	il.queue_free()
	await get_tree().process_frame


func _test_invalid_literal_blocked_and_rejected() -> void:
	print("[F] 잘못된 literal → 저장 차단 + 런타임 type_mismatch(조용한 0 변환 없음)")
	var ge := await _make_editor()
	var start := _find_start(ge)
	var set_node := _add_def_node(ge, _set_def(&"player.gold", TYPE_INT, 0), 50)
	var end_node := _add_def_node(ge, EndDef.new(), 51)
	await get_tree().process_frame
	await get_tree().process_frame

	# value LineEdit에 INT로 파싱 불가한 텍스트를 입력한다(제작자 오타 시나리오).
	var widget = set_node.get_meta(&"state_effect_widget")
	var value_line: LineEdit = widget.get_node("value").get_node("val")
	value_line.text = "abc"

	ge.connect_node(str(start.id), _port_of(start, true, DialogueNode.port_type.flow), "51", _port_of(end_node, false, DialogueNode.port_type.flow))
	ge.connect_node(str(start.id), _port_of(start, true, EFFECT), "50", _port_of(set_node, false, EFFECT))

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	var def := _def_of(captured, 50) as StateSetDef
	# 조용히 0(int)으로 변환되지 않고 원본 String이 보존된다 → 타입 불일치.
	_check_typeof("F.captured_value_typeof", def.value, TYPE_STRING)
	_check("F.captured_value", def.value, "abc")
	# 저장 검증이 잘못된 literal을 잡아 저장을 차단한다(fatal).
	_check("F.save_blocked", ge._validate_runtime_snapshot(captured), false)

	# 런타임: 불일치 Variant를 변환하지 않고 Store가 type_mismatch로 거부한다(값 불변).
	var store := _make_store()
	var reports: Array = []
	var player := DialoguePlayer.new()
	player.state_mutation_evaluated.connect(func(_e, r): reports.append(r))
	player.set_mutation_state_provider(store)
	player.dialogue_resource = captured
	add_child(player)
	player.start_dialogue(captured)
	_check("F.report_count", reports.size(), 1)
	if reports.size() == 1:
		_check("F.error", reports[0]["error"], &"type_mismatch")
		_check("F.not_applied", reports[0]["applied"], false)
	_check("F.store_unchanged", store.get_value(&"player.gold"), 100)
	player.free()

	_free_editor(ge)
	await get_tree().process_frame


func _cleanup() -> void:
	if FileAccess.file_exists(GRAPH_PATH):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(GRAPH_PATH))
