@tool
extends PopupMenu


func _ready() -> void:
	clear()
	add_item("선택된 노드 삭제", 0)


func _on_id_pressed(id: int) -> void:
	match id :
		0 : 
			selected_nodes_delete()	
			pass
		pass

func selected_nodes_delete() -> void:
	print(1)
	var undo_redo = EditorInterface.get_editor_undo_redo()
	var graphedit = $"../../HSplitContainer/GraphEdit"

	undo_redo.create_action("selected_nodes_delete")
	var selected_nodes : Array
	for elem in graphedit.get_children():
		var graphelem = elem as dialogue_node
		if graphelem.selected and graphelem.definition is not StartDef:
			selected_nodes.append(graphelem)
	
	for node in selected_nodes:
		undo_redo.add_do_method(graphedit, "remove_child", node)
		undo_redo.add_undo_method(graphedit, "add_child", node)
		var connections : Array = graphedit.get_connections_for_node(node)
		if connections.size() > 0 :
			for connection in connections :
				undo_redo.add_undo_method(graphedit, "connect_node", connection["from_node"], connection["from_port"], connection["to_node"], connection["to_port"])

	undo_redo.commit_action()		
	
