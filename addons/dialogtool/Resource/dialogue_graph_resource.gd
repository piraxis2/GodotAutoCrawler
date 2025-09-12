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
	start_node_id = -1
	next_node_id = 1
	

	
	
	
