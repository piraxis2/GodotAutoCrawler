@tool
@abstract class_name PortraitDef extends FlowDefinition

# Portrait Flow 명령(show/hide/expression)의 공통 베이스.
# 세 노드는 동일한 직렬화 필드와 runtime params 형태를 공유하고, runtime type만 다르다.
# 실제 비대기 실행 규칙은 DialoguePlayer가 runtime snapshot의 type으로 처리한다(DT-002 Step 1).
#
# Abstract/ 폴더에 두어 노드 목록 검색(dialogue_node_item_list.gd)에서 제외한다.
# 에디터 UI는 Definition이 직접 만들지 않고 Editor Adapter에 위임한다(ADR-002).
#
# 필드 계약(ADR-004): texture_path가 MVP의 1차 이미지 식별자이며,
# actor/expression은 향후 resolver용 메타데이터다. transition 기본값은 "none".

@export var slot: String = "center"
@export var texture_path: String = ""
@export var actor: String = ""
@export var expression: String = ""
@export var transition: String = "none"


func get_runtime_params() -> Dictionary:
	return {
		"slot": slot,
		"texture_path": texture_path,
		"actor": actor,
		"expression": expression,
		"transition": transition,
	}


# Portrait 명령은 비대기다. 런타임 실행은 snapshot type 기반으로 DialoguePlayer가 하므로
# (Step 1) 레거시 execute 경로는 사용하지 않는다. FlowDefinition 추상 메서드만 만족시킨다.
func _is_done() -> bool:
	return true


func execute(_dialogue_player: Node) -> FlowDefinition:
	return null


func _node_init(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	adapter.apply_params(node, get_runtime_params())


# 어댑터가 실제로 캡처한 키만 갱신한다. UI에 노출되지 않은 필드(예: Hide의
# texture_path/actor/expression)는 기존 Definition 값을 그대로 보존한다.
func _capture(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	if params.has("slot"):
		slot = params["slot"]
	if params.has("texture_path"):
		texture_path = params["texture_path"]
	if params.has("actor"):
		actor = params["actor"]
	if params.has("expression"):
		expression = params["expression"]
	if params.has("transition"):
		transition = params["transition"]
