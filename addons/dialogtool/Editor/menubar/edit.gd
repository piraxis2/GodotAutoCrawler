@tool
extends PopupMenu

@onready var graph_edit = $"../../../HSplitContainer/GraphEdit" 

func _ready() -> void:
	clear()
	add_item("선택된 노드 삭제", 0)
	add_item("초기화", 1)


func _on_id_pressed(id: int) -> void:
	match id :
		0 : 
			selected_nodes_delete()	
			pass
		1 :
			var undo_redo = EditorInterface.get_editor_undo_redo()
			undo_redo.create_action("reset_graph_edit")
			undo_redo.add_do_method(self, "reset_graph_edit")
			var graph =	graph_edit.capture_current_graphedit()
			undo_redo.add_undo_method(graph_edit, "load_resource", graph)
			undo_redo.commit_action()
			pass

func selected_nodes_delete() -> void:
	var undo_redo = EditorInterface.get_editor_undo_redo()
	

	undo_redo.create_action("selected_nodes_delete")
	var selected_nodes : Array
	for elem in graph_edit.get_children():
		if elem is not DialogueNode:
			continue
			
		var graphelem = elem as DialogueNode
			
		if graphelem.selected and graphelem.definition is not StartDef:
			selected_nodes.append(graphelem)
	
	for node in selected_nodes:
		undo_redo.add_do_method(graph_edit, "remove_child", node)
		undo_redo.add_undo_method(graph_edit, "add_child", node)
		var connections : Array = graph_edit.get_connections_for_node(node)
		if connections.size() > 0 :
			for connection in connections :
				undo_redo.add_undo_method(graph_edit, "connect_node", connection["from_node"], connection["from_port"], connection["to_node"], connection["to_port"])

	undo_redo.commit_action()		
	
func reset_graph_edit() -> void:
	graph_edit.clear_connections()
	for node in graph_edit.get_children():
		if node is DialogueNode:
			if node.definition is StartDef:
				node.position_offset = graph_edit._start_node_position
				continue
			node.free()
	
