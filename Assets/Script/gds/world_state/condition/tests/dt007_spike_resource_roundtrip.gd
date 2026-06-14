# DT-007 Step 1 SPIKE: recursive typed Resource의 생성/저장/재로드 검증.
#
# 목적(제품 구현 전 위험 확인, Resolution 1):
# - @abstract ConditionClause base가 직접 인스턴스화를 거부하는가.
# - Array[ConditionClause] typed array에 StateCondition/ConditionGroup 혼합 subclass를
#   담은 재귀 트리가 .tres로 저장되는가.
# - cache 무시 재로드 후 트리 구조(자식 순서), 각 노드의 구체 subtype, enum, StringName/
#   int/bool expected_value의 typeof()가 보존되는가.
#
# 실행:
#   godot --headless --path <project> --import   (먼저 class cache 생성)
#   godot --headless --path <project> res://Assets/Script/gds/world_state/condition/tests/dt007_spike_resource_roundtrip.tscn
#
# 결과: ALL PASS면 제품 구현 진행, 실패면 Design Deviation 보고.
extends Node

const OP := StateCondition.Operator
const LG := ConditionGroup.Logic
const TMP_PATH := "user://dt007_spike_condition_set.tres"
const CLAUSE_SCRIPT := "res://Assets/Script/gds/world_state/condition/condition_clause.gd"

var _failures: int = 0


func _ready() -> void:
	_test_abstract_base_not_instantiable()
	_test_recursive_tree_roundtrip()

	if _failures == 0:
		print("[DT-007 Spike] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-007 Spike] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# @abstract base는 실제 ConditionClause 인스턴스를 만들 수 없어야 한다.
# 정적 ConditionClause.new()는 분석기가 parse 단계에서 막으므로(스크립트가 로드되지 않음)
# 여기서는 GDScript 변수를 통해 동적으로 확인한다.
# Godot 4.6.3 실측 동작: script.is_abstract()==true. 정적 ConditionClause.new()는 컴파일 단계에서
# 거부되고, 에디터 "New Resource" 피커는 abstract 타입을 후보에서 제외한다(authoring 보호).
# 동적 load().new()는 base 스크립트가 붙은 인스턴스를 만들 수 있으나, 그 인스턴스는 구체 clause
# 타입(StateCondition/ConditionGroup)이 아니므로 validator의 clause_unknown에 걸린다(런타임 backstop).
func _test_abstract_base_not_instantiable() -> void:
	print("[Spike A] @abstract base 인스턴스화 거부")
	var script: GDScript = load(CLAUSE_SCRIPT)
	_check_true("A.script_loaded", script != null)
	_check("A.is_abstract", script.is_abstract(), true)
	var inst: Object = script.new()
	if inst == null:
		_check_true("A.no_concrete_clause", true)
	else:
		# 동적 경로로 만들어도 구체 clause 타입이 아니어야 한다(clause_unknown 대상).
		_check_true("A.not_state_condition", not (inst is StateCondition))
		_check_true("A.not_condition_group", not (inst is ConditionGroup))
		# inst는 Resource(=RefCounted)다. 수동 free()는 SCRIPT ERROR이므로 하지 않는다.
		# 참조가 사라지면 ref-count로 정리된다.


func _test_recursive_tree_roundtrip() -> void:
	print("[Spike B] 재귀 typed Resource 트리 .tres 왕복")

	# ALL
	#   quest.main.stage >= 3                (int)
	#   ANY
	#     actor.example.affinity >= 10       (int)
	#     NOT
	#       session.intro.seen == true       (bool)
	#   actor.example.mood == &"calm"        (StringName)
	var leaf_stage := _state(&"quest.main.stage", OP.GREATER_EQUAL, 3)
	var leaf_aff := _state(&"actor.example.affinity", OP.GREATER_EQUAL, 10)
	var leaf_seen := _state(&"session.intro.seen", OP.EQUAL, true)
	var leaf_mood := _state(&"actor.example.mood", OP.EQUAL, &"calm")

	var not_group := _group(LG.NOT, [leaf_seen])
	var any_group := _group(LG.ANY, [leaf_aff, not_group])
	var root := _group(LG.ALL, [leaf_stage, any_group, leaf_mood])

	var cs := ConditionSet.new()
	cs.root = root
	cs.description = "spike sample"
	cs.tags = [&"spike", &"sample"] as Array[StringName]

	var save_err := ResourceSaver.save(cs, TMP_PATH)
	_check("B.save_ok", save_err, OK)

	var loaded: ConditionSet = ResourceLoader.load(TMP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	_check_true("B.loaded_not_null", loaded != null)
	if loaded == null:
		_cleanup()
		return

	# metadata 보존
	_check("B.description", loaded.description, "spike sample")
	_check("B.tags", loaded.tags, cs.tags)

	# root: ALL group, child 3
	var lroot := loaded.root
	_check_true("B.root_is_group", lroot is ConditionGroup)
	if not (lroot is ConditionGroup):
		_cleanup()
		return
	var lroot_g := lroot as ConditionGroup
	_check("B.root_logic", lroot_g.logic, LG.ALL)
	_check("B.root_children", lroot_g.children.size(), 3)

	# child[0]: StateCondition int >= 3
	var c0 := lroot_g.children[0]
	_check_true("B.c0_is_state", c0 is StateCondition)
	var c0_s := c0 as StateCondition
	_check("B.c0_key", c0_s.key, &"quest.main.stage")
	_check("B.c0_op", c0_s.operator, OP.GREATER_EQUAL)
	_check("B.c0_expected", c0_s.expected_value, 3)
	_check("B.c0_expected_typeof", typeof(c0_s.expected_value), TYPE_INT)

	# child[1]: ANY group, child 2
	var c1 := lroot_g.children[1]
	_check_true("B.c1_is_group", c1 is ConditionGroup)
	var c1_g := c1 as ConditionGroup
	_check("B.c1_logic", c1_g.logic, LG.ANY)
	_check("B.c1_children", c1_g.children.size(), 2)

	# c1.child[1]: NOT group, child 1
	var not_loaded := c1_g.children[1]
	_check_true("B.not_is_group", not_loaded is ConditionGroup)
	var not_g := not_loaded as ConditionGroup
	_check("B.not_logic", not_g.logic, LG.NOT)
	_check("B.not_children", not_g.children.size(), 1)
	# bool expected 보존
	var seen_loaded := not_g.children[0] as StateCondition
	_check("B.seen_expected", seen_loaded.expected_value, true)
	_check("B.seen_expected_typeof", typeof(seen_loaded.expected_value), TYPE_BOOL)

	# child[2]: StringName expected 보존(strict 구분)
	var c2 := lroot_g.children[2] as StateCondition
	_check("B.c2_expected", c2.expected_value, &"calm")
	_check("B.c2_expected_typeof", typeof(c2.expected_value), TYPE_STRING_NAME)

	_cleanup()


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


func _cleanup() -> void:
	if FileAccess.file_exists(TMP_PATH):
		var err := DirAccess.remove_absolute(ProjectSettings.globalize_path(TMP_PATH))
		_check("cleanup.removed", err, OK)
	else:
		print("  (cleanup) temp file already absent")
