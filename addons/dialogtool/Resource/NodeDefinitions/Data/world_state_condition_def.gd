@tool
class_name WorldStateConditionDef extends DataDefinition
## State Condition Data 노드 (DT-008 Step 1, ADR-009 D1).
##
## ConditionSet 하나를 보유하고 boolean output을 내는 Data 노드다. 런타임 평가는 이 Definition이
## 아니라 DialoguePlayer._get_data_value()의 state_condition 분기가 수행한다. DialoguePlayer가
## 주입된 원본 read provider를 ConditionEvaluator.evaluate()에 그대로 전달하고 report.passed를
## boolean Data 값으로 돌려준다(ADR-009 D2: facade 재포장 금지). 이 Definition은 provider를 모른다.
##
## 이름은 DT-007 leaf Resource인 전역 클래스 StateCondition과 충돌하지 않도록
## WorldStateConditionDef를 쓴다(DT-008 Proposed Runtime Contract).
##
## 에디터 GraphNode UI / ResourcePicker / Adapter·NodeTypeRegistry 등록 / 저장·재로드 왕복은
## Step 2 범위다. 이 Step은 런타임 계약(runtime type + params)만 확정한다.

# leaf(StateCondition) 또는 group(ConditionGroup) 트리를 담은 top-level 조건 asset. null이면
# 런타임 evaluator가 condition_set_null로 fail-closed한다(조용한 true 없음).
@export var condition_set: ConditionSet


func get_runtime_type() -> StringName:
	return &"state_condition"


func get_runtime_params() -> Dictionary:
	# 런타임 snapshot에 ConditionSet Resource 참조를 그대로 보존한다. 평가는 DialoguePlayer가
	# 주입 provider로 수행하므로 여기서는 어떤 평가/lookup도 하지 않는다(순수 데이터).
	return {
		"condition_set": condition_set,
	}


func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/world_state_condition_node.tscn"


func _node_init(node: DialogueNode) -> void:
	# 에디터 Adapter가 boolean output 슬롯과 ConditionSet picker를 구성한다(Step 2 등록).
	# 캐시가 낡아 adapter가 null이어도 UI만 비고 graceful degrade(런타임 평가는 영향 없음).
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {"condition_set": condition_set})


func _capture(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	if params.has("condition_set"):
		condition_set = params["condition_set"]


func _get_data_output(port: int) -> Variant:
	# 에디터/Build 미리보기 경로. 런타임 조건 평가는 주입된 read provider가 필요하므로 여기서는
	# 평가하지 않고 null을 반환한다. 실제 boolean 결과는 DialoguePlayer가 런타임에 계산한다.
	return null
