# DT-004 Step 1 검증용 헤드리스 테스트.
# 실행: godot --headless res://addons/dialogtool/RunTime/tests/dt004_step1_headless_test.tscn
#
# 수작업 runtime snapshot으로 Effect 연결 실행 순서와 방어 동작을 검증한다.
# 에디터 UI 없이 DialogueGraphResource.runtime_nodes/runtime_connections를 직접 구성한다.
# DialoguePlayer가 autoload(DialogueToolUtil)에 의존하므로 --script가 아닌 씬 부팅으로 실행한다.
extends Node

var _failures: int = 0


func _ready() -> void:
	_run_all()
	if _failures == 0:
		print("[DT-004 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-004 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _run_all() -> void:
	_test_multi_portrait_then_say()
	_test_serial_backcompat()
	_test_empty_effect()
	_test_invalid_effect_target()
	_test_effect_cycle()
	_test_reentry_repeat()


# --- 헬퍼 -------------------------------------------------------------

func _make_resource(runtime_nodes: Dictionary, runtime_connections: Array) -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = runtime_nodes
	var typed: Array[Dictionary] = []
	for c in runtime_connections:
		typed.append(c)
	res.runtime_connections = typed
	res.start_node_id = 0
	return res


# player를 실행하고 발행된 ui_request 로그를 반환한다.
func _run(res: DialogueGraphResource) -> Array:
	var player := DialoguePlayer.new()
	var log: Array = []
	player.ui_request.connect(func(req: Dictionary): log.append(req))
	player.start_dialogue(res)
	var result := {"log": log, "waiting_for": player.waiting_for, "player": player}
	player.free()
	return [log, result["waiting_for"]]


func _portrait_summary(log: Array) -> Array:
	# portrait_state 요청을 (action, slot) 문자열로 요약한다.
	var out: Array = []
	for req in log:
		if req.get("type") == "portrait_state":
			out.append("%s:%s" % [req.get("action"), req.get("slot")])
		elif req.get("type") == "display_text":
			out.append("say")
		elif req.get("type") == "offer_choice":
			out.append("choice")
	return out


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _n(type: StringName, params: Dictionary = {}) -> Dictionary:
	return {"type": type, "params": params}


func _c(from_id: int, to_id: int, kind: String = "", port: int = 0) -> Dictionary:
	var d := {"from_node_id": from_id, "from_port": port, "to_node_id": to_id, "to_port": 0}
	if kind != "":
		d["kind"] = kind
	return d


# --- 시나리오 ---------------------------------------------------------

func _test_multi_portrait_then_say() -> void:
	print("[A] 다중 Portrait Effect 후 Say")
	var nodes := {
		0: _n(&"start"),
		1: _n(&"portrait_show", {"slot": "left", "texture_path": "res://a.png"}),
		2: _n(&"portrait_show", {"slot": "right", "texture_path": "res://b.png"}),
		3: _n(&"say", {"text": "hi"}),
	}
	var conns := [
		_c(0, 1, "effect"),
		_c(0, 2, "effect"),
		_c(0, 3),
	]
	var r := _run(_make_resource(nodes, conns))
	_check("A.order", _portrait_summary(r[0]), ["show:left", "show:right", "say"])
	_check("A.waiting", r[1], &"text")


func _test_serial_backcompat() -> void:
	print("[B] 기존 직렬 Portrait -> Say")
	var nodes := {
		0: _n(&"start"),
		1: _n(&"portrait_show", {"slot": "center", "texture_path": "res://a.png"}),
		2: _n(&"say", {"text": "hi"}),
	}
	var conns := [
		_c(0, 1),
		_c(1, 2),
	]
	var r := _run(_make_resource(nodes, conns))
	_check("B.order", _portrait_summary(r[0]), ["show:center", "say"])
	_check("B.waiting", r[1], &"text")


func _test_empty_effect() -> void:
	print("[C] Effect 없는 그래프 (Start -> Say)")
	var nodes := {
		0: _n(&"start"),
		1: _n(&"say", {"text": "hi"}),
	}
	var conns := [_c(0, 1)]
	var r := _run(_make_resource(nodes, conns))
	_check("C.order", _portrait_summary(r[0]), ["say"])
	_check("C.waiting", r[1], &"text")


func _test_invalid_effect_target() -> void:
	print("[D] 잘못된 Effect 대상 (Say를 Effect로 연결)")
	var nodes := {
		0: _n(&"start"),
		1: _n(&"say", {"text": "main"}),
		2: _n(&"say", {"text": "bad-effect"}),
	}
	var conns := [
		_c(0, 2, "effect"), # 잘못된 대상: skip 되어야 한다(경고).
		_c(0, 1),           # 주 Flow.
	]
	var r := _run(_make_resource(nodes, conns))
	# Portrait 발행 없음, 주 Flow의 Say 한 번만.
	_check("D.order", _portrait_summary(r[0]), ["say"])
	_check("D.waiting", r[1], &"text")


func _test_effect_cycle() -> void:
	print("[E] Effect 순환 (p1 <-> p2) 무한 루프 없음")
	var nodes := {
		0: _n(&"start"),
		1: _n(&"portrait_show", {"slot": "left", "texture_path": "res://a.png"}),
		2: _n(&"portrait_show", {"slot": "right", "texture_path": "res://b.png"}),
		3: _n(&"say", {"text": "hi"}),
	}
	var conns := [
		_c(0, 1, "effect"), # start effect -> p1
		_c(1, 2, "effect"), # p1 effect -> p2
		_c(2, 1, "effect"), # p2 effect -> p1 (순환)
		_c(0, 3),           # 주 Flow -> say
	]
	var r := _run(_make_resource(nodes, conns))
	# p1, p2 각각 한 번씩 발행 후 순환은 차단, 그 뒤 say.
	_check("E.order", _portrait_summary(r[0]), ["show:left", "show:right", "say"])
	_check("E.waiting", r[1], &"text")


func _test_reentry_repeat() -> void:
	print("[F] 같은 리소스 반복 실행 일관성")
	var nodes := {
		0: _n(&"start"),
		1: _n(&"portrait_show", {"slot": "left", "texture_path": "res://a.png"}),
		2: _n(&"say", {"text": "hi"}),
	}
	var conns := [
		_c(0, 1, "effect"),
		_c(0, 2),
	]
	var res := _make_resource(nodes, conns)
	var r1 := _run(res)
	var r2 := _run(res)
	_check("F.run1", _portrait_summary(r1[0]), ["show:left", "say"])
	_check("F.run2", _portrait_summary(r2[0]), ["show:left", "say"])
