# DT-004 Step 2 검증용 헤드리스 에디터 테스트.
# 실행: godot --headless res://addons/dialogtool/RunTime/tests/dt004_step2_editor_test.tscn
#
# 실제 editor.gd(GraphEdit) 인스턴스로 Effect 포트 생성, capture의 kind 파생,
# 저장/재로드 보존, validation, 런타임 실행 순서를 검증한다.
# editor.gd의 일부 @onready(형제 UI 경로)는 이 테스트 트리에 없어 null 경고가 날 수 있으나
# capture/load/validate 경로는 그 노드들을 쓰지 않는다.
extends Node

const EDITOR_SCRIPT := preload("res://addons/dialogtool/Editor/editor.gd")
const NODE_SCENE := "res://addons/dialogtool/Node/dialogue_node.tscn"
const TMP_PATH := "res://__dt004_step2_tmp.tres"

const FLOW := DialogueNode.port_type.flow
const EFFECT := DialogueNode.port_type.effect

var _failures: int = 0


func _ready() -> void:
	await _run_all()
	if _failures == 0:
		print("[DT-004 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-004 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _run_all() -> void:
	await _test_ports_and_capture_roundtrip()
	await _test_validation_rejects_mismatch()
	await _test_legacy_flow_only_has_no_kind()
	await _test_legacy_effect_port0_roundtrip()
	_cleanup()


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _make_editor() -> GraphEdit:
	var ge := GraphEdit.new()
	ge.set_script(EDITOR_SCRIPT)
	add_child(ge)
	# editor._ready가 Start 노드를 call_deferred로 추가하므로 두 프레임 대기.
	await get_tree().process_frame
	await get_tree().process_frame
	return ge


func _add_node(ge: GraphEdit, def: DialogueDefinition, id: int) -> DialogueNode:
	var node: DialogueNode = load(NODE_SCENE).instantiate()
	def.node_id = id
	def.graph_resource = weakref(ge.graph_resource)
	node.definition = def
	node.name = str(id)
	node.id = id
	ge.add_child(node)
	node.set_owner(ge)
	return node


func _conn_kind(connections: Array, to_id: int) -> Variant:
	for c in connections:
		if c.get("to_node_id") == to_id:
			return c.get("kind", "<none>")
	return "<missing>"


# 노드를 이름이 아닌 id로 찾는다. load_resource는 clear_graph(deferred queue_free)
# 때문에 재로드된 노드가 일시적으로 다른 이름을 받을 수 있으므로 id로 조회한다.
func _find_by_id(ge: GraphEdit, id: int) -> DialogueNode:
	for child in ge.get_children():
		if child is DialogueNode and child.id == id:
			return child
	return null


# --- 시나리오 ---------------------------------------------------------

func _test_ports_and_capture_roundtrip() -> void:
	print("[A] Effect 포트 생성 + capture kind 파생 + 저장/재로드 보존")
	var ge := await _make_editor()

	# 자동 생성된 Start(id 0) + Portrait 2개(left/right) + Say.
	var def_left := PortraitShowDef.new()
	def_left.slot = "left"
	var def_right := PortraitShowDef.new()
	def_right.slot = "right"
	var p_left := await _add_node(ge, def_left, 1)
	var p_right := await _add_node(ge, def_right, 2)
	var say := await _add_node(ge, SayDef.new(), 3)
	await get_tree().process_frame

	var start: DialogueNode = ge.get_node("0")

	# 포트 계약 검사.
	_check("A.start.out_count", start.get_output_port_count(), 2)
	_check("A.start.out0_flow", start.get_output_port_type(0), FLOW)
	_check("A.start.out1_effect", start.get_output_port_type(1), EFFECT)

	_check("A.portrait.in_count", p_left.get_input_port_count(), 2)
	_check("A.portrait.in0_flow", p_left.get_input_port_type(0), FLOW)
	_check("A.portrait.in1_effect", p_left.get_input_port_type(1), EFFECT)
	_check("A.portrait.out0_flow", p_left.get_output_port_type(0), FLOW)

	_check("A.say.in0_flow", say.get_input_port_type(0), FLOW)
	_check("A.say.out_count", say.get_output_port_count(), 2)
	_check("A.say.out0_flow", say.get_output_port_type(0), FLOW)
	_check("A.say.out1_effect", say.get_output_port_type(1), EFFECT)

	# 연결: Start.effect(port1) -> 두 Portrait.effect_in(port1), Start.flow(port0) -> Say.flow_in(port0).
	ge.connect_node("0", 1, "1", 1)
	ge.connect_node("0", 1, "2", 1)
	ge.connect_node("0", 0, "3", 0)

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	_check("A.capture.conn_count", captured.connections.size(), 3)
	_check("A.capture.effect_kind_pL", _conn_kind(captured.connections, 1), "effect")
	_check("A.capture.effect_kind_pR", _conn_kind(captured.connections, 2), "effect")
	_check("A.capture.flow_kind_say", _conn_kind(captured.connections, 3), "<none>")

	# validation 통과(유효 그래프는 저장된다).
	_check("A.validate", ge._validate_runtime_snapshot(captured), true)

	# Effect 대상 저장 순서(= 실행 순서) 보존 기준값.
	var captured_effect_order := _effect_targets_in_order(captured.connections)
	_check("A.capture.effect_order", captured_effect_order, [1, 2])

	# 런타임 실행 순서: left -> right Portrait 발행 후 Say.
	var summary := _run_player(captured)
	_check("A.runtime.order", summary, ["show:left", "show:right", "say"])

	# 저장 -> 재로드 -> 재캡처 보존.
	var save_err := ResourceSaver.save(captured, TMP_PATH)
	_check("A.save_ok", save_err, OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(TMP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)

	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame

	var recaptured: DialogueGraphResource = ge2.capture_current_graphedit()
	_check("A.reload.conn_count", recaptured.connections.size(), 3)
	_check("A.reload.effect_kind_pL", _conn_kind(recaptured.connections, 1), "effect")
	_check("A.reload.effect_kind_pR", _conn_kind(recaptured.connections, 2), "effect")
	_check("A.reload.flow_kind_say", _conn_kind(recaptured.connections, 3), "<none>")

	# 재로드된 노드의 포트 계약도 동일해야 한다(이름이 아닌 id로 조회).
	var r_start: DialogueNode = _find_by_id(ge2, 0)
	var r_portrait: DialogueNode = _find_by_id(ge2, 1)
	_check("A.reload.start_found", r_start != null, true)
	_check("A.reload.portrait_found", r_portrait != null, true)
	_check("A.reload.start.out1_effect", r_start.get_output_port_type(1), EFFECT)
	_check("A.reload.portrait.in1_effect", r_portrait.get_input_port_type(1), EFFECT)

	# 순서 보존(P2): 재캡처된 Effect 대상 순서와 재실행 순서가 저장 전과 같아야 한다.
	_check("A.reload.effect_order", _effect_targets_in_order(recaptured.connections), captured_effect_order)
	var reload_summary := _run_player(recaptured)
	_check("A.reload.runtime.order", reload_summary, ["show:left", "show:right", "say"])

	ge.queue_free()
	ge2.queue_free()
	await get_tree().process_frame


func _test_validation_rejects_mismatch() -> void:
	print("[B] Flow↔Effect 잘못된 연결을 validation이 거부")
	var ge := await _make_editor()
	await _add_node(ge, PortraitShowDef.new(), 1)
	await get_tree().process_frame

	# 정상 노드로 nodes를 채운 뒤 연결만 잘못된 것으로 교체:
	# Start.flow(port0) -> Portrait.effect_in(port1)  (flow 출력을 effect 입력에 연결)
	var res: DialogueGraphResource = ge.capture_current_graphedit()
	res.connections = [{
		"from_node_id": 0, "from_port": 0,
		"to_node_id": 1, "to_port": 1,
	}]
	_check("B.validate_rejects", ge._validate_runtime_snapshot(res), false)

	ge.queue_free()
	await get_tree().process_frame


func _test_legacy_flow_only_has_no_kind() -> void:
	print("[C] Flow 전용(레거시 형태) 그래프는 kind가 붙지 않는다")
	var ge := await _make_editor()
	await _add_node(ge, SayDef.new(), 1)
	await get_tree().process_frame

	# Start.flow(port0) -> Say.flow_in(port0): 순수 Flow 연결.
	ge.connect_node("0", 0, "1", 0)
	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	_check("C.conn_count", captured.connections.size(), 1)
	_check("C.no_kind", _conn_kind(captured.connections, 1), "<none>")
	_check("C.validate", ge._validate_runtime_snapshot(captured), true)

	ge.queue_free()
	await get_tree().process_frame


# Step 1 시대 리소스 모사: Effect를 Flow 포트(0→0)에 kind="effect"로 저장한 형태.
func _make_legacy_effect_resource() -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	var start_def := StartDef.new()
	start_def.node_id = 0
	var p_def := PortraitShowDef.new()
	p_def.node_id = 1
	p_def.slot = "left"
	var say_def := SayDef.new()
	say_def.node_id = 2
	say_def.say_text = "hi"
	res.nodes = {
		0: {"name": "0", "size": Vector2(), "position_offset": Vector2(), "definition": start_def, "id": 0},
		1: {"name": "1", "size": Vector2(), "position_offset": Vector2(), "definition": p_def, "id": 1},
		2: {"name": "2", "size": Vector2(), "position_offset": Vector2(), "definition": say_def, "id": 2},
	}
	var conns: Array[Dictionary] = [
		{"from_node_id": 0, "from_port": 0, "to_node_id": 1, "to_port": 0, "kind": "effect"},
		{"from_node_id": 0, "from_port": 0, "to_node_id": 2, "to_port": 0},
	]
	res.connections = conns
	res.start_node_id = 0
	res.next_node_id = 3
	return res


# [P1 회귀] kind="effect" + 포트 0→0 리소스를 에디터에 로드하면, kind를 신뢰해
# Effect 포트로 정규화해야 한다. 재캡처 후에도 effect kind가 보존되고(직렬 Flow로
# 둔갑하지 않음), 실행 순서가 유지되어야 한다.
func _test_legacy_effect_port0_roundtrip() -> void:
	print("[D] Step 1 형태(kind=effect, port 0->0) 로드->재캡처->실행 보존")
	var legacy := _make_legacy_effect_resource()

	var ge := await _make_editor()
	ge.load_resource(legacy)
	await get_tree().process_frame
	await get_tree().process_frame

	var recap: DialogueGraphResource = ge.capture_current_graphedit()
	# Portrait(id 1) 연결이 effect로 보존되고, Say(id 2)는 Flow로 남아야 한다.
	_check("D.portrait_effect_preserved", _conn_kind(recap.connections, 1), "effect")
	_check("D.say_flow", _conn_kind(recap.connections, 2), "<none>")

	# 재캡처 노드의 Effect 연결이 실제 Effect 포트로 정규화됐는지(port 1) 확인.
	var portrait_conn_port := -1
	for c in recap.connections:
		if c.get("to_node_id") == 1:
			portrait_conn_port = c.get("from_port")
	_check("D.effect_port_normalized", portrait_conn_port, 1)

	# 실행: Portrait(left)가 비대기 Effect로 발행된 뒤 Say. (직렬 Flow였다면 깨진다.)
	_check("D.runtime.order", _run_player(recap), ["show:left", "say"])

	ge.queue_free()
	await get_tree().process_frame


# 캡처된 리소스를 DialoguePlayer로 실행해 ui_request 순서를 요약한다.
# portrait_state는 "action:slot", display_text는 "say"로 요약해 좌/우 순서를 구분한다.
func _summarize_request(req: Dictionary) -> String:
	var t: String = req.get("type", "?")
	if t == "portrait_state":
		return "%s:%s" % [req.get("action"), req.get("slot")]
	if t == "display_text":
		return "say"
	return t


func _run_player(res: DialogueGraphResource) -> Array:
	var player := DialoguePlayer.new()
	var log: Array = []
	player.ui_request.connect(func(req: Dictionary): log.append(_summarize_request(req)))
	player.start_dialogue(res)
	player.free()
	return log


# connections에서 Effect 대상 to_node_id를 저장(배열) 순서대로 반환한다.
func _effect_targets_in_order(connections: Array) -> Array:
	var out: Array = []
	for c in connections:
		if c.get("kind", "") == "effect":
			out.append(c.get("to_node_id"))
	return out


func _cleanup() -> void:
	if FileAccess.file_exists(TMP_PATH):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(TMP_PATH))
		var err := DirAccess.remove_absolute(TMP_PATH)
		if err != OK:
			# globalize 경로/res 경로 둘 다 시도(에디터/익스포트 환경 차이 방어).
			pass
