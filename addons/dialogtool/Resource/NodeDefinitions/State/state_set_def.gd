@tool
class_name StateSetDef extends StateEffectDef

# State Set 비대기 Effect (DT-009 Step 3, ADR-010 D7).
# key state를 literal 값으로 설정한다. 지원 타입은 Store 허용 5타입(bool/int/float/String/StringName).
# 런타임은 DialoguePlayer가 mutation provider.apply_state_batch([{key, value}])로 실행한다(Step 2).
# 노드 목록 표시 이름: "StateSet" (get_global_name().left(-3)).

# value_type은 Variant.Type(TYPE_BOOL/INT/FLOAT/STRING/STRING_NAME 중 하나). value의 typeof를 결정한다.
@export var value_type: int = TYPE_INT
@export var value: Variant = 0


func get_runtime_type() -> StringName:
	return &"state_set"


func get_runtime_params() -> Dictionary:
	# 값을 변환하지 않고 그대로 넘긴다(D7 — typeof는 capture/.tres가 보존). 타입 불일치(손상 .tres)는
	# Store가 strict하게 type_mismatch로 거부한다(조용한 변환 금지, ADR-010).
	return {
		"key": key,
		"value": value,
	}


# 잘못된 literal(예: INT에 "abc" → String)을 저장 검증에서 차단한다.
func validate_literal() -> String:
	return literal_error(value, value_type, SET_VALUE_TYPES, "value")


func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {"key": key, "value_type": value_type, "value": value})


func _capture(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	if params.has("key"):
		key = params["key"]
	if params.has("value_type"):
		value_type = params["value_type"]
	if params.has("value"):
		value = params["value"]
