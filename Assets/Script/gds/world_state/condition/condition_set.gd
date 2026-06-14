@tool
class_name ConditionSet extends Resource
## 재사용 가능한 top-level 조건 asset (DT-007).
##
## root 하나의 ConditionClause 트리(leaf 또는 group)를 metadata와 함께 묶는다.
## Dialogue/퀘스트/Response Selector가 같은 ConditionSet `.tres`를 공유한다(ADR-008 D1).
##
## 이 Resource는 데이터만 보관한다. 구조 검증은 ConditionValidator(Step 1), 실제 평가는
## Step 2 ConditionEvaluator의 책임이다. 부분 compiled lookup/evaluation 데이터를 보관하지 않는다.

# leaf(StateCondition) 또는 group(ConditionGroup). null이면 root_null 오류.
@export var root: ConditionClause
@export_multiline var description: String
@export var tags: Array[StringName] = []
