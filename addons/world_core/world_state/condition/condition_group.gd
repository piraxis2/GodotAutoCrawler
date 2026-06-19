@tool
class_name ConditionGroup extends ConditionClause
## 조건 트리의 recursive boolean group (DT-007).
##
## logic으로 children을 결합한다:
## - ALL: 모든 child가 통과해야 통과(비어 있으면 authoring 오류 group_empty).
## - ANY: 하나 이상 통과하면 통과(비어 있으면 authoring 오류 group_empty).
## - NOT: unary. child가 정확히 1개여야 한다(아니면 not_arity_invalid). child 결과를 부정한다.
##
## children 순서는 저장 순서를 따르며 평가/trace 순서의 안정 계약이다(ADR-008 D4).
## 이 Resource는 데이터만 보관한다. 실제 평가는 Step 2 ConditionEvaluator의 책임이다.

enum Logic { ALL, ANY, NOT }

@export var logic: Logic
@export var children: Array[ConditionClause] = []


## logic enum이 지원되는 범위 안에 있는가.
static func is_known_logic(lg: int) -> bool:
	return lg >= Logic.ALL and lg <= Logic.NOT


## logic -> 안정 trace 문자열(ADR-008 D4의 안정 계약). 알 수 없으면 "unknown".
static func logic_to_string(lg: int) -> String:
	match lg:
		Logic.ALL: return "all"
		Logic.ANY: return "any"
		Logic.NOT: return "not"
		_: return "unknown"
