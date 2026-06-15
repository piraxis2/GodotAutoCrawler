# DT-008 Step 2 SPIKE (F4): 중첩 ConditionSet runtime snapshot의 .tres 왕복 보존.
#
# 목적(제품 에디터 구현 전 위험 확인, ADR-009 / DT-008 Step 0 F4):
# - state_condition의 get_runtime_params()는 {"condition_set": <Resource>}를 반환하고, 이는
#   DialogueGraphResource.runtime_nodes(untyped Dictionary)에 {id: {params: {condition_set: ...}}}로
#   2중 중첩 저장된다.
# - DT-007 spike는 typed Array[ConditionClause]를 ConditionSet .tres에 *직접* export하는 backbone만
#   보장했다. 여기서는 Dictionary 2중 중첩 Resource 참조가 external(ext_resource) / inline(sub_resource)
#   양쪽에서 CACHE_MODE_IGNORE 재로드 후 보존되는지 확인한다.
#
# 검증:
# - external: 미리 .tres로 저장한 ConditionSet을 참조 -> 그래프 저장 시 ext_resource로 기록되고
#   재로드 후 같은 트리를 가리킨다.
# - inline: in-memory ConditionSet을 참조 -> 그래프 저장 시 sub_resource로 인라인되고 재로드 후
#   트리(자식 순서/구체 subtype/operator/expected typeof)가 보존된다.
# - 재로드된 condition_set을 실제 ConditionEvaluator로 평가해 결과가 살아 있는지 확인한다.
#
# 실행:
#   godot --headless --path <project> --import
#   godot --headless --path <project> res://addons/dialogtool/RunTime/tests/dt008_step2_snapshot_spike.tscn
#
# 결과: ALL PASS면 Step 2 에디터 구현 진행, 실패면 Design Deviation 보고.
extends Node

const OP := StateCondition.Operator
const LG := ConditionGroup.Logic
const CS_EXT_PATH := "user://dt008_spike_condition_set.tres"
const GRAPH_EXT_PATH := "user://dt008_spike_graph_external.tres"
const GRAPH_INLINE_PATH := "user://dt008_spike_graph_inline.tres"

var _failures: int = 0


func _ready() -> void:
	_test_external_reference_roundtrip()
	_test_inline_subresource_roundtrip()
	_cleanup()

	if _failures == 0:
		print("[DT-008 Step2 Spike] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-008 Step2 Spike] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


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


# ALL[ quest.main.stage >= 3 , ANY[ actor.example.affinity >= 10 , NOT[ session.intro.seen == true ] ] ]
func _sample_set() -> ConditionSet:
	var leaf_stage := _state(&"quest.main.stage", OP.GREATER_EQUAL, 3)
	var leaf_aff := _state(&"actor.example.affinity", OP.GREATER_EQUAL, 10)
	var leaf_seen := _state(&"session.intro.seen", OP.EQUAL, true)
	var any_group := _group(LG.ANY, [leaf_aff, _group(LG.NOT, [leaf_seen])])
	var cs := ConditionSet.new()
	cs.root = _group(LG.ALL, [leaf_stage, any_group])
	cs.description = "spike"
	return cs


# state_condition 노드 하나를 담은 runtime snapshot 그래프를 만든다.
func _graph_with_condition(cs: ConditionSet) -> DialogueGraphResource:
	var res := DialogueGraphResource.new()
	res.runtime_nodes = {
		7: {"id": 7, "type": &"state_condition", "params": {"condition_set": cs}},
	}
	return res


# 재로드된 그래프에서 중첩 condition_set을 꺼내 트리/평가를 검증한다.
func _assert_nested_set(tag: String, loaded: DialogueGraphResource) -> void:
	_check_true("%s.loaded_not_null" % tag, loaded != null)
	if loaded == null:
		return
	var node: Dictionary = loaded.runtime_nodes.get(7, {})
	_check("%s.node_type" % tag, node.get("type"), &"state_condition")
	var params: Dictionary = node.get("params", {})
	var cs = params.get("condition_set")
	_check_true("%s.cs_is_set" % tag, cs is ConditionSet)
	if not (cs is ConditionSet):
		return

	# 트리 구조 보존: ALL > [State, ANY > [State, NOT > [State]]]
	var root = cs.root
	_check_true("%s.root_group" % tag, root is ConditionGroup)
	if not (root is ConditionGroup):
		return
	_check("%s.root_logic" % tag, (root as ConditionGroup).logic, LG.ALL)
	_check("%s.root_children" % tag, (root as ConditionGroup).children.size(), 2)
	var c0 = (root as ConditionGroup).children[0]
	_check_true("%s.c0_state" % tag, c0 is StateCondition)
	_check("%s.c0_key" % tag, (c0 as StateCondition).key, &"quest.main.stage")
	_check("%s.c0_op" % tag, (c0 as StateCondition).operator, OP.GREATER_EQUAL)
	_check("%s.c0_expected_typeof" % tag, typeof((c0 as StateCondition).expected_value), TYPE_INT)

	# 재로드된 set이 실제 evaluator에서 동작하는가(구조 valid + read).
	var fake := _FakeProvider.new({
		&"quest.main.stage": 5, &"actor.example.affinity": 10, &"session.intro.seen": false,
	})
	var report := ConditionEvaluator.evaluate(cs, fake)
	_check("%s.eval_valid" % tag, report["valid"], true)
	_check("%s.eval_passed" % tag, report["passed"], true)


func _test_external_reference_roundtrip() -> void:
	print("[Spike A] external ConditionSet 참조(ext_resource) 왕복")
	# 먼저 ConditionSet을 자체 .tres로 저장해 resource_path를 갖게 한다(→ ext_resource로 기록).
	var cs := _sample_set()
	var cs_save := ResourceSaver.save(cs, CS_EXT_PATH)
	_check("A.cs_save_ok", cs_save, OK)
	var cs_ext: ConditionSet = ResourceLoader.load(CS_EXT_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	_check_true("A.cs_ext_loaded", cs_ext != null)

	var graph := _graph_with_condition(cs_ext)
	var g_save := ResourceSaver.save(graph, GRAPH_EXT_PATH)
	_check("A.graph_save_ok", g_save, OK)

	# 저장 파일에 ext_resource로 ConditionSet 경로가 기록됐는지 확인(2중 중첩 참조 직렬화).
	var text := FileAccess.get_file_as_string(GRAPH_EXT_PATH)
	_check_true("A.has_ext_resource", text.contains("ext_resource") and text.contains("dt008_spike_condition_set"))

	var loaded: DialogueGraphResource = ResourceLoader.load(GRAPH_EXT_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	_assert_nested_set("A", loaded)


func _test_inline_subresource_roundtrip() -> void:
	print("[Spike B] inline ConditionSet(sub_resource) 왕복")
	# in-memory ConditionSet(저장 경로 없음) → 그래프 저장 시 sub_resource로 인라인.
	var cs := _sample_set()   # resource_path 없음
	var graph := _graph_with_condition(cs)
	var g_save := ResourceSaver.save(graph, GRAPH_INLINE_PATH)
	_check("B.graph_save_ok", g_save, OK)

	var text := FileAccess.get_file_as_string(GRAPH_INLINE_PATH)
	_check_true("B.has_sub_resource", text.contains("sub_resource"))

	var loaded: DialogueGraphResource = ResourceLoader.load(GRAPH_INLINE_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	_assert_nested_set("B", loaded)


func _cleanup() -> void:
	for p in [CS_EXT_PATH, GRAPH_EXT_PATH, GRAPH_INLINE_PATH]:
		if FileAccess.file_exists(p):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(p))


# duck-typed read provider.
class _FakeProvider:
	var data: Dictionary
	func _init(d: Dictionary = {}) -> void:
		data = d
	func has_state(key: StringName) -> bool:
		return data.has(key)
	func read_state(key: StringName) -> Variant:
		return data.get(key)
