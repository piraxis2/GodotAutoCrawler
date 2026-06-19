@tool
extends PopupMenu

@onready var editor: GraphEdit = $"../VSplitContainer/HSplitContainer/GraphEdit"
var connections: Dictionary
func popup_context_box(parent_rect: Rect2i, closest_connections: Dictionary) -> void:
	super.popup_on_parent(parent_rect)
	connections = closest_connections
	set_item_disabled(0, (connections == null or connections.is_empty()))


func _on_index_pressed(index: int) -> void:
	match index:
		0:
			if Engine.is_editor_hint():
				var undo_redo = EditorInterface.get_editor_undo_redo()
				undo_redo.create_action("Disconnect Nodes")
				undo_redo.add_do_method(editor, "disconnect_graph_node", connections["from_node"], connections["from_port"], connections["to_node"], connections["to_port"])
				undo_redo.add_undo_method(editor, "connect_node", connections["from_node"], connections["from_port"], connections["to_node"], connections["to_port"])
				undo_redo.commit_action()
			else:
				editor.disconnect_graph_node(connections["from_node"], connections["from_port"], connections["to_node"], connections["to_port"])
				
		2:
			var desc_def: DialogueDefinition = load("res://addons/world_core/dialogtool/Resource/NodeDefinitions/description_def.gd").new()
			var node                         = load(desc_def._get_dialogue_node()).instantiate()
			desc_def.node_id = editor._next_id
			node.definition = desc_def
			var viewposition = (get_meta("at_position") + editor.scroll_offset) / editor.zoom
			node.position_offset = viewposition
			node.id = editor._next_id
			node.name = str(node.id)
			
			if Engine.is_editor_hint():
				editor.add_dialogue_node(node)
			else:
				editor._next_id += 1
				editor.add_child(node)
				node.set_owner(editor)
			
		4:
			editor.call_deferred("reset_camera")
			
