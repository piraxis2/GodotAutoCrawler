@tool
extends Resource
class_name DialogueGraphResource

@export var nodes: Dictionary
@export var connections: Array[Dictionary]
@export var start_node_id: int
@export var next_node_id: int
@export var scene: PackedScene
@export var runtime_nodes: Dictionary
@export var runtime_connections: Array[Dictionary]

var black_board: Dictionary


func _init() -> void:
	nodes = {}
	connections = []
	runtime_nodes = {}
	runtime_connections = []
	start_node_id = 0
	next_node_id = 1
	

func get_connections(elem: DialogueDefinition) -> Array:
	var results = []
	for connection in connections:
		if connection.from_node_id == elem.node_id or connection.to_node_id == elem.node_id:
			results.append(connection)
	return results


func set_runtime_snapshot(editor_nodes: Dictionary, editor_connections: Array[Dictionary]) -> void:
	runtime_nodes = {}
	runtime_connections = editor_connections.duplicate(true)

	for node_id in editor_nodes:
		var node_data = editor_nodes[node_id]
		var definition: DialogueDefinition = node_data.get("definition")
		if definition == null:
			continue

		runtime_nodes[node_id] = {
			"id": node_id,
			"type": definition.get_runtime_type(),
			"params": definition.get_runtime_params(),
		}


func get_runtime_node(node_id: int) -> Dictionary:
	if runtime_nodes.has(node_id):
		return runtime_nodes[node_id]

	if nodes.has(node_id):
		var definition: DialogueDefinition = nodes[node_id].get("definition")
		if definition:
			return {
				"id": node_id,
				"type": definition.get_runtime_type(),
				"params": definition.get_runtime_params(),
			}

	return {}


func get_runtime_start_node_id() -> int:
	if not get_runtime_node(start_node_id).is_empty():
		return start_node_id

	for node_id in runtime_nodes:
		if runtime_nodes[node_id].get("type") == &"start":
			return node_id

	for node_id in nodes:
		var definition: DialogueDefinition = nodes[node_id].get("definition")
		if definition is StartDef:
			return node_id

	return -1


# Effect 연결 식별자(ADR-005). connection 딕셔너리에 kind=="effect"가 있으면
# 실행 커서가 이동하지 않는 비대기 Effect 연결이다. kind가 없거나 다른 값이면
# 기존 Flow/Data 규칙으로 해석한다(이전 리소스 호환).
const CONNECTION_KIND_EFFECT := "effect"

# Effect 연결의 대상으로 허용하는 노드 런타임 타입(화이트리스트, ADR-005).
# 현재는 Portrait 명령만 비대기 Effect로 실행할 수 있다. Say/Choice/Branch/End/Data 등
# wait state를 만들거나 데이터인 노드는 Effect 대상이 될 수 없다.
const EFFECT_TARGET_TYPES: Array = [&"portrait_show", &"portrait_hide", &"portrait_expression"]


static func is_effect_target_type(type: StringName) -> bool:
	return type in EFFECT_TARGET_TYPES


func _is_effect_connection(connection: Dictionary) -> bool:
	return connection.get("kind", "") == CONNECTION_KIND_EFFECT


# 주 Flow 대상 하나를 반환한다. Effect 연결은 실행 커서를 옮기지 않으므로 건너뛴다.
# 같은 포트에 Effect와 Flow가 함께 연결돼 있어도 Flow만 따라간다.
func get_runtime_next_node_id(from_node_id: int, from_port: int = 0) -> int:
	var active_connections = runtime_connections if not runtime_connections.is_empty() else connections
	for connection in active_connections:
		if connection.get("from_node_id") == from_node_id and connection.get("from_port") == from_port:
			if _is_effect_connection(connection):
				continue
			return connection.get("to_node_id", -1)

	return -1


# 한 노드(from_node_id)에서 나가는 Effect 대상들을 저장 순서대로 반환한다.
# Effect는 전용 Effect 출력 포트(별도 port index)로 발행되므로 port로 거르지 않고
# kind=="effect"로만 식별한다. 저장된 연결 순서가 곧 Effect 실행 순서다(ADR-005).
func get_runtime_effect_node_ids(from_node_id: int) -> Array:
	var active_connections = runtime_connections if not runtime_connections.is_empty() else connections
	var results: Array = []
	for connection in active_connections:
		if connection.get("from_node_id") == from_node_id and _is_effect_connection(connection):
			results.append(connection.get("to_node_id", -1))

	return results


func get_runtime_input_node_id(to_node_id: int, to_port: int = 0) -> int:
	var active_connections = runtime_connections if not runtime_connections.is_empty() else connections
	for connection in active_connections:
		if connection.get("to_node_id") == to_node_id and connection.get("to_port") == to_port:
			return connection.get("from_node_id", -1)

	return -1
	
	
func get_flow(flow: FlowDefinition = null) -> FlowDefinition: 
	if flow == null:
		return nodes[start_node_id]["definition"] as FlowDefinition
		
	return flow.get_next_flow()
	
func get_start_flow() -> StartDef:
	for node in nodes:
		if nodes[node]["definition"] is StartDef:
			return nodes[node]["definition"]
	
	return null		
	
	
