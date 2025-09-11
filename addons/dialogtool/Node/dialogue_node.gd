@tool
extends GraphNode
class_name DialogueNode

@export var definition : NodeDefinition
var delete_button: DeleteButton

var id: int = -1

enum port_type { flow, data }

var port_info: Dictionary


func _ready() -> void:
	delete_button = load("res://addons/dialogtool/Node/Sub/DeleteButton.tscn").instantiate()
	add_child(delete_button)
	move_child(delete_button, 0)
	delete_button.on_click_delete.connect(_on_delete_button_pressed)
	
	if definition:
		clear_all_slots()
		definition._node_init(self)
		title = str(definition.get_script().get_global_name()).left(-3)

func _on_delete_button_pressed() -> void:
	print(name)
	var undo_redo = EditorInterface.get_editor_undo_redo()
	undo_redo.create_action("delete node")
	undo_redo.add_do_method(get_parent(), "remove_child", self)
	undo_redo.add_undo_method(get_parent(), "add_child", self)
	var connections : Array = get_parent().get_connections_for_node(self)
	if connections.size() > 0 :
		for connection in connections :
			undo_redo.add_undo_method(get_parent(), "connect_node", connection["from_node"], connection["from_port"], connection["to_node"], connection["to_port"])
	undo_redo.commit_action()

func update_port() -> void:
	clear_all_slots()
	for info_idx in port_info:
		
		var left_enable = false
		var left_port_type = 0
		var left_color = Color.WHITE
		var right_enable = false
		var right_port_type = 0
		var right_color = Color.WHITE
		if port_info[info_idx].has("left"):
			left_enable = true
			left_port_type = port_info[info_idx]["left"]["port_type"]
			left_color = port_info[info_idx]["left"]["color"]
		if port_info[info_idx].has("right"):
			right_enable = true
			right_port_type = port_info[info_idx]["right"]["port_type"]
			right_color = port_info[info_idx]["right"]["color"]
			
		set_slot(info_idx, left_enable, left_port_type, left_color, right_enable, right_port_type, right_color) 

func add_left_port_info(idx: int, type: port_type, color: Color) -> void:
	if port_info.has(idx):
		port_info[idx].get_or_add("left", {"port_type": type, "color": color})
	else:
		port_info.get_or_add(idx, {"left": {"port_type": type, "color": color}})
	
	update_port()

func remove_left_port_info(idx: int) -> void:
	if port_info.has(idx):
		port_info[idx].erase("left")
		update_port()

func add_right_port_info(idx: int, type: port_type, color: Color) -> void:
	if port_info.has(idx):
		port_info[idx].get_or_add("right", {"port_type": type, "color": color})
	else:
		port_info.get_or_add(idx, {"right": {"port_type": type, "color": color}})
	
	update_port()
	
func remove_right_port_info(idx: int) -> void:
	if port_info.has(idx):
		port_info[idx].erase("right")
		update_port()
	
