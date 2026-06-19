# DT-004 Step 4 통합 파이프라인 테스트.
# 실행: godot --headless res://addons/world_core/dialogtool/RunTime/tests/dt004_step4_pipeline_test.tscn
#
# 두 Effect 지점(Start, Say)을 가진 통합 시나리오를 에디터에서 구성하고
# 저장 -> 재로드 -> 재캡처 후 연결 종류/순서가 보존되며, 런타임 실행 순서가
# 두 지점 모두에서 결정적인지 검증한다.
extends Node

const EDITOR_SCRIPT := preload("res://addons/world_core/dialogtool/Editor/editor.gd")
const NODE_SCENE := "res://addons/world_core/dialogtool/Node/dialogue_node.tscn"
const TMP_PATH := "res://__dt004_step4_tmp.tres"

var _failures: int = 0


func _ready() -> void:
	await _run_all()
	if _failures == 0:
		print("[DT-004 Step4-Pipeline] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-004 Step4-Pipeline] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _run_all() -> void:
	await _test_two_effect_point_roundtrip()
	_cleanup()


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


func _effect_targets(connections: Array) -> Array:
	var out: Array = []
	for c in connections:
		if c.get("kind", "") == "effect":
			out.append(c.get("to_node_id"))
	return out


func _conn_kind(connections: Array, to_id: int) -> Variant:
	for c in connections:
		if c.get("to_node_id") == to_id:
			return c.get("kind", "<none>")
	return "<missing>"


# Start와 Say 두 지점에서 Effect가 fan-out하는 시나리오를 실행해 ui_request 요약을 반환한다.
func _summarize(req: Dictionary) -> String:
	match req.get("type", "?"):
		"portrait_state": return "%s:%s" % [req.get("action"), req.get("slot")]
		"display_text": return "say"
		"offer_choice": return "choice"
		_: return req.get("type", "?")


func _run_player(res: DialogueGraphResource) -> Array:
	var player := DialoguePlayer.new()
	var log: Array = []
	player.ui_request.connect(func(req: Dictionary): log.append(_summarize(req)))
	player.start_dialogue(res)
	# Say에서 한 번 advance해 Say의 Effect와 다음 Flow(End)를 진행.
	player.advance()
	player.free()
	return log


func _test_two_effect_point_roundtrip() -> void:
	print("[A] 두 Effect 지점 그래프 저장/재로드 + 런타임 순서")
	var ge := await _make_editor()

	var d_pl := PortraitShowDef.new();       d_pl.slot = "left"
	var d_pr := PortraitShowDef.new();        d_pr.slot = "right"
	var d_el := PortraitExpressionDef.new();   d_el.slot = "left";  d_el.expression = "happy"
	var d_er := PortraitHideDef.new();         d_er.slot = "right"

	await _add_node(ge, d_pl, 1)   # Start effect 대상
	await _add_node(ge, d_pr, 2)
	await _add_node(ge, SayDef.new(), 3)
	await _add_node(ge, d_el, 4)   # Say effect 대상
	await _add_node(ge, d_er, 5)
	await _add_node(ge, EndDef.new(), 6)
	await get_tree().process_frame

	# Start(0): effect(port1) -> pL, pR ; flow(port0) -> Say
	ge.connect_node("0", 1, "1", 1)
	ge.connect_node("0", 1, "2", 1)
	ge.connect_node("0", 0, "3", 0)
	# Say(3): effect(port1) -> eL, eR ; flow(port0) -> End
	ge.connect_node("3", 1, "4", 1)
	ge.connect_node("3", 1, "5", 1)
	ge.connect_node("3", 0, "6", 0)

	var captured: DialogueGraphResource = ge.capture_current_graphedit()
	_check("A.start_effects", _conn_kind(captured.connections, 1), "effect")
	_check("A.start_effects2", _conn_kind(captured.connections, 2), "effect")
	_check("A.say_flow", _conn_kind(captured.connections, 3), "<none>")
	_check("A.say_effects", _conn_kind(captured.connections, 4), "effect")
	_check("A.say_effects2", _conn_kind(captured.connections, 5), "effect")
	_check("A.end_flow", _conn_kind(captured.connections, 6), "<none>")
	_check("A.effect_order", _effect_targets(captured.connections), [1, 2, 4, 5])
	_check("A.validate", ge._validate_runtime_snapshot(captured), true)

	# 런타임: Start 두 Effect -> Say -> (advance) Say 두 Effect -> End.
	_check("A.runtime", _run_player(captured), ["show:left", "show:right", "say", "expression:left", "hide:right"])

	# 저장 -> 재로드 -> 재캡처.
	_check("A.save", ResourceSaver.save(captured, TMP_PATH), OK)
	var reloaded: DialogueGraphResource = ResourceLoader.load(TMP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var ge2 := await _make_editor()
	ge2.load_resource(reloaded)
	await get_tree().process_frame
	await get_tree().process_frame
	var recap: DialogueGraphResource = ge2.capture_current_graphedit()
	_check("A.reload_order", _effect_targets(recap.connections), [1, 2, 4, 5])
	_check("A.reload_say_flow", _conn_kind(recap.connections, 3), "<none>")
	_check("A.reload_runtime", _run_player(recap), ["show:left", "show:right", "say", "expression:left", "hide:right"])

	ge.queue_free()
	ge2.queue_free()
	await get_tree().process_frame


func _cleanup() -> void:
	if FileAccess.file_exists(TMP_PATH):
		DirAccess.remove_absolute(TMP_PATH)
