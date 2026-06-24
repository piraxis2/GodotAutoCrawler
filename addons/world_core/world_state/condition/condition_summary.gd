@tool
class_name ConditionSummary extends RefCounted
## ConditionSet/ConditionClause를 provider 없이 사람이 읽을 수 있는 문자열로 요약한다 (DT-012 Step 1).
##
## 책임:
## - validate-first. 먼저 ConditionValidator.validate(condition_set)를 호출하고, null/invalid면 트리를
##   순회하지 않고 명시적 invalid/null summary를 반환한다(손상 트리 naive 순회 금지).
## - structural valid일 때만 트리를 순회해 leaf/group summary를 만든다. validator가 depth/node/cycle/alias/
##   group_empty/not_arity 불변식을 이미 강제했으므로, 여기 순회는 DEPTH_LIMIT(64) 안으로 한정된다.
## - editor UX 표시 문자열만 만든다. ADR-008 trace 안정 문자열(`greater_equal`, `all` 등)을 표시에 재사용하지
##   않고, 표시 전용 operator 기호/logic 라벨 맵을 둔다.
## - strict literal 표기를 보존한다(INT/FLOAT, String/StringName, bool 구분).
##
## 비책임:
## - provider read/mutation(이 클래스는 provider를 알지 않는다, read_count==0).
## - 실제 true/false 평가(Step 2 ConditionEvaluator).
## - UI 위젯 갱신/레이아웃(DT-012 Step 2 WorldStateConditionNode).

# 노드 summary의 기본 최대 표시 길이. full_summary는 잘리지 않은 전체를 보존한다.
const DEFAULT_MAX_LENGTH := 80

# condition_set 자체가 null일 때의 표시 문자열(root_null 등 다른 invalid와 구분).
const NO_CONDITION_SET := "No ConditionSet"

# 표시 전용 ellipsis(잘린 summary 표시).
const ELLIPSIS := "…"


## ConditionSet을 사람이 읽을 수 있는 summary로 요약한다.
##
## options:
##   "max_length": int  # summary 잘림 한계(기본 DEFAULT_MAX_LENGTH). full_summary는 항상 전체.
##
## 반환(호출마다 새 copy):
## {
##   "valid": bool,                 # structural validation 통과 여부
##   "summary": String,             # 노드 표시용(잘릴 수 있음)
##   "full_summary": String,        # 잘리지 않은 전체 구조/표시 요약
##   "tooltip": String,             # full text + 오류/설명 병기
##   "error_codes": Array[String],  # validator error code 목록(대표 code는 [0])
##   "errors": Array[Dictionary],   # validator {code,path,key,message}
## }
##
## 정책:
## - condition_set == null              -> "No ConditionSet" (invalid)
## - structural invalid(root_null/cycle/alias/depth/node/group_empty/not_arity/leaf 오류 등)
##                                       -> "Invalid: <대표 code>" (구조 요약 금지)
## - structural valid + description 있음 -> description 우선(summary), full_summary는 구조 요약
## - structural valid + description 없음 -> 구조 요약을 summary/full_summary로
static func summarize(condition_set: ConditionSet, options := {}) -> Dictionary:
	var max_length := int(options.get("max_length", DEFAULT_MAX_LENGTH))

	# validate-first. invalid/null 경로는 트리를 순회하지 않는다.
	var result := ConditionValidator.validate(condition_set)
	var error_codes: Array = result.get("error_codes", [])
	var errors: Array = result.get("errors", [])

	if condition_set == null:
		return _make(NO_CONDITION_SET, NO_CONDITION_SET, NO_CONDITION_SET, false,
			error_codes, errors, max_length)

	if not bool(result.get("valid", false)):
		var code := String(error_codes[0]) if not error_codes.is_empty() else "unknown"
		var inv := "Invalid: %s" % code
		return _make(inv, inv, _invalid_tooltip(errors), false, error_codes, errors, max_length)

	# valid: validator가 구조 불변식을 보장했으므로 bounded recursion이 안전하다.
	var structural := _format_clause(condition_set.root)
	var desc := condition_set.description.strip_edges()
	var primary := desc if not desc.is_empty() else structural
	var tooltip := structural if desc.is_empty() else "%s\n\n%s" % [desc, structural]
	return _make(primary, structural, tooltip, true, error_codes, errors, max_length)


# --- 트리 포맷(valid 트리에서만 호출) ---------------------------------

static func _format_clause(clause: ConditionClause) -> String:
	if clause is StateCondition:
		var sc := clause as StateCondition
		return "%s %s %s" % [
			String(sc.key), _operator_symbol(sc.operator), _format_literal(sc.expected_value)]
	if clause is ConditionGroup:
		var g := clause as ConditionGroup
		var parts: Array[String] = []
		# children 순서 보존(ADR-008 D4 저장 순서).
		for child in g.children:
			parts.append(_format_clause(child))
		return "%s(%s)" % [_logic_label(g.logic), ", ".join(parts)]
	# validate-first 계약상 도달하지 않지만, 방어적으로 표시한다.
	return "<?>"


# 표시 전용 operator 기호 맵. trace 문자열(operator_to_string)을 재사용하지 않는다.
static func _operator_symbol(op: int) -> String:
	match op:
		StateCondition.Operator.EQUAL: return "=="
		StateCondition.Operator.NOT_EQUAL: return "!="
		StateCondition.Operator.LESS: return "<"
		StateCondition.Operator.LESS_EQUAL: return "<="
		StateCondition.Operator.GREATER: return ">"
		StateCondition.Operator.GREATER_EQUAL: return ">="
		_: return "?"


# 표시 전용 logic 라벨 맵. trace 문자열(logic_to_string)을 재사용하지 않는다.
static func _logic_label(lg: int) -> String:
	match lg:
		ConditionGroup.Logic.ALL: return "ALL"
		ConditionGroup.Logic.ANY: return "ANY"
		ConditionGroup.Logic.NOT: return "NOT"
		_: return "?"


# strict literal 표기. typeof 차이를 숨기지 않는다.
static func _format_literal(value: Variant) -> String:
	match typeof(value):
		TYPE_BOOL:
			return "true" if value else "false"
		TYPE_INT:
			return str(value)
		TYPE_FLOAT:
			return _format_float(value)
		TYPE_STRING:
			return "\"%s\"" % _escape_string(value)
		TYPE_STRING_NAME:
			return "&\"%s\"" % _escape_string(String(value))
		_:
			# validate-first가 미지원 타입을 invalid로 막으므로 도달하지 않는다.
			return "<?>"


# 문자열 literal을 단일 행/모호하지 않게 표기하기 위한 escape.
# 따옴표/백슬래시/제어문자가 들어가도 summary가 깨지거나 여러 줄로 번지지 않게 한다.
# (Step 2에서 이 문자열이 GraphNode label/tooltip에 그대로 올라간다.)
# 백슬래시를 먼저 치환해 이중 escape를 피한다.
static func _escape_string(s: String) -> String:
	s = s.replace("\\", "\\\\")
	s = s.replace("\"", "\\\"")
	s = s.replace("\n", "\\n")
	s = s.replace("\r", "\\r")
	s = s.replace("\t", "\\t")
	return s


# FLOAT는 정수처럼 보이는 값도 소수점 표기를 강제해 INT와 구분한다.
static func _format_float(value: float) -> String:
	var s := str(value)
	if not ("." in s or "e" in s or "E" in s or "inf" in s or "nan" in s):
		s += ".0"
	return s


# --- 헬퍼 -------------------------------------------------------------

static func _truncate(s: String, limit: int) -> String:
	if limit > 0 and s.length() > limit:
		return s.substr(0, limit - 1) + ELLIPSIS
	return s


static func _invalid_tooltip(errors: Array) -> String:
	var lines: Array[String] = []
	for e in errors:
		lines.append("[%s] %s" % [e.get("code", ""), e.get("message", "")])
	return "\n".join(lines)


static func _make(primary: String, full_summary: String, tooltip: String, valid: bool,
		error_codes: Array, errors: Array, max_length: int) -> Dictionary:
	# 계약: summary는 잘릴 수 있고 full_summary는 전체를 보존한다.
	return {
		"valid": valid,
		"summary": _truncate(primary, max_length),
		"full_summary": full_summary,
		"tooltip": tooltip,
		"error_codes": error_codes.duplicate(),
		"errors": errors.duplicate(true),
	}
