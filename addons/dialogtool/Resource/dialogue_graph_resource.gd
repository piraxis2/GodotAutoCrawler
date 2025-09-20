@tool
extends Resource
class_name DialogueGraphResource

@export var nodes: Dictionary
@export var connections: Array[Dictionary]
@export var start_node_id: int
@export var next_node_id: int

var black_board: Dictionary


func _init() -> void:
	nodes = {}
	connections = []
	start_node_id = 0
	next_node_id = 1
	

func get_connections(elem: NodeDefinition) -> Array:
	var results = []
	for connection in connections:
		if connection.from_node_id == elem.node_id or connection.to_node_id == elem.node_id:
			results.append(connection)
	return results
	
	
	
