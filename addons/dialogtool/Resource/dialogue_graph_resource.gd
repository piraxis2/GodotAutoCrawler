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


func get_runtime_next_node_id(from_node_id: int, from_port: int = 0) -> int:
	var active_connections = runtime_connections if not runtime_connections.is_empty() else connections
	for connection in active_connections:
		if connection.get("from_node_id") == from_node_id and connection.get("from_port") == from_port:
			return connection.get("to_node_id", -1)

	return -1


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
	
	
