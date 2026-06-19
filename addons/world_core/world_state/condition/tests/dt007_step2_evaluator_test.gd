# DT-007 Step 2 검증용 헤드리스 테스트(Pure Read ConditionEvaluator).
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/world_core/world_state/condition/tests/dt007_step2_evaluator_test.tscn
#
# 검증 범위(Verification Matrix의 Step 2 행):
# - 타입: 다섯 state 타입 equality true/false, numeric ordering 경계, strict actual_type_mismatch
# - 논리: ALL/ANY/NOT truth table, 중첩, child 순서
# - provider: null(provider_missing), 계약 method 누락(provider_contract_invalid), missing key,
#             call count(같은 key 1회 read), changing fake provider
# - trace: 성공/실패 leaf, group 결과, path, actual/expected/operator, 전체 평가 순서
# - fail-closed: NOT(missing) 미통과, ANY(true, errored) 미통과, errored child를 pass로 변환 안 함
# - 오류 분류: structural reject 시 read_count==0(provider 미접촉), EQUAL String-vs-INT 불일치,
#             FLOAT state vs int literal 불일치, 반복 missing key probe 1회
# - 불변성: 반환 report 변조 후 재평가 불변, mutation method 미호출
extends Node

const OP := StateCondition.Operator
const LG := ConditionGroup.Logic

var _failures: int = 0


# read provider 계약을 구현하고 호출 횟수를 기록하는 fake. mutation method는 호출되면 안 된다.
class FakeProvider extends RefCounted:
	var data: Dictionary = {}
	var has_calls: int = 0
	var read_calls: int = 0
	var mutation_calls: int = 0

	func has_state(key: StringName) -> bool:
		has_calls += 1
		return data.has(key)

	func read_state(key: StringName) -> Variant:
		read_calls += 1
		return data.get(key)

	func try_read_state(key: StringName, fallback: Variant = null) -> Variant:
		return data.get(key, fallback)

	func set_state(key: StringName, value: Variant) -> int:
		mutation_calls += 1
		data[key] = value
		return OK

	func apply_state_batch(_changes: Array) -> Dictionary:
		mutation_calls += 1
		return {}


# read_state가 없어 read 계약을 만족하지 않는 provider.
class BadProvider extends RefCounted:
	func has_state(_key: StringName) -> bool:
		return false


# has_state가 인자를 받지 않는다(arity 위반). 평가 중 호출하면 "expected 0 arguments"로 실패해야 하므로
# 사전 검사에서 막혀야 한다.
class ZeroArgProvider extends RefCounted:
	func has_state() -> bool:
		return false

	func read_state(_key: StringName) -> Variant:
		return null


# has_state가 bool이 아닌 타입을 선언한다(반환 타입 위반).
class IntReturnProvider extends RefCounted:
	func has_state(_key: StringName) -> int:
		return 0

	func read_state(_key: StringName) -> Variant:
		return null


# 첫 인자를 int로 선언했다. StringName key를 넘기면 호출 시 SCRIPT ERROR가 나므로 사전 검사에서 막혀야 한다.
class IntArgProvider extends RefCounted:
	func has_state(_key: int) -> bool:
		return false

	func read_state(_key: int) -> Variant:
		return null


# 미선언(Variant) 반환에 실제로는 non-bool(int 1)을 돌려준다. 정적으로는 통과하지만 런타임 타입 검사로
# provider_contract_invalid가 되어야 한다(truthy로 새지 않음).
class UntypedNonBoolReturnProvider extends RefCounted:
	func has_state(_key):
		return 1

	func read_state(_key):
		return null


# 미선언(Variant) 시그니처지만 실제로 bool을 올바르게 반환한다(미선언 provider 정상 동작 보장).
class UntypedGoodProvider extends RefCounted:
	var data: Dictionary = {}

	func has_state(key):
		return data.has(key)

	func read_state(key):
		return data.get(key)


func _ready() -> void:
	_test_equality_types()
	_test_numeric_ordering()
	_test_strict_type_mismatch()
	_test_truth_tables()
	_test_nested_and_order()
	_test_provider_null()
	_test_provider_contract_invalid()
	_test_provider_non_object()
	_test_provider_bad_arity()
	_test_provider_bad_return()
	_test_provider_bad_arg_type()
	_test_provider_untyped_nonbool_return()
	_test_provider_untyped_bool_ok()
	_test_state_missing()
	_test_read_cache_count()
	_test_repeated_missing_probe_once()
	_test_changing_provider()
	_test_fail_closed_not_missing()
	_test_fail_closed_any_true_errored()
	_test_structural_reject_read_count0()
	_test_mutation_never_called()
	_test_report_immutability()
	_test_trace_fields()

	if _failures == 0:
		print("[DT-007 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-007 Step2] FAILED: %d assertion(s)" % _failures)
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
	return r.get("error_codes_cache", _codes(r)).has(code)


func _codes(r: Dictionary) -> Array:
	var out: Array = []
	for e in r.get("errors", []):
		out.append(e["code"])
	return out


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


func _provider(data: Dictionary) -> FakeProvider:
	var p := FakeProvider.new()
	p.data = data.duplicate()
	return p


# 단일 leaf를 주어진 provider data로 평가한 결과.
func _eval_leaf(key: StringName, op: int, expected: Variant, data: Dictionary) -> Dictionary:
	return ConditionEvaluator.evaluate(_cset(_state(key, op, expected)), _provider(data))


# --- 시나리오 ---------------------------------------------------------

func _test_equality_types() -> void:
	print("[A] 다섯 state 타입 equality true/false")
	# bool
	_check("A.bool_eq_true", _eval_leaf(&"a.b", OP.EQUAL, true, {&"a.b": true})["passed"], true)
	_check("A.bool_eq_false", _eval_leaf(&"a.b", OP.EQUAL, true, {&"a.b": false})["passed"], false)
	_check("A.bool_ne_true", _eval_leaf(&"a.b", OP.NOT_EQUAL, true, {&"a.b": false})["passed"], true)
	# int
	_check("A.int_eq_true", _eval_leaf(&"a.b", OP.EQUAL, 7, {&"a.b": 7})["passed"], true)
	_check("A.int_eq_false", _eval_leaf(&"a.b", OP.EQUAL, 7, {&"a.b": 8})["passed"], false)
	# float
	_check("A.float_eq_true", _eval_leaf(&"a.b", OP.EQUAL, 1.5, {&"a.b": 1.5})["passed"], true)
	# String
	_check("A.string_eq_true", _eval_leaf(&"a.b", OP.EQUAL, "town", {&"a.b": "town"})["passed"], true)
	_check("A.string_eq_false", _eval_leaf(&"a.b", OP.EQUAL, "town", {&"a.b": "city"})["passed"], false)
	# StringName
	_check("A.sn_eq_true", _eval_leaf(&"a.b", OP.EQUAL, &"calm", {&"a.b": &"calm"})["passed"], true)
	_check("A.sn_ne_true", _eval_leaf(&"a.b", OP.NOT_EQUAL, &"calm", {&"a.b": &"angry"})["passed"], true)
	# 모두 valid해야 한다(오류 없음)
	_check("A.valid", _eval_leaf(&"a.b", OP.EQUAL, 7, {&"a.b": 7})["valid"], true)


func _test_numeric_ordering() -> void:
	print("[B] numeric ordering 경계(int/float)")
	# int actual 5
	_check("B.less_eq5", _eval_leaf(&"a.b", OP.LESS, 5, {&"a.b": 5})["passed"], false)
	_check("B.less_6", _eval_leaf(&"a.b", OP.LESS, 6, {&"a.b": 5})["passed"], true)
	_check("B.le_5", _eval_leaf(&"a.b", OP.LESS_EQUAL, 5, {&"a.b": 5})["passed"], true)
	_check("B.le_4", _eval_leaf(&"a.b", OP.LESS_EQUAL, 4, {&"a.b": 5})["passed"], false)
	_check("B.gt_5", _eval_leaf(&"a.b", OP.GREATER, 5, {&"a.b": 5})["passed"], false)
	_check("B.gt_4", _eval_leaf(&"a.b", OP.GREATER, 4, {&"a.b": 5})["passed"], true)
	_check("B.ge_5", _eval_leaf(&"a.b", OP.GREATER_EQUAL, 5, {&"a.b": 5})["passed"], true)
	_check("B.ge_6", _eval_leaf(&"a.b", OP.GREATER_EQUAL, 6, {&"a.b": 5})["passed"], false)
	# float actual 2.5
	_check("B.f_gt_2", _eval_leaf(&"a.b", OP.GREATER, 2.0, {&"a.b": 2.5})["passed"], true)
	_check("B.f_le_2_5", _eval_leaf(&"a.b", OP.LESS_EQUAL, 2.5, {&"a.b": 2.5})["passed"], true)


func _test_strict_type_mismatch() -> void:
	print("[C] strict actual_type_mismatch")
	# EQUAL: expected String "7", actual INT 7 -> 불일치
	var r1 := _eval_leaf(&"a.b", OP.EQUAL, "7", {&"a.b": 7})
	_check("C.str_vs_int.valid", r1["valid"], false)
	_check("C.str_vs_int.passed", r1["passed"], false)
	_check_true("C.str_vs_int.code", _has_code(r1, "actual_type_mismatch"))
	_check("C.str_vs_int.read_count", r1["read_count"], 1)
	# FLOAT state vs int literal: expected 1(int), actual 1.0(float) -> 불일치(암시적 변환 없음)
	var r2 := _eval_leaf(&"a.b", OP.EQUAL, 1, {&"a.b": 1.0})
	_check("C.float_vs_int.valid", r2["valid"], false)
	_check_true("C.float_vs_int.code", _has_code(r2, "actual_type_mismatch"))
	# 메시지에 두 typeof 코드(int=2, float=3)가 드러난다
	_check_true("C.float_vs_int.msg", str(r2["errors"][0]["message"]).contains("2") and str(r2["errors"][0]["message"]).contains("3"))
	# bool vs int strict 구분
	var r3 := _eval_leaf(&"a.b", OP.EQUAL, 1, {&"a.b": true})
	_check_true("C.bool_vs_int.code", _has_code(r3, "actual_type_mismatch"))
	# StringName vs String strict 구분
	var r4 := _eval_leaf(&"a.b", OP.EQUAL, "calm", {&"a.b": &"calm"})
	_check_true("C.sn_vs_str.code", _has_code(r4, "actual_type_mismatch"))


func _test_truth_tables() -> void:
	print("[D] ALL/ANY/NOT truth table")
	var data := {&"a.t": true, &"a.f": false}
	# leaf_true: a.t == true (passes), leaf_false: a.f == true (fails)
	var lt := func(): return _state(&"a.t", OP.EQUAL, true)
	var lf := func(): return _state(&"a.f", OP.EQUAL, true)
	# ALL
	_check("D.all_TT", ConditionEvaluator.evaluate(_cset(_group(LG.ALL, [lt.call(), lt.call()])), _provider(data))["passed"], true)
	_check("D.all_TF", ConditionEvaluator.evaluate(_cset(_group(LG.ALL, [lt.call(), lf.call()])), _provider(data))["passed"], false)
	# ANY
	_check("D.any_FF", ConditionEvaluator.evaluate(_cset(_group(LG.ANY, [lf.call(), lf.call()])), _provider(data))["passed"], false)
	_check("D.any_FT", ConditionEvaluator.evaluate(_cset(_group(LG.ANY, [lf.call(), lt.call()])), _provider(data))["passed"], true)
	# NOT
	_check("D.not_T", ConditionEvaluator.evaluate(_cset(_group(LG.NOT, [lt.call()])), _provider(data))["passed"], false)
	_check("D.not_F", ConditionEvaluator.evaluate(_cset(_group(LG.NOT, [lf.call()])), _provider(data))["passed"], true)


func _test_nested_and_order() -> void:
	print("[E] 중첩 + child 순서 trace")
	# ALL[ quest>=3 , ANY[ aff>=10 , NOT[ seen==true ] ] ]
	var data := {&"quest.main.stage": 5, &"actor.example.affinity": 2, &"session.intro.seen": false}
	var root := _group(LG.ALL, [
		_state(&"quest.main.stage", OP.GREATER_EQUAL, 3),
		_group(LG.ANY, [
			_state(&"actor.example.affinity", OP.GREATER_EQUAL, 10),
			_group(LG.NOT, [_state(&"session.intro.seen", OP.EQUAL, true)]),
		]),
	])
	var r := ConditionEvaluator.evaluate(_cset(root), _provider(data))
	# quest>=3 true; aff>=10 false; NOT(seen==true): seen=false so leaf false, NOT -> true; ANY -> true; ALL -> true
	_check("E.passed", r["passed"], true)
	_check("E.valid", r["valid"], true)
	# trace 구조/순서
	var t: Dictionary = r["trace"]
	_check("E.root_kind", t["kind"], "group")
	_check("E.root_logic", t["logic"], "all")
	_check("E.root_path", t["path"], [])
	_check("E.child0_key", t["children"][0]["key"], &"quest.main.stage")
	_check("E.child0_op", t["children"][0]["operator"], "greater_equal")
	_check("E.child0_path", t["children"][0]["path"], [0])
	_check("E.child0_actual", t["children"][0]["actual"], 5)
	_check("E.child0_expected", t["children"][0]["expected"], 3)
	_check("E.any_logic", t["children"][1]["logic"], "any")
	_check("E.not_logic", t["children"][1]["children"][1]["logic"], "not")
	_check("E.not_leaf_path", t["children"][1]["children"][1]["children"][0]["path"], [1, 1, 0])


func _test_provider_null() -> void:
	print("[F] provider null -> provider_missing, read 0")
	var r := ConditionEvaluator.evaluate(_cset(_state(&"a.b", OP.EQUAL, 1)), null)
	_check("F.valid", r["valid"], false)
	_check("F.passed", r["passed"], false)
	_check("F.read_count", r["read_count"], 0)
	_check_true("F.code", _has_code(r, "provider_missing"))
	# trace는 여전히 만들어지되 leaf는 errored
	_check("F.leaf_error", r["trace"]["error"], "provider_missing")
	_check("F.leaf_actual_null", r["trace"]["actual"], null)


func _test_provider_contract_invalid() -> void:
	print("[G] 계약 method 누락 -> provider_contract_invalid, read 0")
	var r := ConditionEvaluator.evaluate(_cset(_state(&"a.b", OP.EQUAL, 1)), BadProvider.new())
	_check("G.valid", r["valid"], false)
	_check("G.read_count", r["read_count"], 0)
	_check_true("G.code", _has_code(r, "provider_contract_invalid"))


func _test_provider_non_object() -> void:
	print("[G2] 비-Object provider -> provider_contract_invalid (SCRIPT ERROR 없음)")
	var bads: Array = [42, 3.14, "x", [1, 2], {"a": 1}, true]
	for b in bads:
		var r := ConditionEvaluator.evaluate(_cset(_state(&"a.b", OP.EQUAL, 1)), b)
		_check("G2.valid[%s]" % typeof(b), r["valid"], false)
		_check("G2.read[%s]" % typeof(b), r["read_count"], 0)
		_check_true("G2.code[%s]" % typeof(b), _has_code(r, "provider_contract_invalid"))


func _test_provider_bad_arity() -> void:
	print("[G3] has_state arity 위반 -> provider_contract_invalid (호출 전 차단)")
	var r := ConditionEvaluator.evaluate(_cset(_state(&"a.b", OP.EQUAL, 1)), ZeroArgProvider.new())
	_check("G3.valid", r["valid"], false)
	_check("G3.passed", r["passed"], false)
	_check("G3.read_count", r["read_count"], 0)
	_check_true("G3.code", _has_code(r, "provider_contract_invalid"))


func _test_provider_bad_return() -> void:
	print("[G4] has_state 반환 타입 위반(-> int) -> provider_contract_invalid")
	var r := ConditionEvaluator.evaluate(_cset(_state(&"a.b", OP.EQUAL, 1)), IntReturnProvider.new())
	_check("G4.valid", r["valid"], false)
	_check("G4.read_count", r["read_count"], 0)
	_check_true("G4.code", _has_code(r, "provider_contract_invalid"))


func _test_provider_bad_arg_type() -> void:
	print("[G5] has_state(key:int) arg 타입 위반 -> provider_contract_invalid (호출 전 차단, SCRIPT ERROR 없음)")
	var r := ConditionEvaluator.evaluate(_cset(_state(&"a.b", OP.EQUAL, 1)), IntArgProvider.new())
	_check("G5.valid", r["valid"], false)
	_check("G5.passed", r["passed"], false)
	_check("G5.read_count", r["read_count"], 0)
	_check_true("G5.code", _has_code(r, "provider_contract_invalid"))


func _test_provider_untyped_nonbool_return() -> void:
	print("[G6] 미선언 has_state가 런타임에 non-bool 반환 -> provider_contract_invalid (truthy로 안 샘)")
	var r := ConditionEvaluator.evaluate(_cset(_state(&"a.b", OP.EQUAL, 1)), UntypedNonBoolReturnProvider.new())
	_check("G6.valid", r["valid"], false)
	_check("G6.passed", r["passed"], false)        # 1이 true로 암시 변환되지 않아야 한다
	_check_true("G6.code", _has_code(r, "provider_contract_invalid"))
	_check("G6.read_count", r["read_count"], 1)    # has_state를 호출했으므로 read 1회로 카운트
	_check("G6.leaf_error", r["trace"]["error"], "provider_contract_invalid")
	_check("G6.leaf_actual_null", r["trace"]["actual"], null)


func _test_provider_untyped_bool_ok() -> void:
	print("[G7] 미선언 시그니처지만 bool 정상 반환 provider는 동작")
	var p := UntypedGoodProvider.new()
	p.data = {&"a.b": 5}
	var r := ConditionEvaluator.evaluate(_cset(_state(&"a.b", OP.EQUAL, 5)), p)
	_check("G7.valid", r["valid"], true)
	_check("G7.passed", r["passed"], true)
	_check("G7.read_count", r["read_count"], 1)


func _test_state_missing() -> void:
	print("[H] missing key -> state_missing")
	var r := _eval_leaf(&"a.b", OP.EQUAL, 1, {}) # provider에 a.b 없음
	_check("H.valid", r["valid"], false)
	_check("H.passed", r["passed"], false)
	_check_true("H.code", _has_code(r, "state_missing"))
	_check("H.leaf_error", r["trace"]["error"], "state_missing")
	_check("H.leaf_actual_null", r["trace"]["actual"], null)
	_check("H.read_count", r["read_count"], 1) # miss도 read로 카운트


func _test_read_cache_count() -> void:
	print("[I] 같은 key는 1회만 read")
	# 같은 key를 3개 leaf에서 사용
	var p := _provider({&"a.b": 5})
	var root := _group(LG.ALL, [
		_state(&"a.b", OP.GREATER_EQUAL, 1),
		_state(&"a.b", OP.LESS, 100),
		_state(&"a.b", OP.EQUAL, 5),
	])
	var r := ConditionEvaluator.evaluate(_cset(root), p)
	_check("I.passed", r["passed"], true)
	_check("I.read_count", r["read_count"], 1)
	_check("I.has_calls", p.has_calls, 1)
	_check("I.read_calls", p.read_calls, 1)
	# 서로 다른 key 2개는 read_count 2
	var p2 := _provider({&"a.b": 5, &"a.c": 9})
	var r2 := ConditionEvaluator.evaluate(_cset(_group(LG.ALL, [
		_state(&"a.b", OP.EQUAL, 5), _state(&"a.c", OP.EQUAL, 9)])), p2)
	_check("I.two_keys_read", r2["read_count"], 2)
	_check("I.two_keys_has", p2.has_calls, 2)


func _test_repeated_missing_probe_once() -> void:
	print("[J] 반복 missing key는 probe 1회")
	var p := _provider({}) # 비어 있음
	var root := _group(LG.ALL, [
		_state(&"a.miss", OP.EQUAL, 1),
		_state(&"a.miss", OP.EQUAL, 2),
	])
	var r := ConditionEvaluator.evaluate(_cset(root), p)
	_check("J.has_calls", p.has_calls, 1) # miss cache로 두 번째는 probe 안 함
	_check("J.read_count", r["read_count"], 1)
	# 두 leaf 모두 errored이므로 state_missing 2건
	_check("J.error_count", _codes(r).count("state_missing"), 2)


func _test_changing_provider() -> void:
	print("[K] provider 값 변경이 재평가에 반영")
	var p := _provider({&"a.b": 1})
	var cs := _cset(_state(&"a.b", OP.EQUAL, 2))
	_check("K.before", ConditionEvaluator.evaluate(cs, p)["passed"], false)
	p.data[&"a.b"] = 2
	_check("K.after", ConditionEvaluator.evaluate(cs, p)["passed"], true)


func _test_fail_closed_not_missing() -> void:
	print("[L] NOT(missing leaf) 미통과")
	var r := ConditionEvaluator.evaluate(
		_cset(_group(LG.NOT, [_state(&"a.miss", OP.EQUAL, true)])), _provider({}))
	_check("L.passed", r["passed"], false)
	_check("L.valid", r["valid"], false)
	_check_true("L.code", _has_code(r, "state_missing"))
	# NOT group의 trace passed도 false(errored child를 pass로 바꾸지 않음)
	_check("L.group_passed", r["trace"]["passed"], false)


func _test_fail_closed_any_true_errored() -> void:
	print("[M] ANY(true, errored) 미통과")
	# a.t==true (passes) + a.miss (errored)
	var r := ConditionEvaluator.evaluate(_cset(_group(LG.ANY, [
		_state(&"a.t", OP.EQUAL, true),
		_state(&"a.miss", OP.EQUAL, 1),
	])), _provider({&"a.t": true}))
	_check("M.passed", r["passed"], false)        # 논리적으로 true여도 error로 fail-closed
	_check("M.valid", r["valid"], false)
	_check("M.group_passed", r["trace"]["passed"], false)
	# 정상 child의 leaf passed는 여전히 true로 기록된다(trace 정확성)
	_check("M.true_leaf", r["trace"]["children"][0]["passed"], true)


func _test_structural_reject_read_count0() -> void:
	print("[N] structural reject 시 read_count==0, provider 미접촉")
	var p := _provider({&"a.b": 1})
	# empty ALL group -> group_empty(구조 오류)
	var r := ConditionEvaluator.evaluate(_cset(_group(LG.ALL, [])), p)
	_check("N.valid", r["valid"], false)
	_check("N.passed", r["passed"], false)
	_check("N.read_count", r["read_count"], 0)
	_check_true("N.struct_code", _has_code(r, "group_empty"))
	_check("N.trace_empty", r["trace"], {})
	_check("N.provider_untouched", p.has_calls, 0) # 값 읽기 전에 거부


func _test_mutation_never_called() -> void:
	print("[O] mutation method는 호출되지 않음")
	var p := _provider({&"a.b": 5, &"a.c": &"x"})
	var root := _group(LG.ALL, [
		_state(&"a.b", OP.GREATER, 1),
		_state(&"a.c", OP.EQUAL, &"x"),
	])
	ConditionEvaluator.evaluate(_cset(root), p)
	_check("O.mutation_calls", p.mutation_calls, 0)


func _test_report_immutability() -> void:
	print("[P] 반환 report 변조 후 재평가 불변")
	var p := _provider({&"a.b": 5})
	var cs := _cset(_state(&"a.b", OP.EQUAL, 5))
	var r := ConditionEvaluator.evaluate(cs, p)
	_check("P.first", r["passed"], true)
	# 반환값을 마구 변조한다
	r["passed"] = false
	r["errors"].append({"code": "tampered"})
	r["trace"]["passed"] = false
	r["trace"]["key"] = &"hacked"
	# 재평가는 영향받지 않는다
	var r2 := ConditionEvaluator.evaluate(cs, p)
	_check("P.second_passed", r2["passed"], true)
	_check("P.second_key", r2["trace"]["key"], &"a.b")
	_check_true("P.no_tampered", not _has_code(r2, "tampered"))
	# Resource 자체도 불변
	_check("P.resource_key", (cs.root as StateCondition).key, &"a.b")


func _test_trace_fields() -> void:
	print("[Q] trace 필드 형태")
	# 성공 leaf
	var r := _eval_leaf(&"a.b", OP.GREATER_EQUAL, 3, {&"a.b": 5})
	var n: Dictionary = r["trace"]
	_check("Q.kind", n["kind"], "state")
	_check("Q.operator", n["operator"], "greater_equal")
	_check("Q.expected", n["expected"], 3)
	_check("Q.actual", n["actual"], 5)
	_check("Q.passed", n["passed"], true)
	_check_true("Q.no_error_field", not n.has("error"))
	# 실패(에러) leaf: actual null + error 필드
	var re := _eval_leaf(&"a.b", OP.EQUAL, 1, {})
	_check_true("Q.err_has_error", re["trace"].has("error"))
	_check("Q.err_actual_null", re["trace"]["actual"], null)
