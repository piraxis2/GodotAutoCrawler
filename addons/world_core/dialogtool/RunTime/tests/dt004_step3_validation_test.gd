# DT-004 Step 3 검증용 헤드리스 테스트(validation 행렬).
# 실행: godot --headless res://addons/world_core/dialogtool/RunTime/tests/dt004_step3_validation_test.tscn
#
# 실제 editor.gd validation으로 유효/무효 그래프를 구분한다.
extends Node

const EDITOR_SCRIPT := preload("res://addons/world_core/dialogtool/Editor/editor.gd")
const NODE_SCENE := "res://addons/world_core/dialogtool/Node/dialogue_node.tscn"

var _failures: int = 0


func _ready() -> void:
	await _run_all()
	if _failures == 0:
		print("[DT-004 Step3] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-004 Step3] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


const EFFECT := DialogueNode.port_type.effect


func _run_all() -> void:
	_test_whitelist_helper()
	_test_cycle_helper()
	_test_message_format()
	await _test_valid_multi_portrait()
	await _test_valid_serial()
	await _test_invalid_two_flow_targets()
	await _test_invalid_effect_to_say()
	await _test_invalid_whitelist_full_path()
	await _test_invalid_effect_cycle_full_path()


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


# --- 단위(순수 함수) ---------------------------------------------------

func _test_whitelist_helper() -> void:
	print("[U1] Effect 대상 화이트리스트 헬퍼")
	_check("U1.portrait_show", DialogueGraphResource.is_effect_target_type(&"portrait_show"), true)
	_check("U1.portrait_hide", DialogueGraphResource.is_effect_target_type(&"portrait_hide"), true)
	_check("U1.say_rejected", DialogueGraphResource.is_effect_target_type(&"say"), false)
	_check("U1.branch_rejected", DialogueGraphResource.is_effect_target_type(&"branch"), false)


func _e(to_id: int, from_port: int, to_port: int) -> Dictionary:
	return {"to": to_id, "from_port": from_port, "to_port": to_port}


func _test_cycle_helper() -> void:
	print("[U2] Effect 순환 검사 헬퍼(간선 경로 반환)")
	var ge := GraphEdit.new()
	ge.set_script(EDITOR_SCRIPT)
	var cyc: Array = ge._find_effect_cycle({0: [_e(1, 0, 1)], 1: [_e(2, 1, 1)], 2: [_e(0, 1, 1)]})
	_check("U2.cycle_from_ids", cyc.map(func(e): return e["from_id"]), [0, 1, 2])
	_check("U2.cycle_to_ids", cyc.map(func(e): return e["to_id"]), [1, 2, 0])
	_check("U2.cycle_has_ports", cyc[0]["from_port"] == 0 and cyc[0]["to_port"] == 1, true)
	_check("U2.self_loop", ge._find_effect_cycle({1: [_e(1, 0, 0)]}).size(), 1)
	_check("U2.acyclic_empty", ge._find_effect_cycle({0: [_e(1, 0, 0)], 1: [_e(2, 0, 0)]}).is_empty(), true)
	_check("U2.empty", ge._find_effect_cycle({}).is_empty(), true)
	ge.free()


func _test_message_format() -> void:
	print("[U3] 오류 메시지 포맷 헬퍼에 node/type/port 포함")
	var ge := GraphEdit.new()
	ge.set_script(EDITOR_SCRIPT)
	var msg: String = ge._format_port_edge(0, &"start", 1, 7, &"say", 2)
	_check("U3.from_id", msg.contains("node 0"), true)
	_check("U3.from_type", msg.contains("start"), true)
	_check("U3.from_port", msg.contains("out-port 1"), true)
	_check("U3.to_id", msg.contains("node 7"), true)
	_check("U3.to_type", msg.contains("say"), true)
	_check("U3.to_port", msg.contains("in-port 2"), true)
	ge.free()


# 테스트 전용: 노드에 Effect 입력/출력 포트를 추가한다(에디터 포트 설계상 만들 수 없는
# 위상을 validation 전체 경로로 검증하기 위함). 새 row를 추가하고 그 row에 effect 슬롯을 켠다.
func _add_test_effect_port(node: DialogueNode, is_output: bool) -> void:
	var lbl := Label.new()
	lbl.text = "test_effect"
	node.add_child(lbl)
	var row := lbl.get_index()
	if is_output:
		node.set_slot(row, false, 0, Color.WHITE, true, EFFECT, DialogueNode.EFFECT_PORT_COLOR)
	else:
		node.set_slot(row, true, EFFECT, DialogueNode.EFFECT_PORT_COLOR, false, 0, Color.WHITE)


# --- 그래프 validation 행렬 -------------------------------------------

func _test_valid_multi_portrait() -> void:
	print("[V1] 유효: 다중 Portrait Effect + 단일 Say")
	var ge := await _make_editor()
	await _add_node(ge, PortraitShowDef.new(), 1)
	await _add_node(ge, PortraitShowDef.new(), 2)
	await _add_node(ge, SayDef.new(), 3)
	await get_tree().process_frame
	ge.connect_node("0", 1, "1", 1)  # Start.effect -> pL
	ge.connect_node("0", 1, "2", 1)  # Start.effect -> pR
	ge.connect_node("0", 0, "3", 0)  # Start.flow -> Say
	var res: DialogueGraphResource = ge.capture_current_graphedit()
	_check("V1.valid", ge._validate_runtime_snapshot(res), true)
	ge.queue_free()
	await get_tree().process_frame


func _test_valid_serial() -> void:
	print("[V2] 유효: 기존 직렬 Portrait -> Say")
	var ge := await _make_editor()
	await _add_node(ge, PortraitShowDef.new(), 1)
	await _add_node(ge, SayDef.new(), 2)
	await get_tree().process_frame
	ge.connect_node("0", 0, "1", 0)  # Start.flow -> Portrait.flow_in
	ge.connect_node("1", 0, "2", 0)  # Portrait.flow_out -> Say.flow_in
	var res: DialogueGraphResource = ge.capture_current_graphedit()
	_check("V2.valid", ge._validate_runtime_snapshot(res), true)
	ge.queue_free()
	await get_tree().process_frame


func _test_invalid_two_flow_targets() -> void:
	print("[I1] 무효: 주 Flow 대상 2개(같은 포트)")
	var ge := await _make_editor()
	await _add_node(ge, SayDef.new(), 1)
	await _add_node(ge, SayDef.new(), 2)
	await get_tree().process_frame
	ge.connect_node("0", 0, "1", 0)  # Start.flow -> Say1
	ge.connect_node("0", 0, "2", 0)  # Start.flow -> Say2 (같은 포트, 두 번째)
	var res: DialogueGraphResource = ge.capture_current_graphedit()
	_check("I1.rejected", ge._validate_runtime_snapshot(res), false)
	ge.queue_free()
	await get_tree().process_frame


func _test_invalid_effect_to_say() -> void:
	print("[I2] 무효: Effect -> Say (Portrait 아닌 대상)")
	var ge := await _make_editor()
	await _add_node(ge, SayDef.new(), 1)
	await get_tree().process_frame
	# Start.effect 출력(port 1)을 Say.flow 입력(port 0)에 연결 -> 카테고리 불일치로 거부.
	ge.connect_node("0", 1, "1", 0)
	var res: DialogueGraphResource = ge.capture_current_graphedit()
	_check("I2.rejected", ge._validate_runtime_snapshot(res), false)
	ge.queue_free()
	await get_tree().process_frame


func _test_invalid_whitelist_full_path() -> void:
	print("[I3] 무효: Effect 화이트리스트(비-Portrait 대상) — validation 전체 경로")
	var ge := await _make_editor()
	var say := await _add_node(ge, SayDef.new(), 1)
	await get_tree().process_frame
	# Say에 테스트 전용 Effect 입력을 달아 카테고리는 맞고(effect↔effect) 대상 타입만 위반시킨다.
	_add_test_effect_port(say, false)
	await get_tree().process_frame
	var say_effect_in: int = ge._find_effect_port(say, false)
	# Start.effect 출력(port 1) -> Say.effect 입력. 카테고리 일치 -> (B) 화이트리스트만 fatal.
	ge.connect_node("0", 1, "1", say_effect_in)
	var res: DialogueGraphResource = ge.capture_current_graphedit()
	_check("I3.say_effect_in_found", say_effect_in != -1, true)
	_check("I3.rejected", ge._validate_runtime_snapshot(res), false)
	ge.queue_free()
	await get_tree().process_frame


func _test_invalid_effect_cycle_full_path() -> void:
	print("[I4] 무효: Effect 순환 — validation 전체 경로")
	var ge := await _make_editor()
	var p1 := await _add_node(ge, PortraitShowDef.new(), 1)
	var p2 := await _add_node(ge, PortraitShowDef.new(), 2)
	await get_tree().process_frame
	# Portrait는 본래 Effect 입력만 있으므로, 테스트 전용 Effect 출력을 달아 순환을 만든다.
	_add_test_effect_port(p1, true)
	_add_test_effect_port(p2, true)
	await get_tree().process_frame
	var p1_out: int = ge._find_effect_port(p1, true)
	var p2_out: int = ge._find_effect_port(p2, true)
	var p1_in: int = ge._find_effect_port(p1, false)
	var p2_in: int = ge._find_effect_port(p2, false)
	# p1.effect_out -> p2.effect_in, p2.effect_out -> p1.effect_in : Effect 순환.
	ge.connect_node("1", p1_out, "2", p2_in)
	ge.connect_node("2", p2_out, "1", p1_in)
	var res: DialogueGraphResource = ge.capture_current_graphedit()
	_check("I4.ports_found", p1_out != -1 and p2_out != -1 and p1_in != -1 and p2_in != -1, true)
	_check("I4.rejected", ge._validate_runtime_snapshot(res), false)
	ge.queue_free()
	await get_tree().process_frame
