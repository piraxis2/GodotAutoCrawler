# DT-008 Step 4 검증용 헤드리스 테스트(Conditional Choice Runtime Mapping).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/dialogtool/RunTime/tests/dt008_step4_conditional_choice_test.tscn
#
# Choice의 항목별 Data 입력(port i+1)을 조건으로 사용하면서 visible index를 원래 flow 출력 port(i)로
# 안전하게 되돌리는지 검증한다(ADR-009 D5/D6, F5). 직접 구성한 runtime snapshot + fake provider.
#
# 포트 계약(choice_node.gd): 항목 i 데이터 입력 = port i+1, flow 출력 = port i.
#
# 검증 범위:
# - 첫/중간/마지막 숨김, 복수 숨김, 전부 숨김 → visible list와 select 후 원래 Flow 일치.
# - no-input 레거시 Choice는 identity(이전과 동일).
# - 잘못된 visible index는 대기 유지(Flow 불변).
# - 평가 후 상태가 바뀌어도 현재 목록/mapping 고정(대기 중 재평가 없음), 재진입에서만 갱신.
# - condition_evaluated consumer == choice id.
extends Node

const OP := StateCondition.Operator

var _failures: int = 0


func _ready() -> void:
	_test_first_hidden()
	_test_middle_hidden_critical()
	_test_last_hidden()
	_test_multiple_hidden()
	_test_all_hidden_ends()
	_test_legacy_no_input()
	_test_invalid_visible_index_keeps_wait()
	_test_error_condition_hides()
	_test_state_change_during_wait_frozen()
	_test_reentry_reevaluates()
	_test_signal_consumer_is_choice()
	_test_error_dominance_through_expression()

	if _failures == 0:
		print("[DT-008 Step4] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-008 Step4] FAILED: %d assertion(s)" % _failures)
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


class FakeReadProvider:
	var data: Dictionary
	func _init(d: Dictionary = {}) -> void:
		data = d
	func has_state(key: StringName) -> bool:
		return data.has(key)
	func read_state(key: StringName) -> Variant:
		return data.get(key)


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


# key == true 조건 set.
func _cond(key: StringName) -> ConditionSet:
	var s := StateCondition.new()
	s.key = key
	s.operator = OP.EQUAL
	s.expected_value = true
	var cs := ConditionSet.new()
	cs.root = s
	return cs


# Start -> Choice(item_texts). 항목 i: 출력 port i -> Say "FLOW_i" -> End.
# item_conds[i]가 ConditionSet이면 state_condition 노드를 choice 데이터 입력 port i+1에 연결.
func _choice_graph(item_texts: Array, item_conds: Array) -> DialogueGraphResource:
	var nodes := {
		0: _n(&"start"),
		1: _n(&"choice", {"choices": item_texts}),
		99: _n(&"end"),
	}
	var conns := [_c(0, 0, 1, 0)]   # start flow -> choice flow 입력(to_port 0)
	for i in range(item_texts.size()):
		var say_id := 10 + i
		nodes[say_id] = _n(&"say", {"text": "FLOW_%d" % i})
		conns.append(_c(1, i, say_id, 0))     # choice 항목 i 출력 port i -> say
		conns.append(_c(say_id, 0, 99, 0))    # say -> end
		if item_conds[i] != null:
			var cond_id := 200 + i
			nodes[cond_id] = _n(&"state_condition", {"condition_set": item_conds[i]})
			conns.append(_c(cond_id, 0, 1, i + 1))   # state_condition -> choice 데이터 입력 port i+1
	return _resource(nodes, conns)


# player를 만들고 ui_request/dialogue_end를 수집한다(트리에 붙이지 않음).
func _make(res: DialogueGraphResource, provider) -> Dictionary:
	var player := DialoguePlayer.new()
	player.dialogue_resource = res
	if provider != null:
		player.set_read_state_provider(provider)
	var log := {"offers": [], "says": [], "ended": false, "events": []}
	player.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "offer_choice":
			log["offers"].append(req.get("choices"))
		elif req.get("type") == "display_text":
			log["says"].append(req.get("say")))
	player.dialogue_end.connect(func(): log["ended"] = true)
	player.condition_evaluated.connect(func(cid: int, consumer: int, report: Dictionary):
		log["events"].append({"cid": cid, "consumer": consumer, "report": report}))
	return {"player": player, "log": log}


# --- 시나리오 ---------------------------------------------------------

func _test_first_hidden() -> void:
	print("[A] 첫 항목 숨김")
	# 항목0 조건 false(숨김), 항목1/2 무조건. visible=[B,C], map=[1,2].
	var conds := [_cond(&"flag.k0"), null, null]
	var provider := FakeReadProvider.new({&"flag.k0": false})
	var d := _make(_choice_graph(["A", "B", "C"], conds), provider)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("A.offer", d["log"]["offers"][0], ["B", "C"])
	# visible 0 -> 원래 항목1 -> FLOW_1
	d["player"].select_choice(0)
	_check("A.select0_flow", d["log"]["says"].back(), "FLOW_1")
	d["player"].free()

	# 별도 실행에서 visible 1 -> 원래 항목2 -> FLOW_2
	var d2 := _make(_choice_graph(["A", "B", "C"], conds), FakeReadProvider.new({&"flag.k0": false}))
	d2["player"].start_dialogue(d2["player"].dialogue_resource)
	d2["player"].select_choice(1)
	_check("A.select1_flow", d2["log"]["says"].back(), "FLOW_2")
	d2["player"].free()


func _test_middle_hidden_critical() -> void:
	print("[B] 중간 항목 숨김(핵심: visible index != 원래 port)")
	# 항목1 숨김. visible=[A,C], map=[0,2].
	var conds := [null, _cond(&"flag.k1"), null]
	var provider := FakeReadProvider.new({&"flag.k1": false})
	var d := _make(_choice_graph(["A", "B", "C"], conds), provider)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("B.offer", d["log"]["offers"][0], ["A", "C"])
	# visible 1 -> 원래 항목2 -> FLOW_2 (FLOW_1이 아니어야 함!)
	d["player"].select_choice(1)
	_check("B.select1_flow", d["log"]["says"].back(), "FLOW_2")
	d["player"].free()


func _test_last_hidden() -> void:
	print("[C] 마지막 항목 숨김")
	var conds := [null, null, _cond(&"flag.k2")]
	var d := _make(_choice_graph(["A", "B", "C"], conds), FakeReadProvider.new({&"flag.k2": false}))
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("C.offer", d["log"]["offers"][0], ["A", "B"])
	d["player"].select_choice(1)
	_check("C.select1_flow", d["log"]["says"].back(), "FLOW_1")
	d["player"].free()


func _test_multiple_hidden() -> void:
	print("[D] 복수 항목 숨김")
	# 항목0,2 숨김. visible=[B], map=[1].
	var conds := [_cond(&"flag.k0"), null, _cond(&"flag.k2")]
	var provider := FakeReadProvider.new({&"flag.k0": false, &"flag.k2": false})
	var d := _make(_choice_graph(["A", "B", "C"], conds), provider)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("D.offer", d["log"]["offers"][0], ["B"])
	d["player"].select_choice(0)
	_check("D.select0_flow", d["log"]["says"].back(), "FLOW_1")
	d["player"].free()


func _test_all_hidden_ends() -> void:
	print("[E] 전부 숨김 -> 명시적 종료")
	var conds := [_cond(&"flag.k0"), _cond(&"flag.k1"), _cond(&"flag.k2")]
	var provider := FakeReadProvider.new({&"flag.k0": false, &"flag.k1": false, &"flag.k2": false})
	var d := _make(_choice_graph(["A", "B", "C"], conds), provider)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("E.no_offer", d["log"]["offers"].size(), 0)
	_check("E.ended", d["log"]["ended"], true)
	_check("E.waiting_none", d["player"].waiting_for, &"none")
	d["player"].free()


func _test_legacy_no_input() -> void:
	print("[F] no-input 레거시 Choice -> identity")
	var d := _make(_choice_graph(["A", "B", "C"], [null, null, null]), null)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("F.offer", d["log"]["offers"][0], ["A", "B", "C"])
	d["player"].select_choice(2)
	_check("F.select2_flow", d["log"]["says"].back(), "FLOW_2")
	d["player"].free()


func _test_invalid_visible_index_keeps_wait() -> void:
	print("[G] 잘못된 visible index -> 대기 유지(Flow 불변)")
	var conds := [null, _cond(&"flag.k1"), null]   # 항목1 숨김 -> visible=[A,C]
	var provider := FakeReadProvider.new({&"flag.k1": false})
	var d := _make(_choice_graph(["A", "B", "C"], conds), provider)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	# 범위 밖 index 5 -> 대기 유지, say 없음, 종료 안 함.
	d["player"].select_choice(5)
	_check("G.still_waiting", d["player"].waiting_for, &"choice")
	_check("G.no_say", d["log"]["says"].size(), 0)
	_check("G.not_ended", d["log"]["ended"], false)
	# 음수 index도 동일.
	d["player"].select_choice(-1)
	_check("G.still_waiting2", d["player"].waiting_for, &"choice")
	# 이후 유효 index는 정상 진행.
	d["player"].select_choice(1)   # 원래 항목2 -> FLOW_2
	_check("G.valid_after", d["log"]["says"].back(), "FLOW_2")
	d["player"].free()


func _test_error_condition_hides() -> void:
	print("[H] invalid/error 조건 -> 숨김")
	# 항목0 조건이 missing key(state_missing -> errored -> passed false) -> 숨김. provider 미지정도 동일.
	var conds := [_cond(&"flag.missing"), null]
	var provider := FakeReadProvider.new({})   # 키 없음 -> state_missing
	var d := _make(_choice_graph(["A", "B"], conds), provider)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("H.offer", d["log"]["offers"][0], ["B"])
	# 조건 평가 signal이 발행됐고 fail-closed.
	_check_true("H.errored", d["log"]["events"].size() == 1 and d["log"]["events"][0]["report"]["passed"] == false)
	d["player"].select_choice(0)
	_check("H.select0_flow", d["log"]["says"].back(), "FLOW_1")
	d["player"].free()


func _test_state_change_during_wait_frozen() -> void:
	print("[I] 대기 중 상태 변경 -> 현재 목록/mapping 고정")
	# 진입 시 항목1 조건 true -> visible=[A,B], map=[0,1].
	var conds := [null, _cond(&"flag.k1")]
	var provider := FakeReadProvider.new({&"flag.k1": true})
	var d := _make(_choice_graph(["A", "B"], conds), provider)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("I.offer", d["log"]["offers"][0], ["A", "B"])
	# 대기 중 외부 상태를 false로 바꿔도 현재 목록/mapping은 재평가하지 않는다.
	provider.data[&"flag.k1"] = false
	# visible 1(항목B)은 여전히 선택 가능하고 원래 항목1 Flow로 진행한다.
	d["player"].select_choice(1)
	_check("I.frozen_select", d["log"]["says"].back(), "FLOW_1")
	d["player"].free()


func _test_reentry_reevaluates() -> void:
	print("[J] 재진입에서 재평가")
	# 0 start -> 1 choice. 항목0 "LOOP"(무조건, 출력 port0 -> say10 -> 다시 choice).
	# 항목1 "COND"(flag.k1, 출력 port1 -> end99).
	var nodes := {
		0: _n(&"start"),
		1: _n(&"choice", {"choices": ["LOOP", "COND"]}),
		10: _n(&"say", {"text": "LOOPED"}),
		99: _n(&"end"),
		201: _n(&"state_condition", {"condition_set": _cond(&"flag.k1")}),
	}
	var conns := [
		_c(0, 0, 1, 0),      # start -> choice 입력
		_c(1, 0, 10, 0),     # 항목0(LOOP) 출력 port0 -> say10
		_c(10, 0, 1, 0),     # say10 -> 다시 choice(루프백)
		_c(1, 1, 99, 0),     # 항목1(COND) 출력 port1 -> end
		_c(201, 0, 1, 2),    # state_condition -> choice 데이터 입력 port 2(항목1)
	]
	var provider := FakeReadProvider.new({&"flag.k1": true})
	var d := _make(_resource(nodes, conns), provider)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("J.first_offer", d["log"]["offers"][0], ["LOOP", "COND"])   # k1 true -> 둘 다
	# 상태를 false로 바꾸고 LOOP(visible 0)를 고른다 -> say10(LOOPED) text 대기.
	provider.data[&"flag.k1"] = false
	d["player"].select_choice(0)
	_check("J.looped_say", d["log"]["says"].back(), "LOOPED")
	# advance로 say10을 지나 choice로 루프백 -> 재진입 재평가.
	d["player"].advance()
	# 재진입 offer는 COND 숨김(k1=false) -> [LOOP]만.
	_check("J.offer_count", d["log"]["offers"].size(), 2)
	_check("J.reentry_offer", d["log"]["offers"].back(), ["LOOP"])
	d["player"].free()


func _test_signal_consumer_is_choice() -> void:
	print("[K] condition_evaluated consumer == choice id")
	var conds := [_cond(&"flag.k0"), null]
	var provider := FakeReadProvider.new({&"flag.k0": true})
	var d := _make(_choice_graph(["A", "B"], conds), provider)
	d["player"].start_dialogue(d["player"].dialogue_resource)
	_check("K.one_event", d["log"]["events"].size(), 1)
	_check("K.condition_node", d["log"]["events"][0]["cid"], 200)   # state_condition node id
	_check("K.consumer_choice", d["log"]["events"][0]["consumer"], 1)   # choice node id
	d["player"].free()


# error-dominance가 중첩 Expression을 통해 전파되는지(P1 리뷰 수정).
# errored 조건이 `not c`/`c or true` 같은 식으로 true로 뒤집히지 못하고 fail-closed돼야 한다.
func _test_error_dominance_through_expression() -> void:
	print("[L] error-dominance가 Expression을 통해 전파(P1)")

	# L1: missing condition -> Expression "not c" -> Choice 항목 숨김.
	var l1_nodes := {
		0: _n(&"start"),
		1: _n(&"choice", {"choices": ["A", "B"]}),
		10: _n(&"say", {"text": "FLOW_0"}),
		11: _n(&"say", {"text": "FLOW_1"}),
		99: _n(&"end"),
		200: _n(&"state_condition", {"condition_set": _cond(&"flag.missing")}),
		300: _n(&"expression", {"expression": "not c", "inputs": ["c"]}),
	}
	var l1_conns := [
		_c(0, 0, 1, 0),
		_c(1, 0, 10, 0), _c(10, 0, 99, 0),
		_c(1, 1, 11, 0), _c(11, 0, 99, 0),
		_c(200, 0, 300, 0),    # state_condition -> expression 입력 0
		_c(300, 0, 1, 1),      # expression -> choice 항목0 데이터 입력 port 1
	]
	var d1 := _make(_resource(l1_nodes, l1_conns), FakeReadProvider.new({}))
	d1["player"].start_dialogue(d1["player"].dialogue_resource)
	# 항목0("A")는 errored 조건이 'not c'(=true)로 노출되지 않고 숨겨져야 한다 -> offer ["B"].
	_check("L1.hidden_offer", d1["log"]["offers"][0], ["B"])
	d1["player"].free()

	# L2: missing condition -> Expression "c or true" -> Branch false.
	var l2_nodes := {
		0: _n(&"start"),
		1: _n(&"branch"),
		2: _n(&"say", {"text": "TRUE"}),
		3: _n(&"say", {"text": "FALSE"}),
		99: _n(&"end"),
		200: _n(&"state_condition", {"condition_set": _cond(&"flag.missing")}),
		300: _n(&"expression", {"expression": "c or true", "inputs": ["c"]}),
	}
	var l2_conns := [
		_c(0, 0, 1, 1),        # start -> branch flow-in(to_port 1)
		_c(200, 0, 300, 0),    # state_condition -> expression
		_c(300, 0, 1, 0),      # expression -> branch 조건 입력 0
		_c(1, 0, 2, 0), _c(1, 1, 3, 0),
		_c(2, 0, 99, 0), _c(3, 0, 99, 0),
	]
	var d2 := _make(_resource(l2_nodes, l2_conns), FakeReadProvider.new({}))
	d2["player"].start_dialogue(d2["player"].dialogue_resource)
	_check("L2.branch_false", d2["log"]["says"].back(), "FALSE")
	d2["player"].free()

	# L3: 정상(valid) false condition -> "not c" -> true 허용(errored 아님).
	var l3_nodes := l2_nodes.duplicate(true)
	l3_nodes[200] = _n(&"state_condition", {"condition_set": _cond(&"flag.k0")})
	l3_nodes[300] = _n(&"expression", {"expression": "not c", "inputs": ["c"]})
	var d3 := _make(_resource(l3_nodes, l2_conns), FakeReadProvider.new({&"flag.k0": false}))
	d3["player"].start_dialogue(d3["player"].dialogue_resource)
	# flag.k0 == true? false. passed=false, valid=true(errored 아님). not false = true -> TRUE.
	_check("L3.valid_false_allows_true", d3["log"]["says"].back(), "TRUE")
	d3["player"].free()

	# L4: 직접 연결된 invalid condition -> Branch false(기존처럼 fail-closed).
	var l4_nodes := {
		0: _n(&"start"), 1: _n(&"branch"),
		2: _n(&"say", {"text": "TRUE"}), 3: _n(&"say", {"text": "FALSE"}),
		99: _n(&"end"),
		200: _n(&"state_condition", {"condition_set": _cond(&"flag.missing")}),
	}
	var l4_conns := [
		_c(0, 0, 1, 1), _c(200, 0, 1, 0),
		_c(1, 0, 2, 0), _c(1, 1, 3, 0), _c(2, 0, 99, 0), _c(3, 0, 99, 0),
	]
	var d4 := _make(_resource(l4_nodes, l4_conns), FakeReadProvider.new({}))
	d4["player"].start_dialogue(d4["player"].dialogue_resource)
	_check("L4.direct_invalid_false", d4["log"]["says"].back(), "FALSE")
	d4["player"].free()
