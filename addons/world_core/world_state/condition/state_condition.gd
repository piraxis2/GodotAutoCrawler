@tool
class_name StateCondition extends ConditionClause
## 조건 트리의 leaf (DT-007). provider의 한 state key와 literal expected_value를 비교한다.
##
## 첫 버전은 state-to-literal 비교만 지원한다(state-to-state/time/random은 후속 operand 설계 필요).
## 실제 비교와 provider read는 Step 2 ConditionEvaluator의 책임이다. 이 Resource는 데이터만 보관한다.
##
## 비교 규칙(ADR-008 D3, Step 2에서 강제):
## - EQUAL/NOT_EQUAL: bool/int/float/String/StringName에서 양쪽 typeof() 정확 일치.
## - LESS/LESS_EQUAL/GREATER/GREATER_EQUAL: int 또는 float만(numeric ordered). 양쪽 typeof() 일치.
## - int<->float, String<->StringName 암시적 변환은 하지 않는다.

enum Operator {
	EQUAL,
	NOT_EQUAL,
	LESS,
	LESS_EQUAL,
	GREATER,
	GREATER_EQUAL,
}

# key는 world state read provider에 넘길 등록된 상태 key다(canonical key 문법).
@export var key: StringName
@export var operator: Operator
# literal 비교 대상. bool/int/float/String/StringName만 유효(ConditionValidator가 강제).
@export var expected_value: Variant


## operator enum이 지원되는 범위 안에 있는가.
static func is_known_operator(op: int) -> bool:
	return op >= Operator.EQUAL and op <= Operator.GREATER_EQUAL


## operator가 ordered(숫자 전용) 비교인가.
static func is_ordered_operator(op: int) -> bool:
	return op == Operator.LESS or op == Operator.LESS_EQUAL \
		or op == Operator.GREATER or op == Operator.GREATER_EQUAL


## operator -> 안정 trace 문자열(ADR-008 D4의 안정 계약). 알 수 없으면 "unknown".
static func operator_to_string(op: int) -> String:
	match op:
		Operator.EQUAL: return "equal"
		Operator.NOT_EQUAL: return "not_equal"
		Operator.LESS: return "less"
		Operator.LESS_EQUAL: return "less_equal"
		Operator.GREATER: return "greater"
		Operator.GREATER_EQUAL: return "greater_equal"
		_: return "unknown"
