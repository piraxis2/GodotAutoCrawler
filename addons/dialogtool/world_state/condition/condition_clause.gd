@tool
@abstract
class_name ConditionClause extends Resource
## 조건 트리의 공통 base Resource (DT-007).
##
## 직접 인스턴스화할 수 없다(@abstract, Godot 4.6). 구체 타입만 트리에 들어간다:
## - StateCondition: leaf. provider의 한 state key와 literal expected_value 비교.
## - ConditionGroup: recursive boolean group(ALL/ANY/NOT).
##
## base를 비인스턴스화로 두어 "어떤 clause인지 모르는" 노드가 authoring에서 생기지 않게 한다.
## malformed `.tres`가 알 수 없는 clause를 만들 경우는 ConditionValidator의 clause_unknown으로 막는다.
##
## 이 클래스는 순수 데이터다. 평가/검증 로직과 provider/UI를 알지 않는다(ADR-008 D1/D2).
