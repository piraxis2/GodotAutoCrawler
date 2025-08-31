@tool
extends GraphNode
class_name dialogue_node

@export var definition : NodeDefinition

@onready var delete_button = $VBoxContainer/HBoxContainer/Panel/DeleteButton
@onready var center_container = $VBoxContainer/CenterContainer

func _ready() -> void:
	if definition:
		definition._node_init(self)
		

func _on_delete_button_pressed() -> void:
	var undo_redo = EditorInterface.get_editor_undo_redo()
	undo_redo.create_action("delete node")
	undo_redo.add_do_method(get_parent(), "remove_child", self)
	undo_redo.add_undo_method(get_parent(), "add_child", self)
	var connections : Array = get_parent().get_connections_for_node(self)
	if connections.size() > 0 :
		for connection in connections :
			undo_redo.add_undo_method(get_parent(), "connect_node", connection["from_node"], connection["from_port"], connection["to_node"], connection["to_port"])
	undo_redo.commit_action()
