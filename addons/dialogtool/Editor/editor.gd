@tool
extends GraphEdit

# PopupMenu 노드를 담을 변수
var _popup_menu: PopupMenu

func _ready() -> void:
	connection_request.connect(_on_connection_request)
	
	
func _on_connection_request(from_node_name: StringName, from_port: int, to_node_name: StringName, to_port: int) -> void:
	#connect_node(from_node_name, from_port, to_node_name, to_port)
	var undo_redo = EditorInterface.get_editor_undo_redo()
	undo_redo.create_action("Connect Nodes")
	undo_redo.add_do_method(self, "connect_node", from_node_name, from_port, to_node_name, to_port)
	undo_redo.add_undo_method(self, "disconnect_node", from_node_name, from_port, to_node_name, to_port)
	undo_redo.commit_action()	

func _can_drop_data(at_position: Vector2, data: Variant) -> bool:
	return data is NodeDefinition

func _drop_data(at_position: Vector2, data: Variant) -> void:
	var definition = data
	var node = load("res://addons/dialogtool/Node/dialogue_node.tscn").instantiate()
	node.definition = definition
	var viewposition = (at_position + scroll_offset) / zoom
	node.position_offset = viewposition

	var undo_redo = EditorInterface.get_editor_undo_redo()
	undo_redo.create_action("Add Dialogue Node")
	undo_redo.add_do_method(self, "add_child", node)
#	undo_redo.add_do_property(node, "owner", get_tree().edited_scene_root)
	undo_redo.add_undo_method(self, "remove_child", node)
	
	undo_redo.commit_action()

func get_connections_for_node(node: GraphNode) -> Array:
	var results = []
	for connection in get_connection_list():
		if connection.from_node == node.name or connection.to_node == node.name:
			results.append(connection)
	return results
