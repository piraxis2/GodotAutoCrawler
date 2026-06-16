# DT-009 Step 3b 검증용 헤드리스 테스트(Per-Choice Effect Authoring).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt009_step3b_per_choice_effect_test.tscn
#
# 검증(ADR-010 Step 3b):
# - 런타임: 선택 항목의 Effect(+ 공통 Effect)만 실행되고 다른 항목 Effect는 실행되지 않는다.
# - 공통 Effect(choice_index 없음)는 어느 선택지에서도 실행된다(레거시 수작업 연결 호환).
# - 에디터: Choice 항목별 Effect 출력 포트, capture가 choice_index 보존, save→reload→recapture 보존.
# - resize: 남은 항목의 항목별 Effect 연결은 유지(포트 remap), 삭제된 항목 연결만 제거.
extends Node

const MAIN_SCENE := "res://addons/dialogtool/dialoguetool_main.tscn"
const EFFECT := DialogueNode.port_type.effect
const VT := StateDefinition.StateValueType
const GRAPH_PATH := "res://__dt009_step3b_graph.tres"

var _failures: int = 0
var _stores: Array = []


func _ready() -> void:
	_install_watchdog(40.0)
	# 런타임(동기) — 에디터 없이 hand-built snapshot.
	_test_runtime_per_choice_filter()
	_test_runtime_shared_and_legacy()
	_test_runtime_corrupt_choice_index()
	# 에디터(비동기) — 실제 fixture.
	await _test_editor_choice_index_roundtrip()
	await _test_editor_resize_preserves_per_choice()
	await _test_editor_common_effect_roundtrip()
	await _test_editor_invalid_choice_index_skipped()

	for s in _stores:
		if is_instance_valid(s):
			s.free()
	_cleanup()
	if _failures == 0:
		print("[DT-009 Step3b] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-009 Step3b] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-009 Step3b] WATCHDOG TIMEOUT after %.0fs." % seconds)
		get_tree().quit(2))


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


# 연결(런타임). choice_index가 주어지면(>=0) effect 연결에 choice_index를 기록한다.
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


func _state_def(key: StringName, vtype: int, default_value: Variant) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	return d


func _make_store() -> WorldStateStore:
	var s := StateSchema.new()
	s.schema_version = 1
	var typed: Array[StateDefinition] = []
	for d in [_state_def(&"player.gold", VT.INT, 100), _state_def(&"player.hp", VT.FLOAT, 10.0)]:
		typed.append(d)
	s.definitions = typed
	var store := WorldStateStore.new()
	store.schema = s
	store.initialize()
	_stores.append(store)
	return store


# Start→Choice(2항목), 항목0 effect→set gold=200, 항목1 effect→set gold=999,
# 공통 effect(choice_index 없음)→set hp=5.0. 항목 flow는 Say A/B.
func _choice_graph() -> DialogueGraphResource:
	var nodes := {
		0: _n(&"start"),
		1: _n(&"choice", {"choices": ["A", "B"]}),
		2: _n(&"say", {"text": "A"}),
		3: _n(&"say", {"text": "B"}),
		10: _n(&"state_set", {"key": &"player.gold", "value": 200}),
		11: _n(&"state_set", {"key": &"player.gold", "value": 999}),
		12: _n(&"state_set", {"key": &"player.hp", "value": 5.0}),
	}
	var conns := [
		_c(0, 1),                       # start flow → choice (flow-in port 0)
		_c(1, 2, "", 0, 0),             # choice 항목0 flow → Say A
		_c(1, 3, "", 1, 0),             # choice 항목1 flow → Say B
		_c(1, 10, "effect", 0, 0, 0),   # 항목0 effect → gold=200
		_c(1, 11, "effect", 0, 0, 1),   # 항목1 effect → gold=999
		_c(1, 12, "effect"),            # 공통 effect(choice_index 없음) → hp=5.0
	]
	return _make_resource(nodes, conns)


func _run_choice(store: WorldStateStore, visible_index: int) -> Dictionary:
	var player := DialoguePlayer.new()
	var reports: Array = []
	player.state_mutation_evaluated.connect(func(_e: int, r: Dictionary): reports.append(r))
	player.set_mutation_state_provider(store)
	player.start_dialogue(_choice_graph())
	player.select_choice(visible_index)
	var out := {"reports": reports, "waiting": player.waiting_for}
	player.free()
	return out


# --- 런타임 시나리오 --------------------------------------------------

func _test_runtime_per_choice_filter() -> void:
	print("[A] 선택 항목의 Effect만 실행(다른 항목 Effect 미실행)")
	# 항목0 선택 → gold=200(항목0) + hp=5.0(공통), 999(항목1)은 실행 안 됨.
	var s0 := _make_store()
	var r0 := _run_choice(s0, 0)
	_check("A.item0_gold", s0.get_value(&"player.gold"), 200)
	_check("A.item0_hp", s0.get_value(&"player.hp"), 5.0)
	_check("A.item0_report_count", r0["reports"].size(), 2)   # 항목0 + 공통

	# 항목1 선택 → gold=999(항목1) + hp=5.0(공통), 200(항목0)은 실행 안 됨.
	var s1 := _make_store()
	var r1 := _run_choice(s1, 1)
	_check("A.item1_gold", s1.get_value(&"player.gold"), 999)
	_check("A.item1_hp", s1.get_value(&"player.hp"), 5.0)
	_check("A.item1_report_count", r1["reports"].size(), 2)   # 항목1 + 공통


func _test_runtime_shared_and_legacy() -> void:
	print("[B] 공통 Effect(choice_index 없음)는 어느 선택지에서도 실행(레거시 호환)")
	# 두 선택 모두에서 hp=5.0(공통)이 적용됐는지 확인(위 A에서 양쪽 hp==5.0).
	var s0 := _make_store()
	_run_choice(s0, 0)
	var s1 := _make_store()
	_run_choice(s1, 1)
	_check("B.shared_on_item0", s0.get_value(&"player.hp"), 5.0)
	_check("B.shared_on_item1", s1.get_value(&"player.hp"), 5.0)


func _test_runtime_corrupt_choice_index() -> void:
	print("[G] choice_index 계약: 필드 없음=공통 실행, 유효 int=실행, 명시적 null/String/Dict=건너뜀(무크래시)")
	# 직접 실행되는 수작업 snapshot: 에디터 타입 검사를 거치지 않은 손상 connection.
	var nodes := {
		0: _n(&"start"),
		1: _n(&"choice", {"choices": ["A", "B"]}),
		2: _n(&"say", {"text": "A"}),
		3: _n(&"say", {"text": "B"}),
		10: _n(&"state_set", {"key": &"player.gold", "value": 222}),  # ci "0" String → skip
		11: _n(&"state_set", {"key": &"player.gold", "value": 111}),  # ci 명시적 null → skip
		12: _n(&"state_set", {"key": &"player.gold", "value": 333}),  # ci {} Dict → skip
		13: _n(&"state_set", {"key": &"player.gold", "value": 777}),  # ci 0 int → run
		14: _n(&"state_set", {"key": &"player.hp", "value": 5.0}),    # choice_index 필드 없음 → 공통 run
	}
	# 순서: 13(int, gold=777) 뒤에 11(명시적 null)을 둔다 — 만약 null이 잘못 실행되면 gold가 111로
	# 덮어써져 회귀가 드러난다(현재는 건너뛰어 777 유지).
	var conns: Array = [
		_c(0, 1),
		_c(1, 2, "", 0, 0),
		_c(1, 3, "", 1, 0),
		{"from_node_id": 1, "from_port": 0, "to_node_id": 10, "to_port": 0, "kind": "effect", "choice_index": "0"},   # String → skip
		{"from_node_id": 1, "from_port": 0, "to_node_id": 12, "to_port": 0, "kind": "effect", "choice_index": {}},    # Dict → skip
		_c(1, 13, "effect", 0, 0, 0),                                                                                  # 유효 int 0 → run
		{"from_node_id": 1, "from_port": 0, "to_node_id": 11, "to_port": 0, "kind": "effect", "choice_index": null},  # 명시적 null → skip(13 뒤)
		_c(1, 14, "effect"),                                                                                           # 필드 없음 → 공통 run
	]
	var store := _make_store()
	var player := DialoguePlayer.new()
	player.set_mutation_state_provider(store)
	player.start_dialogue(_make_resource(nodes, conns))
	player.select_choice(0)   # 손상 connection이 있어도 크래시 없이 진행해야 한다
	player.free()
	# 유효 int(13, gold=777)만 gold에 적용 — String/Dict/명시적 null(11=111)은 모두 건너뜀.
	_check("G.valid_int_ran", store.get_value(&"player.gold"), 777)
	_check("G.explicit_null_skipped", store.get_value(&"player.gold"), 777)   # 111이 아님 = null 미실행
	# choice_index 필드가 없는 연결(14)은 공통으로 실행.
	_check("G.field_absent_common_ran", store.get_value(&"player.hp"), 5.0)


# --- 에디터 시나리오 --------------------------------------------------

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


func _effect_in(node: DialogueNode) -> int:
	for i in node.get_input_port_count():
		if node.get_input_port_type(i) == EFFECT:
			return i
	return -1


func _find_start(ge: GraphEdit) -> DialogueNode:
	for child in ge.get_children():
		if child is DialogueNode and child.definition is StartDef:
			return child
	return null


func _flow_out(node: DialogueNode) -> int:
	for i in node.get_output_port_count():
		if node.get_output_port_type(i) == DialogueNode.port_type.flow:
			return i
	return -1


# 재로드된 resource로 Choice를 실행하고 visible_index를 선택한다(런타임 확인용).
func _run_reloaded_choice(res: DialogueGraphResource, store: WorldStateStore, visible_index: int) -> void:
	var player := DialoguePlayer.new()
	player.set_mutation_state_provider(store)
	player.start_dialogue(res)
	player.select_choice(visible_index)
	player.free()


func _set_def(key: StringName, vtype: int, value: Variant) -> StateSetDef:
	var d := StateSetDef.new()
	d.key = key
	d.value_type = vtype
	d.value = value
	return d


func _choice_def(items: Array) -> ChoiceDef:
	var d := ChoiceDef.new()
	var typed: Array[String] = []
	for s in items:
		typed.append(s)
	d.choices = typed
	return d


func _effect_conns_from(res: DialogueGraphResource, from_id: int) -> Array:
	var out: Array = []
	for c in res.runtime_connections:
		if c.get("from_node_id") == from_id and c.get("kind", "") == "effect":
			out.append(c)
	return out


func _test_editor_choice_index_roundtrip() -> void:
	print("[C] Choice 항목별 Effect 포트 + capture choice_index + save/reload 보존")
	var ge := await _make_editor()
	var choice := _add_def_node(ge, _choice_def(["A", "B"]), 60)
	_add_def_node(ge, _set_def(&"player.gold", TYPE_INT, 200), 61)
	_add_def_node(ge, _set_def(&"player.gold", TYPE_INT, 999), 62)
	await get_tree().process_frame
	await get_tree().process_frame

	# 2항목 Choice: 출력 0,1 = flow, 2,3 = 항목별 effect, 4 = 공통 effect.
	_check("C.out_count", choice.get_output_port_count(), 5)
	_check("C.port2_effect", choice.get_output_port_type(2), EFFECT)
	_check("C.port3_effect", choice.get_output_port_type(3), EFFECT)
	_check("C.port4_effect", choice.get_output_port_type(4), EFFECT)
	_check("C.effect_port_item0", choice.effect_port_for_choice_index(0), 2)
	_check("C.effect_port_item1", choice.effect_port_for_choice_index(1), 3)
	_check("C.common_port", choice.common_effect_port(), 4)
	# 공통 포트(4)는 capture에서 choice_index가 부여되지 않는다(-1 = 없음).
	_check("C.common_no_ci", choice.effect_choice_index_for_port(4), -1)

	# 항목0 effect → 61, 항목1 effect → 62.
	ge.connect_node("60", 2, "61", _effect_in(_find_by_id(ge, 61)))
	ge.connect_node("60", 3, "62", _effect_in(_find_by_id(ge, 62)))

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	var eff := _effect_conns_from(captured, 60)
	_check("C.cap_effect_count", eff.size(), 2)
	# choice_index가 to_node와 일치하는지: 61→ci0, 62→ci1.
	for c in eff:
		if c["to_node_id"] == 61:
			_check("C.cap_61_ci", c.get("choice_index"), 0)
		elif c["to_node_id"] == 62:
			_check("C.cap_62_ci", c.get("choice_index"), 1)

	_check("C.save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame
	var recap: DialogueGraphResource = ge2.capture_current_graphedit()
	var eff2 := _effect_conns_from(recap, 60)
	_check("C.reload_effect_count", eff2.size(), 2)
	for c in eff2:
		if c["to_node_id"] == 61:
			_check("C.reload_61_ci", c.get("choice_index"), 0)
		elif c["to_node_id"] == 62:
			_check("C.reload_62_ci", c.get("choice_index"), 1)

	_free_editor(ge)
	_free_editor(ge2)
	await get_tree().process_frame


func _test_editor_resize_preserves_per_choice() -> void:
	print("[D] Choice resize: 남은 항목 Effect 연결 유지 + 삭제 항목만 제거")
	var ge := await _make_editor()
	var choice := _add_def_node(ge, _choice_def(["A", "B"]), 70)
	_add_def_node(ge, _set_def(&"player.gold", TYPE_INT, 200), 71)
	_add_def_node(ge, _set_def(&"player.gold", TYPE_INT, 999), 72)
	_add_def_node(ge, _set_def(&"player.hp", TYPE_FLOAT, 5.0), 73)
	await get_tree().process_frame
	await get_tree().process_frame

	# 항목0 effect(port 2) → 71, 항목1 effect(port 3) → 72, 공통 effect(port 4) → 73.
	ge.connect_node("70", 2, "71", _effect_in(_find_by_id(ge, 71)))
	ge.connect_node("70", 3, "72", _effect_in(_find_by_id(ge, 72)))
	ge.connect_node("70", choice.common_effect_port(), "73", _effect_in(_find_by_id(ge, 73)))

	# 2 → 1로 축소: 항목1(72)은 제거, 항목0(71)·공통(73)은 새 base로 remap되어 유지.
	choice.update_item(1)
	await get_tree().process_frame

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	var eff := _effect_conns_from(captured, 70)
	var by_to := {}
	for c in eff:
		by_to[c["to_node_id"]] = c
	_check("D.after_shrink_count", eff.size(), 2)        # 항목0 + 공통
	_check("D.item0_survived", by_to.has(71), true)
	_check("D.item1_dropped", by_to.has(72), false)
	_check("D.common_survived", by_to.has(73), true)
	if by_to.has(71):
		_check("D.item0_ci", by_to[71].get("choice_index"), 0)
	if by_to.has(73):
		# 공통 연결은 resize 후에도 choice_index가 없어야 한다(항목0 오염 방지).
		_check("D.common_no_ci", by_to[73].has("choice_index"), false)
	# 새 base(1항목): 항목0 effect 포트 = 1, 공통 포트 = 2.
	_check("D.remapped_item_port", choice.effect_port_for_choice_index(0), 1)
	_check("D.remapped_common_port", choice.common_effect_port(), 2)

	_free_editor(ge)
	await get_tree().process_frame


func _test_editor_common_effect_roundtrip() -> void:
	print("[E] 공통 Effect 저장→재로드→재캡처 후 choice_index 부재 + 양쪽 선택 실행")
	var ge := await _make_editor()
	var start := _find_start(ge)
	var choice := _add_def_node(ge, _choice_def(["A", "B"]), 90)
	_add_def_node(ge, _set_def(&"player.gold", TYPE_INT, 200), 92)
	await get_tree().process_frame
	await get_tree().process_frame

	# start flow → choice(flow-in 0), 공통 effect 포트 → state_set.
	ge.connect_node(str(start.id), _flow_out(start), "90", 0)
	ge.connect_node("90", choice.common_effect_port(), "92", _effect_in(_find_by_id(ge, 92)))

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	var eff := _effect_conns_from(captured, 90)
	_check("E.cap_count", eff.size(), 1)
	if eff.size() == 1:
		_check("E.cap_no_ci", eff[0].has("choice_index"), false)

	_check("E.save", ResourceSaver.save(captured, GRAPH_PATH), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(GRAPH_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame
	var recap: DialogueGraphResource = ge2.capture_current_graphedit()
	var eff2 := _effect_conns_from(recap, 90)
	_check("E.reload_count", eff2.size(), 1)
	if eff2.size() == 1:
		# 핵심 회귀: 공통 연결이 저장/재로드 후에도 choice_index 없이 유지된다(항목0 전용으로 변하지 않음).
		_check("E.reload_no_ci", eff2[0].has("choice_index"), false)

	# 재로드 resource로 양쪽 선택 모두 공통 Effect가 실행되는지 확인.
	var s0 := _make_store()
	_run_reloaded_choice(reloaded, s0, 0)
	_check("E.item0_common", s0.get_value(&"player.gold"), 200)
	var s1 := _make_store()
	_run_reloaded_choice(reloaded, s1, 1)
	_check("E.item1_common", s1.get_value(&"player.gold"), 200)

	_free_editor(ge)
	_free_editor(ge2)
	await get_tree().process_frame


func _test_editor_invalid_choice_index_skipped() -> void:
	print("[F] 잘못된 choice_index → 첫 포트 fallback 없이 연결 건너뜀(항목0 오염 방지)")
	var ge := await _make_editor()
	var choice := _add_def_node(ge, _choice_def(["A", "B"]), 80)
	_add_def_node(ge, _set_def(&"player.gold", TYPE_INT, 200), 81)
	await get_tree().process_frame
	await get_tree().process_frame
	ge.connect_node("80", choice.effect_port_for_choice_index(0), "81", _effect_in(_find_by_id(ge, 81)))

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	# 범위 밖 choice_index로 손상시킨다(에디터에서 직접 만들 수 없는 손상 .tres 시나리오).
	for c in captured.connections:
		if c.get("kind", "") == "effect" and c.get("to_node_id") == 81:
			c["choice_index"] = 99

	var ge2 := await _make_editor()
	ge2.load_resource(captured)
	await get_tree().process_frame
	await get_tree().process_frame
	var recap: DialogueGraphResource = ge2.capture_current_graphedit()
	# 잘못된 choice_index 연결은 첫 effect 포트로 fallback되지 않고 건너뛰어진다 → 81로의 effect 연결 없음.
	_check("F.invalid_skipped", _effect_conns_from(recap, 80).size(), 0)

	_free_editor(ge)
	_free_editor(ge2)
	await get_tree().process_frame


func _cleanup() -> void:
	if FileAccess.file_exists(GRAPH_PATH):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(GRAPH_PATH))
