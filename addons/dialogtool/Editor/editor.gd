@tool
extends GraphEdit

var _popup_menu: PopupMenu
var _next_id: int = 1
@onready var _start_node_position: Vector2 = $StartNode.position_offset
@onready var _path_label: Label = $"../../HBoxContainer/PanelContainer/PathLabel"



func _ready() -> void:
	connection_request.connect(_on_connection_request)

func _gui_input(event: InputEvent) -> void:
	if event is InputEventKey and event.is_pressed() and not event.is_echo():
		if event.keycode == KEY_S and event.ctrl_pressed:
			if _path_label.text == "null":
				$"../../HBoxContainer/MenuBar/File".save_file_dialog.popup_centered()
			else:
				save_resource_action(_path_label.text)
				
			get_viewport().set_input_as_handled()
			print("Graph Saved!")
			
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_RIGHT and event.is_pressed():
		var mouse_pos = get_local_mouse_position()
		var closest_connection = get_closest_connection_at_point(mouse_pos)
		if closest_connection == null:
			return
			
		if closest_connection.size() == 0:
			return
			
		var undo_redo = EditorInterface.get_editor_undo_redo()
		undo_redo.create_action("Disconnect Nodes")
		undo_redo.add_do_method(self, "disconnect_node", closest_connection["from_node"], closest_connection["from_port"], closest_connection["to_node"], closest_connection["to_port"])
		undo_redo.add_undo_method(self, "connect_node", closest_connection["from_node"], closest_connection["from_port"], closest_connection["to_node"], closest_connection["to_port"])
		undo_redo.commit_action()


	
func _on_connection_request(from_node_name: StringName, from_port: int, to_node_name: StringName, to_port: int) -> void:
	if Engine.is_editor_hint():
		var undo_redo = EditorInterface.get_editor_undo_redo()
		undo_redo.create_action("Connect Nodes")
		undo_redo.add_do_method(self, "connect_node", from_node_name, from_port, to_node_name, to_port)
		undo_redo.add_undo_method(self, "disconnect_node", from_node_name, from_port, to_node_name, to_port)
		undo_redo.commit_action()	
	else:
		connect_node(from_node_name, from_port, to_node_name, to_port)

func _can_drop_data(at_position: Vector2, data: Variant) -> bool:
	if data is NodeDefinition:
		return true
	if data.type == "files":
		if data.files.size() == 1:
			if data.files[0].get_extension() == "gd" or data.files[0].get_extension() == "tres":
				return true

	return false

func _drop_data(at_position: Vector2, data: Variant) -> void:
	var droped_resource: NodeDefinition
	if not data is NodeDefinition:
		if 	data.files[0].ends_with(".tres") and data.files.size() == 1:
			load_resource_action(data.files[0])
			return
		else:
			droped_resource = load(data.files[0]).new()
	else:
		droped_resource = data
	
	if droped_resource is StartDef:
		return
	
	var definition = droped_resource
	var node = load(definition._get_dialogue_node()).instantiate()
	node.definition = definition
	var viewposition = (at_position + scroll_offset) / zoom
	node.position_offset = viewposition
	node.name = str(_next_id)
	node.id = _next_id
	_next_id += 1

	if Engine.is_editor_hint():
		var undo_redo = EditorInterface.get_editor_undo_redo()
		undo_redo.create_action("Add Dialogue Node")
		
		undo_redo.add_do_method(self, "add_child", node)
		undo_redo.add_do_method(node, "set_owner", self)

		undo_redo.add_undo_method(self, "remove_child", node)
		undo_redo.add_undo_property(self, "_next_id", _next_id - 1)
	
		undo_redo.commit_action()
	else :
		add_child(node)
		node.set_owner(self)

func get_connections_for_node(node: GraphNode) -> Array:
	var results = []
	for connection in get_connection_list():
		if connection.from_node == node.name or connection.to_node == node.name:
			results.append(connection)
	return results

func capture_current_graphedit() -> DialogueGraphResource:
	var graph_resource = DialogueGraphResource.new()
	
	var nodes_data = {}
	var node_name_to_id = {}

	for node in get_children():
		if node is DialogueNode:
			node.definition._capture()
			var node_data = {
				"name": node.name,
				"size": node.size,
				"position_offset": node.position_offset,
				"definition": node.definition,
				"id": node.id
			}
			nodes_data[node.id] = node_data
			node_name_to_id[node.name] = node.id
			
			if node.definition is StartDef:
				graph_resource.start_node_id = node.id

	graph_resource.nodes = nodes_data
	
	var connections_data: Array[Dictionary] = []
	for c in get_connection_list():
		var from_id = node_name_to_id.get(c.from_node)
		var to_id = node_name_to_id.get(c.to_node)
		if from_id != null and to_id != null:
			connections_data.append({
				"from_node_id": from_id,
				"from_port": c.from_port,
				"to_node_id": to_id,
				"to_port": c.to_port
			})
	
	graph_resource.connections = connections_data
	graph_resource.next_node_id = _next_id
	return graph_resource

func save_resource_action(path: String) -> void:
	var graph_resource = capture_current_graphedit()
	graph_resource.take_over_path(path)
	var error = ResourceSaver.save(graph_resource, path)
	if error != OK:
		push_error("An error occurred while saving the dialogue graph resource.")
	else:
		_path_label.text = path


func clear_graph() -> void:
	clear_connections()
	for node in get_children():
		if node is DialogueNode:
			node.free()

func load_resource_action(path: String) -> void:
	var undo_redo = EditorInterface.get_editor_undo_redo()
	undo_redo.create_action("load_resource")
	undo_redo.add_do_property(_path_label, "text", path)
	undo_redo.add_do_method(self, "load_resource", ResourceLoader.load(path))
	undo_redo.add_undo_method(self, "load_resource", capture_current_graphedit())
	undo_redo.add_undo_property(_path_label, "text", _path_label.text)	
	undo_redo.commit_action()
	pass

func load_resource(resource: DialogueGraphResource) -> void:
	clear_graph()
	
	var id_to_name_map = {}

	for node_id in resource.nodes:
		var node_data = resource.nodes[node_id]
		var definition = node_data.definition
		if definition == null:
			push_error(str(node_id) + ": definition is null")
			continue
		
		var node = load(definition._get_dialogue_node()).instantiate()
		
		node.name = node_data["name"]
		node.definition = definition
		node.position_offset = node_data["position_offset"]
		node.id = node_id
		
		add_child(node)

		id_to_name_map[node_id] = node.name
		
	_next_id = resource.next_node_id
	
	await get_tree().process_frame

	for node in get_children():
		if node is DialogueNode:
			if resource.nodes.has(node.id):
				var node_data = resource.nodes[node.id]
				if node_data.has("size"):
					node.set_deferred("size", node_data["size"])


	for connection in resource.connections:
		var from_name = id_to_name_map.get(connection.from_node_id)
		var to_name = id_to_name_map.get(connection.to_node_id)
		
		if from_name != null and to_name != null:
			connect_node(from_name, connection.from_port, to_name, connection.to_port)
	
	
