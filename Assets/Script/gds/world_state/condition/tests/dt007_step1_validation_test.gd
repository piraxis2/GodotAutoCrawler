# DT-007 Step 1 검증용 헤드리스 테스트(Condition Resource Model and Validation).
# 실행:
#   godot --headless --path <project> --import   (새 class_name 캐시 생성)
#   godot --headless --path <project> res://Assets/Script/gds/world_state/condition/tests/dt007_step1_validation_test.tscn
#
# 검증 범위(Verification Matrix의 Step 1 행):
# - 구조: condition_set null, root null, null child, unknown clause, empty group, NOT 0/1/2 child,
#         self/indirect cycle, aliased(공유) clause
# - 한계: depth 경계(64)/초과(65), node 경계(4096)/초과(4097)
# - 타입: 다섯 state 타입 equality, numeric ordering, ordered 비숫자 거부, null/미지원 expected
# - 논리: nested ALL/ANY/NOT valid 통과, leaf-as-root
# - 오류: 구조화 {code,path,key,message}, path 정확성, non-short-circuit 다중 수집
# - 불변성: 반환 결과 변조가 다음 검증에 영향 없음(deep copy)
# - 저장: .tres 왕복에서 트리 순서/operator/expected typeof/StringName/metadata 보존 + 재검증
extends Node

const OP := StateCondition.Operator
const LG := ConditionGroup.Logic
const TMP_PATH := "user://dt007_step1_condition_set.tres"
const CLAUSE_SCRIPT := "res://Assets/Script/gds/world_state/condition/condition_clause.gd"

var _failures: int = 0


func _ready() -> void:
	_test_valid_nested_tree()
	_test_leaf_as_root()
	_test_condition_set_null()
	_test_root_null()
	_test_null_child()
	_test_unknown_clause()
	_test_empty_group()
	_test_not_arity()
	_test_self_cycle()
	_test_indirect_cycle()
	_test_aliased_clause()
	_test_depth_boundary()
	_test_depth_exceeded()
	_test_node_boundary()
	_test_node_exceeded()
	_test_key_errors()
	_test_operator_invalid()
	_test_logic_invalid()
	_test_expected_type_invalid()
	_test_ordered_type_rules()
	_test_equality_types_valid()
	_test_path_reporting()
	_test_multiple_errors_collected()
	_test_result_immutability()
	_test_roundtrip_save_reload()

	if _failures == 0:
		print("[DT-007 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-007 Step1] FAILED: %d assertion(s)" % _failures)
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


func _has_code(result: Dictionary, code: String) -> bool:
	return result.get("error_codes", []).has(code)


# code가 정확히 한 번, 주어진 path에서 보고됐는지.
func _error_at(result: Dictionary, code: String, path: Array) -> bool:
	for e in result.get("errors", []):
		if e["code"] == code and str(e["path"]) == str(path):
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


func _cset(root: ConditionClause) -> ConditionSet:
	var cs := ConditionSet.new()
	cs.root = root
	return cs


# --- 시나리오 ---------------------------------------------------------

# ALL
#   quest.main.stage >= 3            (int, ordered)
#   ANY
#     actor.example.affinity >= 10   (int, ordered)
#     NOT
#       session.intro.seen == true   (bool, equality)
#   actor.example.mood == &"calm"    (StringName, equality)
func _make_valid_tree() -> ConditionSet:
	var not_group := _group(LG.NOT, [_state(&"session.intro.seen", OP.EQUAL, true)])
	var any_group := _group(LG.ANY, [
		_state(&"actor.example.affinity", OP.GREATER_EQUAL, 10),
		not_group,
	])
	var root := _group(LG.ALL, [
		_state(&"quest.main.stage", OP.GREATER_EQUAL, 3),
		any_group,
		_state(&"actor.example.mood", OP.EQUAL, &"calm"),
	])
	return _cset(root)


func _test_valid_nested_tree() -> void:
	print("[A] nested ALL/ANY/NOT valid")
	var r := ConditionValidator.validate(_make_valid_tree())
	_check("A.valid", r["valid"], true)
	_check("A.error_count", r["errors"].size(), 0)
	_check("A.node_count", r["node_count"], 7)


func _test_leaf_as_root() -> void:
	print("[B] leaf(StateCondition) as root")
	var r := ConditionValidator.validate(_cset(_state(&"quest.main.stage", OP.EQUAL, 1)))
	_check("B.valid", r["valid"], true)
	_check("B.node_count", r["node_count"], 1)


func _test_condition_set_null() -> void:
	print("[C] condition_set == null")
	var r := ConditionValidator.validate(null)
	_check("C.valid", r["valid"], false)
	_check_true("C.code_at_root", _error_at(r, "condition_set_null", []))
	_check("C.node_count", r["node_count"], 0) # 어떤 노드도 방문하지 않음


func _test_root_null() -> void:
	print("[D] root == null")
	var r := ConditionValidator.validate(_cset(null))
	_check("D.valid", r["valid"], false)
	_check_true("D.code_at_root", _error_at(r, "root_null", []))
	_check("D.node_count", r["node_count"], 0)


func _test_null_child() -> void:
	print("[E] null child")
	var g := _group(LG.ALL, [_state(&"a.b", OP.EQUAL, 1), null])
	var r := ConditionValidator.validate(_cset(g))
	_check("E.valid", r["valid"], false)
	_check_true("E.unknown_at_1", _error_at(r, "clause_unknown", [1]))
	# null child는 인스턴스가 없으므로 node-count는 group(1)+valid leaf(1)=2.
	_check("E.node_count", r["node_count"], 2)


func _test_unknown_clause() -> void:
	print("[F] unknown clause (동적 base ConditionClause 인스턴스)")
	var bare: ConditionClause = load(CLAUSE_SCRIPT).new()
	var g := _group(LG.ALL, [bare])
	var r := ConditionValidator.validate(_cset(g))
	_check("F.valid", r["valid"], false)
	# bare가 base 인스턴스로 들어가면 else->clause_unknown, typed array가 null로 바꿔도 clause_unknown.
	_check_true("F.unknown_at_0", _error_at(r, "clause_unknown", [0]))


func _test_empty_group() -> void:
	print("[G] empty ALL / ANY group")
	var ra := ConditionValidator.validate(_cset(_group(LG.ALL, [])))
	_check("G.all_valid", ra["valid"], false)
	_check_true("G.all_code", _error_at(ra, "group_empty", []))
	var rb := ConditionValidator.validate(_cset(_group(LG.ANY, [])))
	_check("G.any_valid", rb["valid"], false)
	_check_true("G.any_code", _error_at(rb, "group_empty", []))


func _test_not_arity() -> void:
	print("[H] NOT arity: 0/1/2 child")
	var r0 := ConditionValidator.validate(_cset(_group(LG.NOT, [])))
	_check("H.zero_valid", r0["valid"], false)
	_check_true("H.zero_code", _error_at(r0, "not_arity_invalid", []))
	var r2 := ConditionValidator.validate(_cset(_group(LG.NOT, [
		_state(&"a.b", OP.EQUAL, 1), _state(&"a.c", OP.EQUAL, 2)])))
	_check("H.two_valid", r2["valid"], false)
	_check_true("H.two_code", _error_at(r2, "not_arity_invalid", []))
	var r1 := ConditionValidator.validate(_cset(_group(LG.NOT, [_state(&"a.b", OP.EQUAL, 1)])))
	_check("H.one_valid", r1["valid"], true)


func _test_self_cycle() -> void:
	print("[I] self cycle (group이 자기 자신을 child로)")
	var g := _group(LG.ALL, [])
	var children: Array[ConditionClause] = [g]
	g.children = children
	var r := ConditionValidator.validate(_cset(g))
	_check("I.valid", r["valid"], false)
	_check_true("I.code", _has_code(r, "cycle_detected"))
	# fixture가 만든 실제 순환 참조를 끊어 종료 시 Resource 누수가 남지 않게 한다.
	g.children.clear()


func _test_indirect_cycle() -> void:
	print("[J] indirect cycle (A->B->A)")
	var a := _group(LG.ALL, [])
	var b := _group(LG.ALL, [])
	var a_children: Array[ConditionClause] = [b]
	var b_children: Array[ConditionClause] = [a]
	a.children = a_children
	b.children = b_children
	var r := ConditionValidator.validate(_cset(a))
	_check("J.valid", r["valid"], false)
	_check_true("J.code", _has_code(r, "cycle_detected"))
	# A<->B 상호 참조를 끊어 종료 시 Resource 누수가 남지 않게 한다.
	a.children.clear()
	b.children.clear()


func _test_aliased_clause() -> void:
	print("[K] aliased clause (공유 인스턴스, cycle 아님)")
	var shared := _state(&"a.b", OP.EQUAL, 1)
	var g1 := _group(LG.ALL, [shared])
	var g2 := _group(LG.ALL, [shared])
	var root := _group(LG.ALL, [g1, g2])
	var r := ConditionValidator.validate(_cset(root))
	_check("K.valid", r["valid"], false)
	_check_true("K.code", _has_code(r, "clause_aliased"))
	# 두 번째 경로 root->g2->shared 에서 잡힌다: path [1,0].
	_check_true("K.alias_at_1_0", _error_at(r, "clause_aliased", [1, 0]))


# 깊이 num_groups+1의 체인(가장 안쪽 leaf). 가장 바깥 group이 root.
func _chain(num_groups: int) -> ConditionClause:
	var node: ConditionClause = _state(&"a.b", OP.EQUAL, 1)
	for i in num_groups:
		node = _group(LG.ALL, [node])
	return node


func _test_depth_boundary() -> void:
	print("[L] depth 경계: leaf depth 64 (63 groups + leaf)")
	var r := ConditionValidator.validate(_cset(_chain(63)))
	_check("L.valid", r["valid"], true)
	_check("L.node_count", r["node_count"], 64)


func _test_depth_exceeded() -> void:
	print("[M] depth 초과: leaf depth 65 (64 groups + leaf)")
	var r := ConditionValidator.validate(_cset(_chain(64)))
	_check("M.valid", r["valid"], false)
	_check_true("M.code", _has_code(r, "depth_limit_exceeded"))


func _test_node_boundary() -> void:
	print("[N] node 경계: 4096 nodes (1 group + 4095 leaves)")
	var kids: Array = []
	for i in 4095:
		kids.append(_state(&"n.k%d" % i, OP.EQUAL, i))
	var r := ConditionValidator.validate(_cset(_group(LG.ALL, kids)))
	_check("N.valid", r["valid"], true)
	_check("N.node_count", r["node_count"], 4096)


func _test_node_exceeded() -> void:
	print("[O] node 초과: 4097 nodes (1 group + 4096 leaves)")
	var kids: Array = []
	for i in 4096:
		kids.append(_state(&"n.k%d" % i, OP.EQUAL, i))
	var r := ConditionValidator.validate(_cset(_group(LG.ALL, kids)))
	_check("O.valid", r["valid"], false)
	_check_true("O.code", _has_code(r, "node_limit_exceeded"))


func _test_key_errors() -> void:
	print("[P] key_empty / key_invalid_format")
	var re := ConditionValidator.validate(_cset(_state(&"", OP.EQUAL, 1)))
	_check_true("P.empty_code", _error_at(re, "key_empty", []))
	var bad_keys: Array[StringName] = [&"quest", &"Quest.main", &"quest..main", &"quest.main.", &"1quest.main"]
	for k in bad_keys:
		var r := ConditionValidator.validate(_cset(_state(k, OP.EQUAL, 1)))
		_check_true("P.format[%s]" % k, _has_code(r, "key_invalid_format"))


func _test_operator_invalid() -> void:
	print("[Q] operator enum 범위 밖")
	var s := _state(&"a.b", OP.EQUAL, 1)
	s.operator = 99
	var r := ConditionValidator.validate(_cset(s))
	_check("Q.valid", r["valid"], false)
	_check_true("Q.code", _error_at(r, "operator_invalid", []))


func _test_logic_invalid() -> void:
	print("[R] logic enum 범위 밖 (malformed fail-closed)")
	var g := _group(LG.ALL, [_state(&"a.b", OP.EQUAL, 1)])
	g.logic = 42
	var r := ConditionValidator.validate(_cset(g))
	_check("R.valid", r["valid"], false)
	_check_true("R.code", _error_at(r, "logic_invalid", []))


func _test_expected_type_invalid() -> void:
	print("[S] expected null / 미지원 타입")
	var rn := ConditionValidator.validate(_cset(_state(&"a.b", OP.EQUAL, null)))
	_check_true("S.null_code", _error_at(rn, "expected_type_invalid", []))
	var ra := ConditionValidator.validate(_cset(_state(&"a.b", OP.EQUAL, [1, 2])))
	_check_true("S.array_code", _error_at(ra, "expected_type_invalid", []))
	var rv := ConditionValidator.validate(_cset(_state(&"a.b", OP.EQUAL, Vector2(1, 2))))
	_check_true("S.vector_code", _error_at(rv, "expected_type_invalid", []))


func _test_ordered_type_rules() -> void:
	print("[T] ordered 비교는 숫자만")
	# String/bool/StringName ordered -> ordered_type_invalid
	var rs := ConditionValidator.validate(_cset(_state(&"a.b", OP.GREATER, "town")))
	_check_true("T.string_code", _error_at(rs, "ordered_type_invalid", []))
	var rb := ConditionValidator.validate(_cset(_state(&"a.b", OP.LESS, true)))
	_check_true("T.bool_code", _error_at(rb, "ordered_type_invalid", []))
	var rsn := ConditionValidator.validate(_cset(_state(&"a.b", OP.GREATER_EQUAL, &"calm")))
	_check_true("T.sn_code", _error_at(rsn, "ordered_type_invalid", []))
	# int/float ordered -> valid
	var ri := ConditionValidator.validate(_cset(_state(&"a.b", OP.LESS_EQUAL, 5)))
	_check("T.int_valid", ri["valid"], true)
	var rf := ConditionValidator.validate(_cset(_state(&"a.b", OP.GREATER, 2.5)))
	_check("T.float_valid", rf["valid"], true)


func _test_equality_types_valid() -> void:
	print("[U] 다섯 state 타입 equality 통과")
	var cases := [
		_state(&"a.b", OP.EQUAL, true),
		_state(&"a.b", OP.NOT_EQUAL, 7),
		_state(&"a.b", OP.EQUAL, 1.5),
		_state(&"a.b", OP.EQUAL, "town"),
		_state(&"a.b", OP.NOT_EQUAL, &"calm"),
	]
	for i in cases.size():
		var r := ConditionValidator.validate(_cset(cases[i]))
		_check("U.case[%d].valid" % i, r["valid"], true)


func _test_path_reporting() -> void:
	print("[V] 깊은 위치 오류의 path 정확성")
	# ALL[ leaf-ok, ANY[ leaf-ok, NOT[ bad-leaf ] ] ]  -> bad leaf path [1,1,0]
	var bad := _state(&"BadKey", OP.EQUAL, 1)
	var root := _group(LG.ALL, [
		_state(&"a.b", OP.EQUAL, 1),
		_group(LG.ANY, [
			_state(&"a.c", OP.EQUAL, 2),
			_group(LG.NOT, [bad]),
		]),
	])
	var r := ConditionValidator.validate(_cset(root))
	_check("V.valid", r["valid"], false)
	_check_true("V.path", _error_at(r, "key_invalid_format", [1, 1, 0]))


func _test_multiple_errors_collected() -> void:
	print("[W] 서로 다른 형제의 오류를 non-short-circuit으로 모두 수집")
	var root := _group(LG.ALL, [
		_state(&"", OP.EQUAL, 1),              # key_empty at [0]
		_state(&"a.b", 99, 1),                 # operator_invalid at [1]
		_state(&"a.c", OP.GREATER, "x"),       # ordered_type_invalid at [2]
	])
	var r := ConditionValidator.validate(_cset(root))
	_check("W.valid", r["valid"], false)
	_check_true("W.key_empty", _error_at(r, "key_empty", [0]))
	_check_true("W.operator", _error_at(r, "operator_invalid", [1]))
	_check_true("W.ordered", _error_at(r, "ordered_type_invalid", [2]))
	_check("W.error_count", r["errors"].size(), 3)


func _test_result_immutability() -> void:
	print("[X] 반환 결과 변조가 다음 검증에 영향 없음(deep copy)")
	var cs := _make_valid_tree()
	var r := ConditionValidator.validate(cs)
	r["valid"] = false
	r["errors"].append({"code": "tampered"})
	r["error_codes"].append("tampered")
	var r2 := ConditionValidator.validate(cs)
	_check("X.fresh_valid", r2["valid"], true)
	_check_true("X.no_tampered", not _has_code(r2, "tampered"))


func _test_roundtrip_save_reload() -> void:
	print("[Y] .tres 저장 -> cache 무시 재로드 왕복")
	var cs := _make_valid_tree()
	cs.description = "roundtrip sample"
	cs.tags = [&"quest", &"actor"] as Array[StringName]
	var pre := ConditionValidator.validate(cs)
	_check("Y.pre_valid", pre["valid"], true)

	var save_err := ResourceSaver.save(cs, TMP_PATH)
	_check("Y.save_ok", save_err, OK)

	var loaded: ConditionSet = ResourceLoader.load(TMP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	_check_true("Y.loaded_not_null", loaded != null)
	if loaded == null:
		_cleanup()
		return

	# metadata 보존
	_check("Y.description", loaded.description, "roundtrip sample")
	_check("Y.tags", loaded.tags, cs.tags)

	# 재검증 통과 + node_count 동일
	var post := ConditionValidator.validate(loaded)
	_check("Y.post_valid", post["valid"], true)
	_check("Y.node_count", post["node_count"], pre["node_count"])

	# 구조/순서/operator/expected typeof 보존
	var root := loaded.root as ConditionGroup
	_check("Y.root_logic", root.logic, LG.ALL)
	_check("Y.root_children", root.children.size(), 3)
	var c0 := root.children[0] as StateCondition
	_check("Y.c0_op", c0.operator, OP.GREATER_EQUAL)
	_check("Y.c0_expected_typeof", typeof(c0.expected_value), TYPE_INT)
	# StringName expected 보존(strict 구분)
	var c2 := root.children[2] as StateCondition
	_check("Y.c2_expected", c2.expected_value, &"calm")
	_check("Y.c2_expected_typeof", typeof(c2.expected_value), TYPE_STRING_NAME)
	# NOT 안의 bool expected 보존
	var any_g := root.children[1] as ConditionGroup
	var not_g := any_g.children[1] as ConditionGroup
	var seen := not_g.children[0] as StateCondition
	_check("Y.seen_expected_typeof", typeof(seen.expected_value), TYPE_BOOL)

	_cleanup()


func _cleanup() -> void:
	if FileAccess.file_exists(TMP_PATH):
		var err := DirAccess.remove_absolute(ProjectSettings.globalize_path(TMP_PATH))
		_check("cleanup.removed", err, OK)
	else:
		print("  (cleanup) temp file already absent")
