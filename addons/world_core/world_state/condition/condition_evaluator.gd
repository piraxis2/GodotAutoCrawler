@tool
class_name ConditionEvaluator extends RefCounted
## ConditionSet의 pure-read 평가기 (DT-007 Step 2, ADR-008 D2/D3/D4).
##
## 2단계 평가:
##  1) ConditionValidator로 구조 검증한다. 실패하면 provider를 한 번도 읽지 않고
##     (`read_count == 0`) 구조 오류를 그대로 반환한다.
##  2) 통과하면 주입된 read provider(`has_state`/`read_state`)만으로 트리를 평가한다.
##
## 정책:
## - mutation API/signal/save/UI/autoload를 모른다. 주입 provider의 read 두 메서드만 쓴다.
## - non-short-circuit: 모든 leaf를 저장된 child 순서로 평가해 전체 trace를 남긴다.
## - 같은 key는 한 evaluation 안에서 한 번만 읽고 cache를 재사용한다(miss도 cache).
## - fail-closed: provider/state/type 오류는 모두 `errors[]`에 적재되어 `valid=false`,
##   `passed=false`다. errored child는 NOT/ANY에서도 pass로 바뀌지 않는다(errored 전파).
## - 결정론: child 저장 순서를 따른다. operator/logic trace 문자열은 안정 계약이다.
## - report/trace/errors는 호출별 deep copy다. Condition Resource와 provider 값을 수정하지 않는다.
##
## 비교 규칙(strict, ADR-008 D3 / Resolution 5):
## - 양쪽 `typeof()`가 정확히 일치해야 한다. int↔float, String↔StringName 암시적 변환은 없다.
## - expected/operator의 정적 타당성은 1단계(ConditionValidator)에서 이미 보장된다.
##   여기서는 provider가 준 actual 타입이 expected 타입과 일치하는지만 본다.

const SUPPORTED_TYPES: Array[int] = ConditionValidator.SUPPORTED_EXPECTED_TYPES


## ConditionSet을 주입 read provider로 평가한다.
##
## 반환(호출별 deep copy):
## {
##   "passed": bool,                # valid && root 논리 결과
##   "valid": bool,                 # errors.is_empty()
##   "errors": Array[Dictionary],   # {code, path, key, message}
##   "trace": Dictionary,           # 노드 트리. 구조 거부 시 {}.
##   "read_count": int,             # 실제로 읽은 unique key 수(miss 포함, 중복 probe 없음)
## }
##
## read_provider는 `has_state(StringName)->bool`와 `read_state(StringName)->Variant`를 제공해야 한다.
## null이거나 계약 메서드가 없으면 provider_missing/provider_contract_invalid로 fail-closed한다.
static func evaluate(condition_set: ConditionSet, read_provider) -> Dictionary:
	# 1단계: 구조 검증(값 읽기 없음). 실패하면 즉시 구조 거부 — read_count == 0.
	var structural := ConditionValidator.validate(condition_set)
	if not structural["valid"]:
		return _deep_copy({
			"passed": false,
			"valid": false,
			"errors": structural["errors"],
			"trace": {},
			"read_count": 0,
		})

	# 2단계: 평가 context(호출별).
	var ctx := {
		"provider": read_provider,
		"provider_valid": true,
		"provider_error": "",
		"cache": {},        # key -> {has: bool, value: Variant}
		"errors": [] as Array[Dictionary],
		"read_count": 0,
	}

	# provider 계약 사전 검사. 실패해도 트리를 걸어 전체 trace를 남기되 어떤 값도 읽지 않는다.
	# 비-Object/freed/메서드 누락/잘못된 arity/잘못된 has_state 반환 타입을 모두 fail-closed로 막아
	# 평가 중 SCRIPT ERROR가 나지 않게 한다.
	if read_provider == null:
		ctx["provider_valid"] = false
		ctx["provider_error"] = "provider_missing"
		ctx["errors"].append(_error("provider_missing", [], &"", "read_provider is null"))
	else:
		var contract_error := _read_provider_contract_error(read_provider)
		if not contract_error.is_empty():
			ctx["provider_valid"] = false
			ctx["provider_error"] = "provider_contract_invalid"
			ctx["errors"].append(_error("provider_contract_invalid", [], &"", contract_error))

	var root_result := _eval_node(condition_set.root, [], ctx)

	var valid: bool = ctx["errors"].is_empty()
	var passed: bool = valid and root_result["passed"]
	return _deep_copy({
		"passed": passed,
		"valid": valid,
		"errors": ctx["errors"],
		"trace": root_result["trace"],
		"read_count": ctx["read_count"],
	})


# --- 노드 평가 -------------------------------------------------------
# 구조 검증을 통과한 트리만 평가한다(cycle 없음, depth<=64). 따라서 재귀가 안전하다.
# 반환: {trace: Dictionary, passed: bool, errored: bool}. errored면 passed는 항상 false.

static func _eval_node(clause: ConditionClause, path: Array, ctx: Dictionary) -> Dictionary:
	if clause is ConditionGroup:
		return _eval_group(clause as ConditionGroup, path, ctx)
	# 구조 검증이 통과했으므로 leaf는 StateCondition이다.
	return _eval_leaf(clause as StateCondition, path, ctx)


static func _eval_leaf(sc: StateCondition, path: Array, ctx: Dictionary) -> Dictionary:
	var op_str := StateCondition.operator_to_string(sc.operator)
	var expected: Variant = sc.expected_value

	# provider 무효: 어떤 값도 읽지 않고 leaf를 errored로 둔다(전역 provider 오류는 이미 적재됨).
	if not ctx["provider_valid"]:
		return _leaf_error_node(sc, path, op_str, expected, ctx["provider_error"])

	# evaluation-local cache. 같은 key는 한 번만 probe/read 한다(miss도 cache).
	# read_count 정책: provider를 호출한 unique key는 결과가 정상이든 contract 위반이든 1회로 친다
	# (호출 자체가 read 시도다). 같은 key는 cache로 재-probe하지 않는다.
	var key: StringName = sc.key
	var entry: Dictionary
	if ctx["cache"].has(key):
		entry = ctx["cache"][key]
	else:
		# has_state 반환을 Variant로 받아 런타임 타입을 확인한다(미선언/Variant 반환이 non-bool을
		# 돌려줘도 암시적 truthy로 새지 않게 한다). arg 타입과 arity는 사전 검사가 보장한다.
		var has_raw: Variant = ctx["provider"].has_state(key)
		if typeof(has_raw) != TYPE_BOOL:
			entry = {"contract_error": true, "ret_type": typeof(has_raw)}
		else:
			var val: Variant = null
			if has_raw:
				val = ctx["provider"].read_state(key)
			entry = {"has": has_raw, "value": val}
		ctx["cache"][key] = entry
		ctx["read_count"] = int(ctx["read_count"]) + 1

	if entry.get("contract_error", false):
		ctx["errors"].append(_error("provider_contract_invalid", path, key,
			"has_state('%s') returned non-bool type %d at runtime" % [key, entry["ret_type"]]))
		return _leaf_error_node(sc, path, op_str, expected, "provider_contract_invalid")

	if not entry["has"]:
		ctx["errors"].append(_error("state_missing", path, key,
			"state '%s' is not present in provider" % key))
		return _leaf_error_node(sc, path, op_str, expected, "state_missing")

	var actual: Variant = entry["value"]
	if typeof(actual) != typeof(expected):
		ctx["errors"].append(_error("actual_type_mismatch", path, key,
			"actual type %d does not match expected type %d for key '%s'"
				% [typeof(actual), typeof(expected), key]))
		return _leaf_error_node(sc, path, op_str, expected, "actual_type_mismatch")

	var passed := _compare(sc.operator, actual, expected)
	var node := {
		"kind": "state",
		"path": (path as Array).duplicate(),
		"key": key,
		"operator": op_str,
		"expected": expected,
		"actual": actual,
		"passed": passed,
	}
	return {"trace": node, "passed": passed, "errored": false}


static func _eval_group(g: ConditionGroup, path: Array, ctx: Dictionary) -> Dictionary:
	var child_traces: Array = []
	var child_results: Array = []
	var any_errored := false
	for i in g.children.size():
		var child_path: Array = (path as Array).duplicate()
		child_path.append(i)
		var cr := _eval_node(g.children[i], child_path, ctx)
		child_traces.append(cr["trace"])
		child_results.append(cr)
		if cr["errored"]:
			any_errored = true

	var errored := any_errored
	var passed := false
	if not errored:
		# 정상 child만 있을 때만 논리를 계산한다. errored child는 pass로 바뀌지 않는다.
		match g.logic:
			ConditionGroup.Logic.ALL:
				passed = true
				for cr in child_results:
					if not cr["passed"]:
						passed = false
			ConditionGroup.Logic.ANY:
				passed = false
				for cr in child_results:
					if cr["passed"]:
						passed = true
			ConditionGroup.Logic.NOT:
				# arity 1은 구조 검증이 보장한다.
				passed = not bool(child_results[0]["passed"])

	var node := {
		"kind": "group",
		"path": (path as Array).duplicate(),
		"logic": ConditionGroup.logic_to_string(g.logic),
		"passed": passed,
		"children": child_traces,
	}
	return {"trace": node, "passed": passed, "errored": errored}


# 평가 불가 leaf의 trace 노드(actual:null, error 코드 포함). passed=false, errored=true.
static func _leaf_error_node(sc: StateCondition, path: Array, op_str: String,
		expected: Variant, error_code: String) -> Dictionary:
	var node := {
		"kind": "state",
		"path": (path as Array).duplicate(),
		"key": sc.key,
		"operator": op_str,
		"expected": expected,
		"actual": null,
		"passed": false,
		"error": error_code,
	}
	return {"trace": node, "passed": false, "errored": true}


static func _compare(op: int, a: Variant, b: Variant) -> bool:
	# 호출 시점에 typeof(a) == typeof(b)가 보장된다(strict).
	match op:
		StateCondition.Operator.EQUAL: return a == b
		StateCondition.Operator.NOT_EQUAL: return a != b
		StateCondition.Operator.LESS: return a < b
		StateCondition.Operator.LESS_EQUAL: return a <= b
		StateCondition.Operator.GREATER: return a > b
		StateCondition.Operator.GREATER_EQUAL: return a >= b
		_: return false


# --- 헬퍼 -------------------------------------------------------------

static func _error(code: String, path: Array, key: StringName, message: String) -> Dictionary:
	return {"code": code, "path": (path as Array).duplicate(), "key": key, "message": message}


# read provider가 계약(has_state/read_state)을 만족하는지 검사한다.
# 만족하면 "" 반환, 아니면 거부 사유 문자열 반환. provider 메서드를 호출하지 않고 reflection만 쓴다
# (호출하면 read_count/call-count 의미가 흔들리고, 잘못된 provider에서 SCRIPT ERROR가 날 수 있다).
static func _read_provider_contract_error(p: Variant) -> String:
	if typeof(p) != TYPE_OBJECT:
		return "read_provider must be an Object, got type %d" % typeof(p)
	var obj := p as Object
	if not is_instance_valid(obj):
		return "read_provider is a freed/invalid Object"
	for m: StringName in [&"has_state", &"read_state"]:
		if not obj.has_method(m):
			return "read_provider is missing method '%s(key)'" % m
		var info := _method_info(obj, m)
		if info.is_empty():
			return "read_provider method '%s' is not introspectable" % m
		if not _accepts_single_arg(info):
			return "read_provider method '%s' must accept exactly one key argument" % m
		if not _first_arg_type_ok(info):
			return "read_provider method '%s' first argument must be StringName or untyped" % m
	# has_state 선언 반환 타입: bool은 통과, 구체적 비-bool(예: -> int)은 거부.
	# 미선언/Variant 반환은 정적 판단 불가라 통과시키되, 실제 반환값 타입은 _eval_leaf가 런타임에
	# bool인지 확인한다(non-bool이면 provider_contract_invalid).
	if not _returns_bool_or_untyped(_method_info(obj, &"has_state")):
		return "read_provider 'has_state' must return bool"
	return ""


static func _method_info(obj: Object, method_name: StringName) -> Dictionary:
	for m in obj.get_method_list():
		if m.get("name", &"") == method_name:
			return m
	return {}


# 메서드를 정확히 1개의 positional 인자로 호출할 수 있는가(required<=1<=total).
static func _accepts_single_arg(info: Dictionary) -> bool:
	var args: Array = info.get("args", [])
	var defaults: Array = info.get("default_args", [])
	var total := args.size()
	var required := total - defaults.size()
	return total >= 1 and required <= 1


# 첫 인자 타입이 StringName이거나 미선언/Variant인가. int 등 구체적 비-StringName 타입은 거부한다
# (StringName key를 넘기면 호출 시 SCRIPT ERROR가 나기 때문).
static func _first_arg_type_ok(info: Dictionary) -> bool:
	var args: Array = info.get("args", [])
	if args.is_empty():
		return false
	var t := int(args[0].get("type", TYPE_NIL))
	return t == TYPE_STRING_NAME or t == TYPE_NIL


# 선언된 반환 타입이 bool이거나(검증 통과) 미선언/Variant(정적 판단 불가, 허용)면 true.
# int 등 구체적인 비-bool 타입을 선언했으면 false.
static func _returns_bool_or_untyped(info: Dictionary) -> bool:
	if info.is_empty():
		return false
	var ret: Dictionary = info.get("return", {})
	var rtype := int(ret.get("type", TYPE_NIL))
	return rtype == TYPE_BOOL or rtype == TYPE_NIL


static func _deep_copy(d: Dictionary) -> Dictionary:
	return d.duplicate(true)
