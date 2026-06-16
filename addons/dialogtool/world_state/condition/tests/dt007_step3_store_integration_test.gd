# DT-007 Step 3 검증용 헤드리스 테스트(WorldState Provider Integration).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/world_state/condition/tests/dt007_step3_store_integration_test.tscn
#
# 목표: 실제 WorldStateStore를 read provider로 주입해 저장 상태 변화가 조건 결과에 즉시 반영됨을 확인한다.
#
# 검증 범위:
# - 실제 Store가 evaluator의 provider 계약을 만족해 read facade(has_state/read_state)만으로 동작
# - bootstrap schema의 INT/FLOAT/STRING/STRING_NAME/BOOL 평가
# - set_value / apply_batch / reset_value / reset_lifetime(SESSION) / export+import_snapshot 뒤 재평가
# - read-only key와 SESSION key가 평가(read) 의미에 불필요하게 결합되지 않음(둘 다 정상 read)
# - evaluator가 Store를 변경하지 않음(value_changed 미발행, 값 불변) — pure read
# - 미등록 key -> state_missing, 타입 불일치 -> actual_type_mismatch(fail-closed)
# - 같은 key 반복 시 read_count는 unique key 수
#
# 제외: /root 직접 조회, Dialogue node/editor UI, mutation provider.
extends Node

const OP := StateCondition.Operator
const LG := ConditionGroup.Logic
const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const SCHEMA_PATH := "res://addons/dialogtool/examples/world_state_schema_example.tres"

var _failures: int = 0
var _signal_count: int = 0


func _ready() -> void:
	_test_store_accepted_and_defaults()
	_test_five_type_evaluation()
	_test_set_value_reeval()
	_test_apply_batch_reeval()
	_test_reset_value_reeval()
	_test_reset_lifetime_session_reeval()
	_test_snapshot_restore_reeval()
	_test_readonly_and_session_readable()
	_test_pure_read_no_mutation()
	_test_missing_and_type_mismatch_fail_closed()
	_test_read_count_unique_keys()

	if _failures == 0:
		print("[DT-007 Step3] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-007 Step3] FAILED: %d assertion(s)" % _failures)
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


func _codes(r: Dictionary) -> Array:
	var out: Array = []
	for e in r.get("errors", []):
		out.append(e["code"])
	return out


func _has_code(r: Dictionary, code: String) -> bool:
	return _codes(r).has(code)


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


func _cset(root: ConditionClause) -> ConditionSet:
	var cs := ConditionSet.new()
	cs.root = root
	return cs


# 실제 bootstrap schema로 ready 상태의 Store를 만든다(WorldStateStore.new() + schema 주입 + initialize).
func _make_store() -> WorldStateStore:
	var schema: StateSchema = ResourceLoader.load(SCHEMA_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	var store := WorldStateStore.new()
	store.schema = schema
	store.initialize()
	return store


func _eval(store: WorldStateStore, root: ConditionClause) -> Dictionary:
	return ConditionEvaluator.evaluate(_cset(root), store)


# --- 시나리오 ---------------------------------------------------------

func _test_store_accepted_and_defaults() -> void:
	print("[A] 실제 Store가 provider 계약을 만족하고 default를 읽음")
	var store := _make_store()
	_check("A.ready", store.is_store_ready(), true)
	# quest.main.stage default 0 == 0 -> true. provider 오류가 없어야 한다(Store가 계약 만족).
	var r := _eval(store, _state(&"quest.main.stage", OP.EQUAL, 0))
	_check("A.valid", r["valid"], true)
	_check("A.passed", r["passed"], true)
	_check_true("A.no_provider_err", not _has_code(r, "provider_missing") and not _has_code(r, "provider_contract_invalid"))
	store.free()


func _test_five_type_evaluation() -> void:
	print("[B] bootstrap 다섯 타입 평가")
	var store := _make_store()
	# INT, FLOAT, STRING, STRING_NAME, BOOL을 한 ALL 트리에서 default 값과 비교.
	var root := _group(LG.ALL, [
		_state(&"quest.main.stage", OP.EQUAL, 0),          # INT
		_state(&"player.health", OP.EQUAL, 100.0),         # FLOAT
		_state(&"player.display_name", OP.EQUAL, ""),      # STRING
		_state(&"world.build.channel", OP.EQUAL, &"dev"),  # STRING_NAME (read-only)
		_state(&"session.intro.seen", OP.EQUAL, false),    # BOOL (SESSION)
	])
	var r := _eval(store, root)
	_check("B.valid", r["valid"], true)
	_check("B.passed", r["passed"], true)
	_check("B.read_count", r["read_count"], 5)
	# FLOAT ordered도 동작
	_check("B.health_ge", _eval(store, _state(&"player.health", OP.GREATER_EQUAL, 100.0))["passed"], true)
	store.free()


func _test_set_value_reeval() -> void:
	print("[C] set_value 후 재평가가 새 값을 반영")
	var store := _make_store()
	var cond := _state(&"quest.main.stage", OP.GREATER_EQUAL, 3)
	_check("C.before", _eval(store, cond)["passed"], false)   # default 0
	var err := store.set_value(&"quest.main.stage", 5)
	_check("C.set_ok", err, OK)
	_check("C.after", _eval(store, cond)["passed"], true)     # 이제 5 >= 3
	store.free()


func _test_apply_batch_reeval() -> void:
	print("[D] apply_batch 후 재평가")
	var store := _make_store()
	var changes: Array[Dictionary] = [
		{"key": &"actor.example.affinity", "value": 10},
		{"key": &"player.health", "value": 40.0},
	]
	var report := store.apply_batch(changes)
	_check("D.applied", report["applied"], true)
	_check("D.diff_count", report["diff"].size(), 2)
	# ANY[ affinity>=10 , health<=30 ] -> affinity 가지 true
	var root := _group(LG.ANY, [
		_state(&"actor.example.affinity", OP.GREATER_EQUAL, 10),
		_state(&"player.health", OP.LESS_EQUAL, 30.0),
	])
	_check("D.passed", _eval(store, root)["passed"], true)
	store.free()


func _test_reset_value_reeval() -> void:
	print("[E] reset_value 후 재평가가 default로 복귀")
	var store := _make_store()
	var cond := _state(&"quest.main.stage", OP.GREATER_EQUAL, 3)
	store.set_value(&"quest.main.stage", 5)
	_check("E.set_then", _eval(store, cond)["passed"], true)
	var err := store.reset_value(&"quest.main.stage")
	_check("E.reset_ok", err, OK)
	_check("E.after_reset", _eval(store, cond)["passed"], false)  # 0으로 복귀
	store.free()


func _test_reset_lifetime_session_reeval() -> void:
	print("[F] reset_lifetime(SESSION) 후 재평가")
	var store := _make_store()
	var cond := _state(&"session.intro.seen", OP.EQUAL, true)
	store.set_value(&"session.intro.seen", true)
	_check("F.set_then", _eval(store, cond)["passed"], true)
	store.reset_lifetime(LT.SESSION)
	_check("F.after_reset", _eval(store, cond)["passed"], false)  # default false로 복귀
	store.free()


func _test_snapshot_restore_reeval() -> void:
	print("[G] export -> 변경 -> import_snapshot 복원 후 재평가")
	var store := _make_store()
	var cond := _state(&"quest.main.stage", OP.EQUAL, 7)
	store.set_value(&"quest.main.stage", 7)
	var snap := store.export_snapshot(LT.SAVE)
	store.set_value(&"quest.main.stage", 1)
	_check("G.after_change", _eval(store, cond)["passed"], false)  # 1 != 7
	var report := store.import_snapshot(snap)
	_check_true("G.import_ok", report.get("errors", []).is_empty())
	_check("G.after_restore", _eval(store, cond)["passed"], true)  # 7로 복원
	store.free()


func _test_readonly_and_session_readable() -> void:
	print("[H] read-only/SESSION key가 평가에서 정상 read됨")
	var store := _make_store()
	# read-only world.build.channel을 조건에서 읽을 수 있다(read-only는 write만 제한).
	var ro := _eval(store, _state(&"world.build.channel", OP.EQUAL, &"dev"))
	_check("H.readonly_valid", ro["valid"], true)
	_check("H.readonly_passed", ro["passed"], true)
	_check("H.readonly_read", ro["read_count"], 1)
	# SESSION key도 동일하게 read된다(lifetime이 read 의미에 결합되지 않음).
	var se := _eval(store, _state(&"session.intro.seen", OP.EQUAL, false))
	_check("H.session_valid", se["valid"], true)
	_check("H.session_passed", se["passed"], true)
	store.free()


func _test_pure_read_no_mutation() -> void:
	print("[I] evaluator는 Store를 변경하지 않음(pure read)")
	var store := _make_store()
	_signal_count = 0
	store.value_changed.connect(_on_value_changed)
	store.set_value(&"quest.main.stage", 4)
	_signal_count = 0  # set으로 인한 signal은 제외하고 evaluate 동안만 센다
	var before: Variant = store.read_state(&"quest.main.stage")
	var root := _group(LG.ALL, [
		_state(&"quest.main.stage", OP.GREATER_EQUAL, 3),
		_state(&"quest.main.stage", OP.LESS, 100),
	])
	_eval(store, root)
	_check("I.no_signal_during_eval", _signal_count, 0)
	_check("I.value_unchanged", store.read_state(&"quest.main.stage"), before)
	store.value_changed.disconnect(_on_value_changed)
	store.free()


func _on_value_changed(_key: StringName, _old: Variant, _new: Variant) -> void:
	_signal_count += 1


func _test_missing_and_type_mismatch_fail_closed() -> void:
	print("[J] 미등록 key / 타입 불일치는 실제 Store에서도 fail-closed")
	var store := _make_store()
	# 미등록 key: store.has_state == false -> state_missing
	var rm := _eval(store, _state(&"nope.nope", OP.EQUAL, 1))
	_check("J.missing_valid", rm["valid"], false)
	_check("J.missing_passed", rm["passed"], false)
	_check_true("J.missing_code", _has_code(rm, "state_missing"))
	# 타입 불일치: quest.main.stage는 INT, expected String "0" -> actual_type_mismatch
	var rt := _eval(store, _state(&"quest.main.stage", OP.EQUAL, "0"))
	_check("J.mismatch_valid", rt["valid"], false)
	_check_true("J.mismatch_code", _has_code(rt, "actual_type_mismatch"))
	store.free()


func _test_read_count_unique_keys() -> void:
	print("[K] read_count는 unique key 수(같은 key 반복은 1회)")
	var store := _make_store()
	store.set_value(&"quest.main.stage", 5)
	# 같은 key 3회 + 다른 key 1회 -> unique 2
	var root := _group(LG.ALL, [
		_state(&"quest.main.stage", OP.GREATER_EQUAL, 1),
		_state(&"quest.main.stage", OP.LESS, 100),
		_state(&"quest.main.stage", OP.EQUAL, 5),
		_state(&"player.health", OP.EQUAL, 100.0),
	])
	var r := _eval(store, root)
	_check("K.passed", r["passed"], true)
	_check("K.read_count", r["read_count"], 2)
	store.free()
