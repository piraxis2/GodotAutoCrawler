@tool
@abstract
class_name NodeDefinition extends Resource

var definition_node: Node
var node_id: int
var position: Vector2
var graph_resource: DialogueGraphResource

func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/dialogue_node.tscn"
	
@abstract func _node_init(node: DialogueNode) -> void
@abstract func _capture() ->void

func get_connected(is_left: bool, port_id: int) -> NodeDefinition:
	var connections = graph_resource.get_connections(self)
	for connect in connections:
		if connect["from_port"] == port_id or connect["to_port"] == port_id:
			var target_node_id = connect["from_node_id"] if is_left else connect["to_node_id"]
			return graph_resource.nodes[target_node_id]["definition"]
	return null
