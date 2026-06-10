@tool
@abstract
class_name DialogueDefinition extends Resource

@export var node_id: int = -1
var graph_resource: WeakRef 

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/dialogue_node.tscn"


func get_graph_resource() -> DialogueGraphResource:
	if graph_resource == null:
		return null

	return graph_resource.get_ref()


func get_runtime_type() -> StringName:
	return &"unknown"


func get_runtime_params() -> Dictionary:
	return {}
	
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
