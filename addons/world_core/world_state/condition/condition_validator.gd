@tool
class_name ConditionValidator extends RefCounted
## ConditionSet 트리의 구조 검증 (DT-007 Step 1, ADR-008 D5).
##
## 책임:
## - 2단계 평가의 1단계(structural validation)만 담당한다. provider를 읽지 않는다(read_count==0).
## - null/unknown clause, 빈 group, NOT arity, cycle, alias, depth/node 한계를
##   iterative(explicit-stack) traversal로 검사한다(naive 재귀의 stack overflow 회피).
## - StateCondition leaf의 key 형식, operator enum, expected_value 타입, ordered 숫자 제약을 검사한다.
## - 구조화된 {code, path, key, message} 오류와 deep-copy 결과를 반환한다.
##
## 비책임:
## - 실제 true/false 평가, provider read, comparison (Step 2 ConditionEvaluator).
## - 부분 compiled lookup/evaluation 데이터 공개 금지. 이 클래스는 stateless static이며
##   호출마다 새 결과를 만든다(invalid tree에서 어떤 평가 데이터도 노출하지 않는다).
##
## strict tree: 인스턴스 공유(aliasing)와 cycle을 identity visited-set으로 함께 거부한다.

const DEPTH_LIMIT := 64
const NODE_LIMIT := 4096

# expected_value로 허용하는 scalar 타입(DT-005의 다섯 state 타입과 동일).
const SUPPORTED_EXPECTED_TYPES: Array[int] = [
	TYPE_BOOL, TYPE_INT, TYPE_FLOAT, TYPE_STRING, TYPE_STRING_NAME,
]

# condition key는 등록된 world state key여야 하므로 DT-005의 canonical key 문법을 그대로 쓴다
# (단일 source of truth: StateSchema.KEY_PATTERN). Step 1은 형식만 검사하고 schema lookup은 하지 않는다.
static var _key_regex: RegEx


static func _get_key_regex() -> RegEx:
	if _key_regex == null:
		_key_regex = RegEx.new()
		_key_regex.compile(StateSchema.KEY_PATTERN)
	return _key_regex


## ConditionSet의 구조를 검증한다.
##
## 결과 구조(호출마다 새 deep copy):
## {
##   "valid": bool,                 # errors.is_empty()
##   "errors": Array[Dictionary],   # {code, path, key, message}
##   "error_codes": Array[String],  # 빠른 단언용 code 목록(errors와 같은 순서)
##   "node_count": int,             # traversal 중 방문한 clause 인스턴스 수(null child 제외)
## }
##
## path는 root(`[]`)에서 child index 배열이며 모든 error가 같은 위치 표현을 공유한다.
## cycle/alias/depth/node 초과는 구조 거부로 즉시 traversal을 중단한다. leaf/group 내용 오류는
## non-short-circuit으로 모두 수집한다.
static func validate(condition_set: ConditionSet) -> Dictionary:
	var errors: Array[Dictionary] = []
	var node_count := 0

	if condition_set == null:
		errors.append(_error("condition_set_null", [], &"", "condition_set is null"))
		return _result(errors, node_count)

	var root: ConditionClause = condition_set.root
	if root == null:
		errors.append(_error("root_null", [], &"", "condition_set.root is null"))
		return _result(errors, node_count)

	# identity 기반 visited/on_path로 cycle과 alias를 함께 잡는다.
	var on_path: Dictionary = {}   # instance_id -> true: 현재 DFS 경로 상의 조상
	var visited: Dictionary = {}   # instance_id -> true: 한 번이라도 방문한 인스턴스
	var stack: Array[Dictionary] = []
	stack.append(_frame(root, [], 1))

	while not stack.is_empty():
		var frame: Dictionary = stack[stack.size() - 1]
		var clause: ConditionClause = frame["clause"]

		if not frame["entered"]:
			frame["entered"] = true
			var path: Array = frame["path"]
			var depth: int = frame["depth"]

			# depth 한계: 조상 체인 깊이. 초과는 구조 거부(즉시 중단, provider read 없음).
			if depth > DEPTH_LIMIT:
				errors.append(_error("depth_limit_exceeded", path, _clause_key(clause),
					"depth %d exceeds limit %d" % [depth, DEPTH_LIMIT]))
				break

			var id := clause.get_instance_id()
			# cycle: 조상으로 되돌아가는 back-edge.
			if on_path.has(id):
				errors.append(_error("cycle_detected", path, _clause_key(clause),
					"clause instance re-enters its own ancestor path (cycle)"))
				break
			# alias: 조상은 아니나 이미 다른 경로에서 방문한 공유 인스턴스(strict tree 위반).
			if visited.has(id):
				errors.append(_error("clause_aliased", path, _clause_key(clause),
					"clause instance is shared by more than one parent (aliased)"))
				break

			visited[id] = true
			on_path[id] = true
			node_count += 1
			if node_count > NODE_LIMIT:
				errors.append(_error("node_limit_exceeded", path, _clause_key(clause),
					"node count exceeds limit %d" % NODE_LIMIT))
				break

			# 타입별 노드 내용 검증(형제 leaf 오류는 계속 수집).
			if clause is StateCondition:
				_validate_state(clause as StateCondition, path, errors)
			elif clause is ConditionGroup:
				_validate_group_shape(clause as ConditionGroup, path, errors)
			else:
				# @abstract base가 동적으로 만들어졌거나 알 수 없는 subclass(예: 손상된 .tres).
				errors.append(_error("clause_unknown", path, &"",
					"clause is neither StateCondition nor ConditionGroup"))

		# 자식 진행(ConditionGroup만). 한 번에 하나씩 push해 explicit-stack 깊이를 제어한다.
		if clause is ConditionGroup:
			var g := clause as ConditionGroup
			var idx: int = frame["idx"]
			if idx < g.children.size():
				frame["idx"] = idx + 1
				var child: ConditionClause = g.children[idx]
				var child_path: Array = (frame["path"] as Array).duplicate()
				child_path.append(idx)
				if child == null:
					# null child: 인스턴스가 없으므로 node-count에 포함하지 않고 clause_unknown으로 보고한다
					# (고정 코드 목록에 child_null이 없음). 형제는 계속 검사한다.
					errors.append(_error("clause_unknown", child_path, &"",
						"child at index %d is null" % idx))
					continue
				stack.append(_frame(child, child_path, int(frame["depth"]) + 1))
				continue

		# 자식 소진 또는 leaf: 경로에서 제거하고 pop.
		on_path.erase(clause.get_instance_id())
		stack.pop_back()

	return _result(errors, node_count)


# --- 노드별 내용 검증 -------------------------------------------------

static func _validate_state(sc: StateCondition, path: Array, errors: Array) -> void:
	var key_str := String(sc.key)
	if key_str.is_empty():
		errors.append(_error("key_empty", path, sc.key, "key is empty"))
	elif _get_key_regex().search(key_str) == null:
		errors.append(_error("key_invalid_format", path, sc.key,
			"key '%s' does not match %s" % [key_str, StateSchema.KEY_PATTERN]))

	var op_known := StateCondition.is_known_operator(sc.operator)
	if not op_known:
		errors.append(_error("operator_invalid", path, sc.key,
			"unknown operator enum %d" % sc.operator))

	var t := typeof(sc.expected_value)
	if not _is_supported_expected_type(t):
		# null과 미지원 타입(Array/Object 등)을 함께 거부한다.
		errors.append(_error("expected_type_invalid", path, sc.key,
			"expected_value type %d is not a supported scalar (bool/int/float/String/StringName)" % t))
	elif op_known and StateCondition.is_ordered_operator(sc.operator):
		# ordered 비교는 숫자(int/float)만 허용한다(String/StringName lexical 거부).
		if t != TYPE_INT and t != TYPE_FLOAT:
			errors.append(_error("ordered_type_invalid", path, sc.key,
				"ordered operator '%s' requires int/float expected_value, got type %d"
					% [StateCondition.operator_to_string(sc.operator), t]))


static func _validate_group_shape(g: ConditionGroup, path: Array, errors: Array) -> void:
	if not ConditionGroup.is_known_logic(g.logic):
		# 손상된 enum. ADR-008 D3의 malformed-tree fail-closed를 위해 거부한다
		# (StateSchema의 value_type_invalid/lifetime_invalid, StateCondition의 operator_invalid와 대칭).
		errors.append(_error("logic_invalid", path, &"",
			"unknown logic enum %d" % g.logic))
		return

	if g.logic == ConditionGroup.Logic.NOT:
		if g.children.size() != 1:
			errors.append(_error("not_arity_invalid", path, &"",
				"NOT group requires exactly 1 child, got %d" % g.children.size()))
	elif g.children.is_empty():
		errors.append(_error("group_empty", path, &"",
			"%s group has no children" % ConditionGroup.logic_to_string(g.logic)))


# --- 헬퍼 -------------------------------------------------------------

static func _is_supported_expected_type(t: int) -> bool:
	return SUPPORTED_EXPECTED_TYPES.has(t)


static func _clause_key(clause: ConditionClause) -> StringName:
	if clause is StateCondition:
		return (clause as StateCondition).key
	return &""


static func _frame(clause: ConditionClause, path: Array, depth: int) -> Dictionary:
	return {"clause": clause, "path": path, "depth": depth, "idx": 0, "entered": false}


static func _error(code: String, path: Array, key: StringName, message: String) -> Dictionary:
	return {"code": code, "path": (path as Array).duplicate(), "key": key, "message": message}


static func _result(errors: Array, node_count: int) -> Dictionary:
	var codes: Array[String] = []
	for e in errors:
		codes.append(e["code"])
	var res := {
		"valid": errors.is_empty(),
		"errors": errors,
		"error_codes": codes,
		"node_count": node_count,
	}
	# 계약: 호출별 deep copy 반환. 반환값을 변조해도 다음 호출/입력 Resource에 영향이 없다.
	return res.duplicate(true)
