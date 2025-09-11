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

func _dialogue_link(data: Dictionary) -> void:
	var not_satisfied: String
	for key in black_board:
		if not data.has(key):
			not_satisfied += " '" + str(key) + "'"
	
	if not not_satisfied.is_empty():
		push_error("Not satisfied with the dialog requirements" + "{" + not_satisfied+ " }")
		free()
		return
	
		
		
	
	
	
