# DT-012 Step 1 검증용 헤드리스 테스트(Condition Summary Formatter).
# 실행:
#   godot --headless --path <project> --import   (새 class_name 캐시 생성)
#   godot --headless --path <project> res://addons/world_core/world_state/condition/tests/dt012_step1_condition_summary_test.tscn
#
# 검증 범위(DT-012 Step 1 Completion Criteria):
# - null ConditionSet -> "No ConditionSet" + invalid/error code
# - valid leaf INT/FLOAT 표기 구분
# - String vs StringName literal 구분
# - bool literal 표기
# - ALL/ANY/NOT group 요약
# - description-first: valid는 description 우선/full_summary는 구조, invalid는 invalid 우선
# - structural invalid(empty group/NOT arity/cycle/alias/depth/node limit) -> 구조 요약 금지, invalid
# - 긴 summary는 잘리고 full_summary는 전체 보존
# - validate-first(트리 순회 전 validation), provider read 0
extends Node

const OP := StateCondition.Operator
const LG := ConditionGroup.Logic

var _failures: int = 0


func _ready() -> void:
	_test_null_condition_set()
	_test_leaf_int()
	_test_leaf_float_distinct_from_int()
	_test_string_vs_stringname()
	_test_bool_literal()
	_test_group_all_any_not()
	_test_description_priority_valid()
	_test_description_invalid_still_invalid()
	_test_empty_group_and_not_arity_invalid()
	_test_cyclic_and_aliased_invalid()
	_test_depth_and_node_limit_invalid()
	_test_long_summary_truncation()
	_test_operator_symbols_not_trace()
	_test_string_literal_escaping()

	if _failures == 0:
		print("[DT-012 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-012 Step1] FAILED: %d assertion(s)" % _failures)
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


func _cset(root: ConditionClause, description := "") -> ConditionSet:
	var cs := ConditionSet.new()
	cs.root = root
	cs.description = description
	return cs


# --- 시나리오 ---------------------------------------------------------

func _test_null_condition_set() -> void:
	print("[1] null ConditionSet -> No ConditionSet")
	var r := ConditionSummary.summarize(null)
	_check("1.valid", r["valid"], false)
	_check("1.summary", r["summary"], "No ConditionSet")
	_check_true("1.has_error_code", r["error_codes"].has("condition_set_null"))


func _test_leaf_int() -> void:
	print("[2] valid leaf INT >= 10")
	var r := ConditionSummary.summarize(_cset(_state(&"actor.example.affinity", OP.GREATER_EQUAL, 10)))
	_check("2.valid", r["valid"], true)
	_check("2.summary", r["summary"], "actor.example.affinity >= 10")
	_check("2.full", r["full_summary"], "actor.example.affinity >= 10")


func _test_leaf_float_distinct_from_int() -> void:
	print("[3] valid leaf FLOAT >= 10.0 (INT와 구분)")
	var rf := ConditionSummary.summarize(_cset(_state(&"actor.example.affinity", OP.GREATER_EQUAL, 10.0)))
	var ri := ConditionSummary.summarize(_cset(_state(&"actor.example.affinity", OP.GREATER_EQUAL, 10)))
	_check("3.float_summary", rf["summary"], "actor.example.affinity >= 10.0")
	_check("3.int_summary", ri["summary"], "actor.example.affinity >= 10")
	_check_true("3.distinct", rf["summary"] != ri["summary"])


func _test_string_vs_stringname() -> void:
	print("[4] String vs StringName literal 구분")
	var rs := ConditionSummary.summarize(_cset(_state(&"actor.example.mood", OP.EQUAL, "calm")))
	var rsn := ConditionSummary.summarize(_cset(_state(&"actor.example.mood", OP.EQUAL, &"calm")))
	_check("4.string", rs["summary"], "actor.example.mood == \"calm\"")
	_check("4.stringname", rsn["summary"], "actor.example.mood == &\"calm\"")
	_check_true("4.distinct", rs["summary"] != rsn["summary"])


func _test_bool_literal() -> void:
	print("[5] bool literal 표기")
	var rt := ConditionSummary.summarize(_cset(_state(&"session.intro.seen", OP.EQUAL, true)))
	var rfalse := ConditionSummary.summarize(_cset(_state(&"session.intro.seen", OP.NOT_EQUAL, false)))
	_check("5.true", rt["summary"], "session.intro.seen == true")
	_check("5.false", rfalse["summary"], "session.intro.seen != false")


func _test_group_all_any_not() -> void:
	print("[6] ALL/ANY/NOT group 요약")
	var all_set := _cset(_group(LG.ALL, [
		_state(&"quest.main.stage", OP.GREATER_EQUAL, 3),
		_state(&"actor.example.affinity", OP.GREATER_EQUAL, 10),
	]))
	var any_set := _cset(_group(LG.ANY, [
		_state(&"quest.main.stage", OP.EQUAL, 1),
		_state(&"quest.main.stage", OP.EQUAL, 2),
	]))
	var not_set := _cset(_group(LG.NOT, [_state(&"session.intro.seen", OP.EQUAL, true)]))
	_check("6.all", ConditionSummary.summarize(all_set)["summary"],
		"ALL(quest.main.stage >= 3, actor.example.affinity >= 10)")
	_check("6.any", ConditionSummary.summarize(any_set)["summary"],
		"ANY(quest.main.stage == 1, quest.main.stage == 2)")
	_check("6.not", ConditionSummary.summarize(not_set)["summary"],
		"NOT(session.intro.seen == true)")


func _test_description_priority_valid() -> void:
	print("[7] valid + description -> description 우선, full_summary는 구조")
	var cs := _cset(_state(&"actor.example.affinity", OP.GREATER_EQUAL, 10), "친밀도가 충분히 높을 때")
	var r := ConditionSummary.summarize(cs)
	_check("7.valid", r["valid"], true)
	_check("7.summary", r["summary"], "친밀도가 충분히 높을 때")
	_check("7.full", r["full_summary"], "actor.example.affinity >= 10")
	_check_true("7.tooltip_has_structure", r["tooltip"].contains("actor.example.affinity >= 10"))


func _test_description_invalid_still_invalid() -> void:
	print("[8] invalid + description -> invalid 우선")
	var cs := _cset(null, "이 설명은 invalid를 가리면 안 된다")
	var r := ConditionSummary.summarize(cs)
	_check("8.valid", r["valid"], false)
	_check("8.summary", r["summary"], "Invalid: root_null")
	_check_true("8.no_description_in_summary", not r["summary"].contains("설명"))


func _test_empty_group_and_not_arity_invalid() -> void:
	print("[9] empty group / NOT arity -> 구조 요약 금지, invalid")
	var empty := ConditionSummary.summarize(_cset(_group(LG.ALL, [])))
	_check("9.empty_valid", empty["valid"], false)
	_check("9.empty_summary", empty["summary"], "Invalid: group_empty")
	_check_true("9.empty_no_paren", not empty["full_summary"].contains("ALL("))

	var not2 := ConditionSummary.summarize(_cset(_group(LG.NOT, [
		_state(&"a.b", OP.EQUAL, 1), _state(&"a.c", OP.EQUAL, 2)])))
	_check("9.not_valid", not2["valid"], false)
	_check("9.not_summary", not2["summary"], "Invalid: not_arity_invalid")
	_check_true("9.not_no_paren", not not2["full_summary"].contains("NOT("))


func _test_cyclic_and_aliased_invalid() -> void:
	print("[10] cyclic / aliased -> crash 없이 invalid")
	# self cycle
	var g := _group(LG.ALL, [])
	var children: Array[ConditionClause] = [g]
	g.children = children
	var rc := ConditionSummary.summarize(_cset(g))
	_check("10.cycle_valid", rc["valid"], false)
	_check("10.cycle_summary", rc["summary"], "Invalid: cycle_detected")
	g.children.clear() # 순환 참조 해제(누수 방지)

	# aliased(공유 인스턴스)
	var shared := _state(&"a.b", OP.EQUAL, 1)
	var root := _group(LG.ALL, [_group(LG.ALL, [shared]), _group(LG.ALL, [shared])])
	var ra := ConditionSummary.summarize(_cset(root))
	_check("10.alias_valid", ra["valid"], false)
	_check("10.alias_summary", ra["summary"], "Invalid: clause_aliased")


func _test_depth_and_node_limit_invalid() -> void:
	print("[11] depth/node limit 초과 -> crash 없이 invalid")
	# depth 65: 64 groups + leaf
	var node: ConditionClause = _state(&"a.b", OP.EQUAL, 1)
	for i in 64:
		node = _group(LG.ALL, [node])
	var rd := ConditionSummary.summarize(_cset(node))
	_check("11.depth_valid", rd["valid"], false)
	_check("11.depth_summary", rd["summary"], "Invalid: depth_limit_exceeded")

	# node 4097: 1 group + 4096 leaves
	var kids: Array = []
	for i in 4096:
		kids.append(_state(&"n.k%d" % i, OP.EQUAL, i))
	var rn := ConditionSummary.summarize(_cset(_group(LG.ALL, kids)))
	_check("11.node_valid", rn["valid"], false)
	_check("11.node_summary", rn["summary"], "Invalid: node_limit_exceeded")


func _test_long_summary_truncation() -> void:
	print("[12] 긴 summary -> summary 잘림, full_summary 전체 보존")
	var kids: Array = []
	for i in 20:
		kids.append(_state(&"quest.main.flag%d" % i, OP.EQUAL, i))
	var r := ConditionSummary.summarize(_cset(_group(LG.ALL, kids)))
	_check("12.valid", r["valid"], true)
	_check_true("12.summary_truncated", r["summary"].length() <= ConditionSummary.DEFAULT_MAX_LENGTH)
	_check_true("12.summary_ends_ellipsis", r["summary"].ends_with(ConditionSummary.ELLIPSIS))
	_check_true("12.full_longer", r["full_summary"].length() > r["summary"].length())
	_check_true("12.full_complete", r["full_summary"].ends_with("quest.main.flag19 == 19)"))

	# custom max_length 옵션도 적용된다.
	var r2 := ConditionSummary.summarize(_cset(_group(LG.ALL, kids)), {"max_length": 20})
	_check_true("12.custom_limit", r2["summary"].length() <= 20)
	_check("12.full_unchanged", r2["full_summary"], r["full_summary"])


func _test_operator_symbols_not_trace() -> void:
	print("[13] 표시 operator는 trace 문자열이 아니다")
	var r := ConditionSummary.summarize(_cset(_state(&"a.b", OP.GREATER_EQUAL, 1)))
	# trace 문자열 'greater_equal'이 아니라 '>=' 기호를 써야 한다.
	_check_true("13.uses_symbol", r["summary"].contains(">="))
	_check_true("13.no_trace_string", not r["summary"].contains("greater_equal"))


func _test_string_literal_escaping() -> void:
	print("[14] String/StringName literal escaping (quote/newline/backslash/tab/cr)")
	# 따옴표: he said "yes" -> "he said \"yes\"" (모호하지 않게)
	var rq := ConditionSummary.summarize(_cset(_state(&"a.b", OP.EQUAL, "he said \"yes\"")))
	_check("14.quote", rq["summary"], "a.b == \"he said \\\"yes\\\"\"")
	# 줄바꿈/캐리지리턴/탭은 escape 시퀀스로(여러 줄로 깨지지 않음)
	var rn := ConditionSummary.summarize(_cset(_state(&"a.b", OP.EQUAL, "line1\nline2")))
	_check("14.newline", rn["summary"], "a.b == \"line1\\nline2\"")
	_check_true("14.single_line", not rn["summary"].contains("\n"))
	var rt := ConditionSummary.summarize(_cset(_state(&"a.b", OP.EQUAL, "a\tb\rc")))
	_check("14.tab_cr", rt["summary"], "a.b == \"a\\tb\\rc\"")
	# 백슬래시는 이중 escape 없이 한 번만(\\ -> \\\\, 추가 처리 없음)
	var rb := ConditionSummary.summarize(_cset(_state(&"a.b", OP.EQUAL, "c:\\path")))
	_check("14.backslash", rb["summary"], "a.b == \"c:\\\\path\"")
	# StringName도 같은 escape를 쓰되 &"..." 표기는 유지
	var rsn := ConditionSummary.summarize(_cset(_state(&"a.b", OP.EQUAL, &"x\"y")))
	_check("14.stringname", rsn["summary"], "a.b == &\"x\\\"y\"")
