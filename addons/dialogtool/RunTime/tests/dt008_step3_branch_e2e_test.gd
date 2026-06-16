# DT-008 Step 3 검증용 헤드리스 e2e 테스트(Branch End-to-End Integration).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt008_step3_branch_e2e_test.tscn
#
# 실제 DialogueManager -> DialogueUI -> DialoguePlayer provider 주입 경로에서 State Condition이
# 기존 Branch를 제어하는지 검증한다. 통합 그래프:
#   Start -> Branch(state_condition) -> Say "TRUE" / Say "FALSE" -> End
# provider는 실제 WorldStateStore(bootstrap schema)를 주입한다(/root 직접 조회 없음).
#
# 검증 범위:
# - 상태 변경(set_value/reset_value/import_snapshot) 뒤 재실행 결과가 Store 최종값과 일치.
# - provider 미지정/조건 오류는 false Flow이며 크래시/자동 true 없음.
# - condition_evaluated signal의 node(=state_condition)/consumer(=branch)/report.passed 검증.
# - 반복 실행과 dialogue 교체에서 이전 provider/report가 새 실행에 섞이지 않음(latest-wins).
#
# 제외: Choice filtering(Step 4), 에디터 UI(Step 2).
extends Node

const OP := StateCondition.Operator
const LG := ConditionGroup.Logic
const SCHEMA_PATH := "res://addons/dialogtool/examples/world_state_schema_example.tres"

var _failures: int = 0
var _stores: Array = []   # 만든 Store를 추적해 정리.


func _ready() -> void:
	# Watchdog: await 기반 행이 생겨도 무한 대기하지 않고 30초 후 진단을 남기고 종료한다.
	# 정상 완료(보통 1~2초)면 아래 quit가 먼저 발화한다. 이 테스트는 --import 후 실행해야 한다
	# (새 class_name 추가 시 부팅 시 재임포트가 길어질 수 있다 — 헤더 참조).
	_install_watchdog(30.0)
	await _run_all()
	for s in _stores:
		if is_instance_valid(s):
			s.free()
	if _failures == 0:
		print("[DT-008 Step3] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-008 Step3] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _install_watchdog(seconds: float) -> void:
	var t := get_tree().create_timer(seconds)
	t.timeout.connect(func() -> void:
		print("[DT-008 Step3] WATCHDOG TIMEOUT after %.0fs — 미완료 종료(행 가능성). --import 선행 여부 확인." % seconds)
		get_tree().quit(2))


func _run_all() -> void:
	await _test_default_false()
	await _test_set_value_true()
	await _test_reset_value_false()
	await _test_snapshot_restore_true()
	await _test_provider_unset_false()
	await _test_condition_error_false()
	await _test_signal_node_consumer_report()
	await _test_repeat_and_replacement_isolation()


# --- 헬퍼 -------------------------------------------------------------

func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


func _make_store() -> WorldStateStore:
	var schema: StateSchema = ResourceLoader.load(SCHEMA_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var store := WorldStateStore.new()
	store.schema = schema
	store.initialize()
	_stores.append(store)
	return store


func _n(type: StringName, params: Dictionary = {}) -> Dictionary:
	return {"type": type, "params": params}


func _c(from_id: int, from_port: int, to_id: int, to_port: int) -> Dictionary:
	return {"from_node_id": from_id, "from_port": from_port, "to_node_id": to_id, "to_port": to_port}


func _state(key: StringName, op: int, expected: Variant) -> StateCondition:
	var s := StateCondition.new()
	s.key = key
	s.operator = op
	s.expected_value = expected
	return s


func _cset(root) -> ConditionSet:
	var cs := ConditionSet.new()
	cs.root = root
	return cs


# read 호출을 세는 duck-typed provider(폐기 player가 평가했는지 검출용).
class _CountingProvider:
	var data: Dictionary
	var has_calls: int = 0
	var read_calls: int = 0
	func _init(d: Dictionary = {}) -> void:
		data = d
	func has_state(key: StringName) -> bool:
		has_calls += 1
		return data.has(key)
	func read_state(key: StringName) -> Variant:
		read_calls += 1
		return data.get(key)


# Start -> Branch(state_condition) -> Say "TRUE"/"FALSE" -> End 통합 그래프.
func _graph(condition_set) -> DialogueGraphResource:
	var nodes := {
		0: _n(&"start"),
		1: _n(&"branch"),
		2: _n(&"state_condition", {"condition_set": condition_set}),
		3: _n(&"say", {"text": "TRUE"}),
		4: _n(&"say", {"text": "FALSE"}),
		5: _n(&"end"),
	}
	var conns := [
		_c(0, 0, 1, 1),   # start flow -> branch flow-in(to_port 1)
		_c(2, 0, 1, 0),   # state_condition -> branch 조건 입력(to_port 0)
		_c(1, 0, 3, 0),   # branch true -> Say TRUE
		_c(1, 1, 4, 0),   # branch false -> Say FALSE
		_c(3, 0, 5, 0),
		_c(4, 0, 5, 0),
	]
	var res := DialogueGraphResource.new()
	res.runtime_nodes = nodes
	var typed: Array[Dictionary] = []
	for c in conns:
		typed.append(c)
	res.runtime_connections = typed
	res.start_node_id = 0
	return res


# DialogueManager 경로로 그래프를 실행하고 첫 display_text say + condition_evaluated 발행을 수집한다.
func _run(condition_set, provider) -> Dictionary:
	var says: Array = []
	var events: Array = []
	var say_cb := func(req: Dictionary):
		if req.get("type") == "display_text":
			says.append(req.get("say"))
	DialogueManager.ui_request.connect(say_cb)

	DialogueManager.play(_graph(condition_set), provider)
	# UI/player는 play() 내부 add_child로 동기 생성된다 → 시작(deferred) 전에 signal 연결 가능.
	var player: DialoguePlayer = DialogueManager._ui.dialogue_player
	var ev_cb := func(cid: int, consumer: int, report: Dictionary):
		events.append({"cid": cid, "consumer": consumer, "report": report})
	player.condition_evaluated.connect(ev_cb)

	await get_tree().process_frame   # deferred start
	await get_tree().process_frame   # 실행/분기

	var first_say = says[0] if says.size() > 0 else null
	DialogueManager.ui_request.disconnect(say_cb)
	DialogueManager._dismiss()
	await get_tree().process_frame
	return {"say": first_say, "events": events}


# --- 시나리오 ---------------------------------------------------------

func _test_default_false() -> void:
	print("[A] default 상태 -> FALSE 분기")
	var store := _make_store()   # quest.main.stage default 0
	var cs := _cset(_state(&"quest.main.stage", OP.GREATER_EQUAL, 3))
	var r := await _run(cs, store)
	_check("A.say", r["say"], "FALSE")
	_check("A.passed", r["events"][0]["report"]["passed"], false)


func _test_set_value_true() -> void:
	print("[B] set_value 후 -> TRUE 분기")
	var store := _make_store()
	store.set_value(&"quest.main.stage", 5)
	var cs := _cset(_state(&"quest.main.stage", OP.GREATER_EQUAL, 3))
	var r := await _run(cs, store)
	_check("B.say", r["say"], "TRUE")
	_check("B.passed", r["events"][0]["report"]["passed"], true)


func _test_reset_value_false() -> void:
	print("[C] reset_value 후 -> FALSE 분기")
	var store := _make_store()
	store.set_value(&"quest.main.stage", 5)
	store.reset_value(&"quest.main.stage")   # default 0 복귀
	var cs := _cset(_state(&"quest.main.stage", OP.GREATER_EQUAL, 3))
	var r := await _run(cs, store)
	_check("C.say", r["say"], "FALSE")


func _test_snapshot_restore_true() -> void:
	print("[D] snapshot restore 후 -> Store 최종값과 일치(TRUE)")
	var store := _make_store()
	store.set_value(&"quest.main.stage", 5)
	var snap := store.export_snapshot()           # stage=5 보존
	store.set_value(&"quest.main.stage", 0)        # 거짓 상태로 변경
	store.import_snapshot(snap)                    # stage=5 복원
	var cs := _cset(_state(&"quest.main.stage", OP.GREATER_EQUAL, 3))
	var r := await _run(cs, store)
	_check("D.say", r["say"], "TRUE")


func _test_provider_unset_false() -> void:
	print("[E] provider 미지정 -> FALSE 분기(provider_missing, 크래시/자동 true 없음)")
	var cs := _cset(_state(&"quest.main.stage", OP.GREATER_EQUAL, 3))
	var r := await _run(cs, null)
	_check("E.say", r["say"], "FALSE")
	_check("E.passed", r["events"][0]["report"]["passed"], false)
	_check("E.valid", r["events"][0]["report"]["valid"], false)
	_check_true("E.provider_missing", _has_code(r["events"][0]["report"], "provider_missing"))


func _test_condition_error_false() -> void:
	print("[F] 조건 오류(미등록 key) -> FALSE 분기(state_missing)")
	var store := _make_store()
	# 형식은 유효하지만 schema에 없는 key -> state_missing -> errored -> false.
	var cs := _cset(_state(&"quest.unknown.key", OP.EQUAL, 1))
	var r := await _run(cs, store)
	_check("F.say", r["say"], "FALSE")
	_check_true("F.state_missing", _has_code(r["events"][0]["report"], "state_missing"))


func _test_signal_node_consumer_report() -> void:
	print("[G] condition_evaluated node/consumer/report 검증")
	var store := _make_store()
	store.set_value(&"quest.main.stage", 5)
	var cs := _cset(_state(&"quest.main.stage", OP.GREATER_EQUAL, 3))
	var r := await _run(cs, store)
	_check("G.one_event", r["events"].size(), 1)
	_check("G.condition_node", r["events"][0]["cid"], 2)   # state_condition node id
	_check("G.consumer_branch", r["events"][0]["consumer"], 1)   # branch node id
	_check("G.report_passed", r["events"][0]["report"]["passed"], true)
	_check("G.matches_branch", r["say"], "TRUE")


func _test_repeat_and_replacement_isolation() -> void:
	print("[H] 반복 실행/교체에서 provider/report 미혼입")
	var cs := _cset(_state(&"quest.main.stage", OP.GREATER_EQUAL, 3))

	# H1 반복: 서로 다른 Store 값으로 두 번 실행 — 각 결과가 자기 Store와 일치.
	var store_true := _make_store()
	store_true.set_value(&"quest.main.stage", 5)
	var store_false := _make_store()   # default 0
	var r1 := await _run(cs, store_true)
	_check("H1.first_true", r1["say"], "TRUE")
	var r2 := await _run(cs, store_false)
	_check("H1.second_false", r2["say"], "FALSE")

	# H2 같은 프레임 교체(latest-wins): play(true) 직후 play(false).
	# Manager의 source guard가 폐기 UI의 say를 숨기므로, say만 보면 폐기 player가 실제로 조건을
	# 평가해도 통과할 수 있다(리뷰 P1). 따라서 두 player의 condition_evaluated를 각각 수집하고
	# 폐기 provider의 read 횟수까지 단언해 폐기 대화가 평가되지 않았음을 직접 확인한다.
	var prov_true := _CountingProvider.new({&"quest.main.stage": 5})    # 첫(폐기) provider
	var prov_false := _CountingProvider.new({&"quest.main.stage": 0})   # 최종(활성) provider
	var says: Array = []
	var events_a: Array = []
	var events_b: Array = []
	var say_cb := func(req: Dictionary):
		if req.get("type") == "display_text":
			says.append(req.get("say"))
	DialogueManager.ui_request.connect(say_cb)

	DialogueManager.play(_graph(cs), prov_true)
	var player_a: DialoguePlayer = DialogueManager._ui.dialogue_player
	player_a.condition_evaluated.connect(func(_cid, _c, report: Dictionary): events_a.append(report))

	DialogueManager.play(_graph(cs), prov_false)   # 같은 프레임 교체 -> 첫 대화 폐기(cancel_pending_start)
	var player_b: DialoguePlayer = DialogueManager._ui.dialogue_player
	player_b.condition_evaluated.connect(func(_cid, _c, report: Dictionary): events_b.append(report))

	await get_tree().process_frame
	await get_tree().process_frame
	DialogueManager.ui_request.disconnect(say_cb)

	_check_true("H2.distinct_players", player_a != player_b)
	_check("H2.only_last_say", says, ["FALSE"])
	_check("H2.discarded_no_eval", events_a.size(), 0)            # 폐기 player 평가 0회
	_check("H2.active_one_eval", events_b.size(), 1)             # 활성 player 평가 1회
	if events_b.size() == 1:
		_check("H2.active_passed_false", events_b[0]["passed"], false)
	# 폐기 대화가 평가되지 않았으면 첫 provider는 한 번도 read되지 않는다.
	_check("H2.discarded_provider_untouched", prov_true.has_calls + prov_true.read_calls, 0)
	# 활성 대화는 실제로 평가했으므로 최종 provider는 read됐다(양성 대조).
	_check_true("H2.active_provider_read", prov_false.has_calls + prov_false.read_calls > 0)

	DialogueManager._dismiss()
	await get_tree().process_frame


func _has_code(report: Dictionary, code: String) -> bool:
	for e in report.get("errors", []):
		if e.get("code") == code:
			return true
	return false
