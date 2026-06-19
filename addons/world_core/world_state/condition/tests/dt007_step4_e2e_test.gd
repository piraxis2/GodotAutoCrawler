# DT-007 Step 4 검증용 헤드리스 테스트(Integration Regression and Completion).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/world_state/condition/tests/dt007_step4_e2e_test.tscn
#
# 목표: ConditionSet/Evaluator를 독립 공용 계층으로 end-to-end 검증한다.
#
# 검증 범위:
# - 대표 RPG 시나리오 ConditionSet을 `.tres`로 저장 -> cache 무시 재로드.
# - 재로드한 set이 실제 WorldStateStore에서 in-memory set과 동일한 passed/valid/read_count/trace를 낸다.
# - 여러 lifecycle 상태(default/set/snapshot restore)에서 결과·trace가 실제 상태와 일치한다.
# - 성능 sanity: 최대 허용 node(4096) 트리가 validate+evaluate되고, 같은 key 반복은 read 1회.
# - malformed/missing/type mismatch가 fail-closed이며 Store 값과 signal이 불변(pure read).
extends Node

const OP := StateCondition.Operator
const LG := ConditionGroup.Logic
const SCHEMA_PATH := "res://addons/world_core/world_state/examples/world_state_schema_example.tres"
const TMP_PATH := "user://dt007_step4_scenario.tres"

const STAGE := &"quest.main.stage"
const AFFINITY := &"actor.example.affinity"
const SEEN := &"session.intro.seen"

var _failures: int = 0
var _signal_count: int = 0


func _ready() -> void:
	_test_scenario_roundtrip_and_trace_parity()
	_test_lifecycle_states()
	_test_snapshot_restore_e2e()
	_test_performance_sanity()
	_test_fail_closed_store_unchanged()
	_cleanup()

	if _failures == 0:
		print("[DT-007 Step4] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-007 Step4] FAILED: %d assertion(s)" % _failures)
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


func _has_code(r: Dictionary, code: String) -> bool:
	for e in r.get("errors", []):
		if e["code"] == code:
			return true
	return false


func _state(key: StringName, op: int, expected: Variant) -> StateCondition:
	var s := StateCondition.new()
	s.key = key
	s.operator = op
	s.expected_value = expected
	return s


func _group(logic: int, children: Array) -> ConditionGroup:
	var g := ConditionGroup.new()
	g.logic = logic
	var typed: Array[ConditionClause] = []
	for c in children:
		typed.append(c)
	g.children = typed
	return g


# 대표 RPG 시나리오:
# ALL
#   quest.main.stage >= 3
#   ANY
#     actor.example.affinity >= 10
#     NOT
#       session.intro.seen == true
func _make_scenario() -> ConditionSet:
	var root := _group(LG.ALL, [
		_state(STAGE, OP.GREATER_EQUAL, 3),
		_group(LG.ANY, [
			_state(AFFINITY, OP.GREATER_EQUAL, 10),
			_group(LG.NOT, [_state(SEEN, OP.EQUAL, true)]),
		]),
	])
	var cs := ConditionSet.new()
	cs.root = root
	cs.description = "rpg gate sample"
	cs.tags = [&"quest", &"gate"] as Array[StringName]
	return cs


func _make_store() -> WorldStateStore:
	var schema: StateSchema = ResourceLoader.load(SCHEMA_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var store := WorldStateStore.new()
	store.schema = schema
	store.initialize()
	return store


func _set_state(store: WorldStateStore, stage: int, affinity: int, seen: bool) -> void:
	store.set_value(STAGE, stage)
	store.set_value(AFFINITY, affinity)
	store.set_value(SEEN, seen)


# --- 시나리오 ---------------------------------------------------------

func _test_scenario_roundtrip_and_trace_parity() -> void:
	print("[A] `.tres` 왕복 후 in-memory와 재로드 set이 동일 결과·trace")
	var cs := _make_scenario()
	var save_err := ResourceSaver.save(cs, TMP_PATH)
	_check("A.save_ok", save_err, OK)
	var loaded: ConditionSet = ResourceLoader.load(TMP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	_check_true("A.loaded_not_null", loaded != null)
	if loaded == null:
		return

	# 여러 상태에서 in-memory 결과와 재로드 결과가 완전히 같아야 한다(passed/valid/read_count/trace/errors).
	var states := [
		[0, 0, false],
		[5, 2, false],
		[5, 20, true],
		[5, 2, true],
	]
	for i in states.size():
		var st: Array = states[i]
		var store := _make_store()
		_set_state(store, st[0], st[1], st[2])
		var r_mem := ConditionEvaluator.evaluate(cs, store)
		var r_load := ConditionEvaluator.evaluate(loaded, store)
		# 전체 report(passed/valid/errors/trace/read_count) 문자열 표현이 동일해야 한다.
		_check("A[%d].report_parity" % i, str(r_mem), str(r_load))
		_check("A[%d].valid" % i, r_mem["valid"], true)
		store.free()


func _test_lifecycle_states() -> void:
	print("[B] lifecycle 상태별 결과·trace 정확성(재로드 set)")
	var loaded: ConditionSet = ResourceLoader.load(TMP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)

	# default: stage0/aff0/seen false -> stage>=3 false -> ALL false. read_count 3.
	var s0 := _make_store()
	var r0 := ConditionEvaluator.evaluate(loaded, s0)
	_check("B.default_passed", r0["passed"], false)
	_check("B.default_valid", r0["valid"], true)
	_check("B.default_readcount", r0["read_count"], 3)
	# trace: ALL[ stage(false) , ANY[ aff(false) , NOT[ seen(false)->NOT true ] ]->true ]
	var t: Dictionary = r0["trace"]
	_check("B.root_logic", t["logic"], "all")
	_check("B.stage_actual", t["children"][0]["actual"], 0)
	_check("B.stage_passed", t["children"][0]["passed"], false)
	_check("B.not_leaf_path", t["children"][1]["children"][1]["children"][0]["path"], [1, 1, 0])
	_check("B.any_passed", t["children"][1]["passed"], true)
	s0.free()

	# gate open: stage5/aff2/seen false -> ALL[true, ANY[false, NOT(false)->true]->true] -> true
	var s1 := _make_store()
	_set_state(s1, 5, 2, false)
	_check("B.open_passed", ConditionEvaluator.evaluate(loaded, s1)["passed"], true)
	s1.free()

	# affinity path: stage5/aff20/seen true -> ALL[true, ANY[true, NOT(true)->false]->true] -> true
	var s2 := _make_store()
	_set_state(s2, 5, 20, true)
	_check("B.affinity_passed", ConditionEvaluator.evaluate(loaded, s2)["passed"], true)
	s2.free()

	# closed: stage5/aff2/seen true -> ALL[true, ANY[false, NOT(true)->false]->false] -> false
	var s3 := _make_store()
	_set_state(s3, 5, 2, true)
	_check("B.closed_passed", ConditionEvaluator.evaluate(loaded, s3)["passed"], false)
	s3.free()


func _test_snapshot_restore_e2e() -> void:
	print("[C] load lifecycle(restore_world_state): SAVE 복원 + SESSION default 재평가")
	# 실제 load lifecycle은 coordinator(restore_world_state)로 검증한다. 직접 import_snapshot은
	# SAVE만 복원하고 SESSION을 유지하므로 SESSION reset을 증명하지 못한다. autoload Store/Runtime을 쓴다.
	var loaded: ConditionSet = ResourceLoader.load(TMP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var store: WorldStateStore = WorldState

	# gate가 SESSION에 의존하도록 affinity는 낮게(2) 둔다: 통과는 NOT(seen==true)에만 달려 있다.
	store.set_value(STAGE, 5)
	store.set_value(AFFINITY, 2)
	store.set_value(SEEN, true)
	var snap := WorldStateRuntime.capture_world_state()  # SAVE-only (stage/affinity), SEEN 제외
	# 상태를 모두 망가뜨린다(SEEN은 여전히 true로 둔다).
	store.set_value(STAGE, 0)
	store.set_value(AFFINITY, 0)
	store.set_value(SEEN, true)

	var report := WorldStateRuntime.restore_world_state(snap)
	_check("C.restore_ok", report["ok"], true)
	# SESSION 직접 단언: load lifecycle은 initialize(default)를 거치므로 SEEN은 false로 리셋된다.
	_check("C.session_seen_reset_false", store.read_state(SEEN), false)
	# SAVE 값은 snapshot에서 복원된다.
	_check("C.save_stage_restored", store.read_state(STAGE), 5)
	_check("C.save_affinity_restored", store.read_state(AFFINITY), 2)
	# gate 평가: stage5>=3 true; affinity2>=10 false; NOT(seen==true)=NOT(false)=true -> ANY true -> ALL true.
	# 이 통과는 SESSION이 false로 리셋된 것에 의존한다(affinity가 낮아 affinity 가지로는 통과 불가).
	_check("C.gate_open_via_session", ConditionEvaluator.evaluate(loaded, store)["passed"], true)
	# 음성 대조: 같은 SAVE 상태에서 SEEN만 true로 되돌리면 gate가 닫힌다 -> 결과가 SESSION에 실제로 의존함.
	store.set_value(SEEN, true)
	_check("C.gate_closed_when_seen_true", ConditionEvaluator.evaluate(loaded, store)["passed"], false)
	# autoload Store/Runtime은 free하지 않는다(트리 소유).


func _test_performance_sanity() -> void:
	print("[D] 성능 sanity: 최대 node(4096) + 같은 key read 1회")
	var store := _make_store()
	store.set_value(STAGE, 0)
	# 1 group + 4095 leaf(모두 같은 key STAGE == 0). node 4096(허용 경계). unique key 1.
	var kids: Array = []
	for i in 4095:
		kids.append(_state(STAGE, OP.EQUAL, 0))
	var r := ConditionEvaluator.evaluate(_set_root(_group(LG.ALL, kids)), store)
	_check("D.valid", r["valid"], true)
	_check("D.passed", r["passed"], true)        # 모든 leaf 0==0 -> ALL true
	_check("D.read_count", r["read_count"], 1)   # 같은 key는 1회만 read

	# node 초과(4097)는 structural reject -> read 0, Store 미접촉.
	var kids2: Array = []
	for i in 4096:
		kids2.append(_state(STAGE, OP.EQUAL, 0))
	var r2 := ConditionEvaluator.evaluate(_set_root(_group(LG.ALL, kids2)), store)
	_check("D.over_valid", r2["valid"], false)
	_check("D.over_read", r2["read_count"], 0)
	_check_true("D.over_code", _has_code(r2, "node_limit_exceeded"))
	store.free()


func _set_root(root: ConditionClause) -> ConditionSet:
	var cs := ConditionSet.new()
	cs.root = root
	return cs


func _test_fail_closed_store_unchanged() -> void:
	print("[E] fail-closed + Store/값 불변(pure read)")
	var store := _make_store()
	_set_state(store, 4, 7, false)
	_signal_count = 0
	store.value_changed.connect(_on_value_changed)

	# 미등록 key -> state_missing
	var rm := ConditionEvaluator.evaluate(_set_root(_state(&"nope.nope", OP.EQUAL, 1)), store)
	_check("E.missing_valid", rm["valid"], false)
	_check_true("E.missing_code", _has_code(rm, "state_missing"))
	# 타입 불일치 -> actual_type_mismatch
	var rt := ConditionEvaluator.evaluate(_set_root(_state(STAGE, OP.EQUAL, "4")), store)
	_check_true("E.mismatch_code", _has_code(rt, "actual_type_mismatch"))
	# malformed(structural) -> read 0
	var rs := ConditionEvaluator.evaluate(_set_root(_group(LG.NOT, [])), store)
	_check("E.struct_read0", rs["read_count"], 0)
	_check_true("E.struct_code", _has_code(rs, "not_arity_invalid"))

	# 모든 평가 동안 Store 값과 signal이 불변이어야 한다.
	_check("E.no_signal", _signal_count, 0)
	_check("E.stage_unchanged", store.read_state(STAGE), 4)
	_check("E.affinity_unchanged", store.read_state(AFFINITY), 7)
	store.value_changed.disconnect(_on_value_changed)
	store.free()


func _on_value_changed(_key: StringName, _old: Variant, _new: Variant) -> void:
	_signal_count += 1


func _cleanup() -> void:
	if FileAccess.file_exists(TMP_PATH):
		var err := DirAccess.remove_absolute(ProjectSettings.globalize_path(TMP_PATH))
		_check("cleanup.removed", err, OK)
