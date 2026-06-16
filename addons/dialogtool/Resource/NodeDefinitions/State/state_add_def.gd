@tool
class_name StateAddDef extends StateEffectDef

# State Add 비대기 Effect (DT-009 Step 3, ADR-010 D7).
# key의 숫자 state에 같은 타입의 delta를 더한다. delta_type은 INT 또는 FLOAT만 허용한다(에디터 UI가
# 두 타입만 노출하므로 다른 타입 literal을 만들 수 없다). 런타임은 DialoguePlayer가 mutation
# provider.add_state(key, delta)로 실행하며, Store가 strict 타입/도메인을 강제한다(Step 1/2).
# 노드 목록 표시 이름: "StateAdd" (get_global_name().left(-3)).

# delta_type은 TYPE_INT 또는 TYPE_FLOAT만. delta의 typeof를 결정한다.
@export var delta_type: int = TYPE_INT
@export var delta: Variant = 0


func get_runtime_type() -> StringName:
	return &"state_add"


func get_runtime_params() -> Dictionary:
	# delta를 변환하지 않고 그대로 넘긴다(D7). 타입 불일치/비숫자는 Store가 type_mismatch로 거부한다.
	return {
		"key": key,
		"delta": delta,
	}


# delta는 INT 또는 FLOAT여야 하고 typeof가 delta_type과 일치해야 한다(잘못된 literal 저장 차단).
func validate_literal() -> String:
	return literal_error(delta, delta_type, ADD_VALUE_TYPES, "delta")


func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {"key": key, "delta_type": delta_type, "delta": delta})


func _capture(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	if params.has("key"):
		key = params["key"]
	if params.has("delta_type"):
		delta_type = params["delta_type"]
	if params.has("delta"):
		delta = params["delta"]
