@tool
@abstract
class_name DialogueDefinition extends Resource

@export var node_id: int = -1
var graph_resource: WeakRef 

func _get_dialogue_node() -> String:
	return "res://addons/world_core/dialogtool/Node/dialogue_node.tscn"


func get_graph_resource() -> DialogueGraphResource:
	if graph_resource == null:
		return null

	return graph_resource.get_ref()


func get_runtime_type() -> StringName:
	return &"unknown"


func get_runtime_params() -> Dictionary:
	return {}


const _ADAPTER_REGISTRY_PATH := "res://addons/world_core/dialogtool/Editor/Adapter/node_type_registry.gd"

# 이 정의 타입의 에디터 어댑터(없으면 null). 에디터 UI 책임을 어댑터로 위임할 때
# _node_init/_capture에서 사용한다.
#
# registry를 class_name 대신 런타임 load()로 가져온다 — 그래야 어댑터/registry
# 스크립트가 (전역 클래스 캐시에) 아직 등록되지 않은 상태여도 DialogueDefinition과
# 그 서브클래스들이 정상적으로 컴파일/인스턴스화된다. 캐시가 낡았을 때 툴 전체가
# 죽는 대신 어댑터만 일시적으로 null이 되어(노드 UI만 빈 상태) graceful하게 degrade.
func get_editor_adapter() -> Variant:
	var registry = load(_ADAPTER_REGISTRY_PATH)
	if registry == null:
		return null
	return registry.get_adapter(get_runtime_type())

@abstract func _node_init(node: DialogueNode) -> void
@abstract func _capture(node: DialogueNode) ->void

func get_connected(is_left: bool, port_id: int) -> DialogueDefinition:
	var graph = get_graph_resource()
	if graph == null:
		return null

	var connections = graph.get_connections(self)
	for connect in connections:
		if connect["from_port"] == port_id or connect["to_port"] == port_id:
			var target_node_id = connect["from_node_id"] if is_left else connect["to_node_id"]
			return graph.nodes[target_node_id]["definition"]
	return null
