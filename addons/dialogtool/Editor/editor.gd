@tool
extends GraphEdit

var _popup_menu: PopupMenu
var _next_id: int = 1
@onready var _path_label: Label = $"../../HBoxContainer/HBoxContainer/HBoxContainer/PanelContainer/PathLabel"
@onready var context_menu: PopupMenu = $"../../../PopupMenu"
var graph_resource: DialogueGraphResource = DialogueGraphResource.new()

@onready var begin_scroll_offset: Vector2 = scroll_offset

func _ready() -> void:
	connection_request.connect(_on_connection_request)
	# data와 boolean은 서로 호환되는 "값(value)" 포트다 (예: Variable의 data 출력을
	# Branch의 boolean 조건 입력에 연결). 같은 타입 쌍은 기본 허용되며, 아래는
	# 교차 타입 값 연결을 등록한다.
	add_valid_connection_type(DialogueNode.port_type.data, DialogueNode.port_type.boolean)
	add_valid_connection_type(DialogueNode.port_type.boolean, DialogueNode.port_type.data)
	var definition = StartDef.new()
	var node = load(definition._get_dialogue_node()).instantiate()
	node.definition = definition
	definition.node_id = 0
	definition.graph_resource = weakref(graph_resource)
	var viewposition = (scroll_offset) / zoom
	node.position_offset = viewposition
	node.name = str(0)
	node.id = 0
	call_deferred("add_child", node)
	call_deferred("reset_camera")
	

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
		context_menu.popup_context_box(Rect2i(event.global_position, context_menu.size), get_closest_connection_at_point(event.position))
		context_menu.set_meta("at_position", event.position)
		
		
func disconnect_graph_node(from_node: StringName, from_port: int, to_node: StringName, to_port: int) -> void:
	disconnect_node(from_node, from_port, to_node, to_port)
	disconnection_request.emit(from_node, from_port, to_node, to_port)

	
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
	if data is DialogueDefinition:
		return true
	if data.type == "files":
		if data.files.size() == 1:
			if data.files[0].get_extension() == "gd" or data.files[0].get_extension() == "tres":
				return true

	return false

func _drop_data(at_position: Vector2, data: Variant) -> void:
	var droped_resource: DialogueDefinition
	if not data is DialogueDefinition:
		if data.files[0].ends_with(".tres") and data.files.size() == 1:
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
	definition.node_id = _next_id
	definition.graph_resource =	weakref(graph_resource)	
	node.definition = definition
	var viewposition = (at_position + scroll_offset) / zoom
	node.position_offset = viewposition
	node.name = str(_next_id)
	node.id = _next_id

	if Engine.is_editor_hint():
		add_dialogue_node(node)	
	else :
		_next_id += 1
		add_child(node)
		node.set_owner(self)

func add_dialogue_node(node: DialogueNode) -> void:
	var undo_redo = EditorInterface.get_editor_undo_redo()
	undo_redo.create_action("Add Dialogue Node")

	undo_redo.add_do_property(self, "_next_id", _next_id + 1)
	undo_redo.add_do_method(self, "add_child", node)
	undo_redo.add_do_method(node, "set_owner", self)

	undo_redo.add_undo_method(self, "remove_child", node)
	undo_redo.add_undo_property(self, "_next_id", _next_id - 1)

	undo_redo.commit_action()
	return

func get_connections_for_node(node: GraphNode) -> Array:
	var results = []
	for connection in get_connection_list():
		if connection.from_node == node.name or connection.to_node == node.name:
			results.append(connection)
	return results
	
func capture_current_graphedit() -> DialogueGraphResource:
	graph_resource = DialogueGraphResource.new()
	
	var node_datas = {}
	var node_name_to_id = {}

	for node in get_children():
		if node is DialogueNode:
			node.definition._capture(node)
			node.definition.graph_resource = weakref(graph_resource)
			var node_data = {
				"name": node.name,
				"size": node.size,
				"position_offset": node.position_offset,
				"definition": node.definition,
				"id": node.id
			}
			node_datas[node.id] = node_data
			node_name_to_id[node.name] = node.id
			
			if node.definition is StartDef:
				graph_resource.start_node_id = node.id

	graph_resource.nodes = node_datas
	
	var connections_data: Array[Dictionary] = []
	for c in get_connection_list():
		var from_id = node_name_to_id[c.from_node]
		var to_id = node_name_to_id[c.to_node]
		if from_id != null and to_id != null:
			connections_data.append({
				"from_node_id": from_id,
				"from_port": c.from_port,
				"to_node_id": to_id,
				"to_port": c.to_port
			})
	
	graph_resource.connections = connections_data
	graph_resource.next_node_id = _next_id
	graph_resource.set_runtime_snapshot(node_datas, connections_data)
	return graph_resource

func save_resource_action(path: String) -> void:
	var graph_resource = capture_current_graphedit()
	if not _validate_runtime_snapshot(graph_resource):
		push_error("DialogueTool: 런타임 검증 실패로 저장을 중단했습니다. (위 오류 메시지를 확인하세요)")
		return
	graph_resource.take_over_path(path)
	var error = ResourceSaver.save(graph_resource, path)
	if error != OK:
		push_error("An error occurred while saving the dialogue graph resource.")
	else:
		_path_label.text = path


# 저장 직전에 캡처된 스냅샷을 검증한다.
# 치명적(FATAL) 문제일 때만 false를 반환한다(저장 중단). 비치명적 문제는
# push_warning으로 알리고 저장은 그대로 진행한다.
func _validate_runtime_snapshot(graph_resource: DialogueGraphResource) -> bool:
	var nodes: Dictionary = graph_resource.nodes
	var connections: Array = graph_resource.connections
	var flow_type: int = DialogueNode.port_type.flow
	var fatal := false

	# 실제 포트 타입/개수를 조회하기 위해 node id -> 라이브 GraphNode 매핑.
	var id_to_gnode := {}
	for child in get_children():
		if child is DialogueNode:
			id_to_gnode[child.id] = child

	# (1) Start 노드는 정확히 1개.
	var start_ids: Array = []
	for nid in nodes:
		if nodes[nid].get("definition") is StartDef:
			start_ids.append(nid)
	if start_ids.size() != 1:
		push_error("DialogueTool 검증: Start 노드는 정확히 1개여야 합니다 (현재 %d개)." % start_ids.size())
		fatal = true

	# (4) 연결 양 끝 노드 존재 + (3) 명백한 포트 타입 오류 검사.
	for c in connections:
		var from_id = c.get("from_node_id")
		var to_id = c.get("to_node_id")
		var ends_ok := true
		if not nodes.has(from_id):
			push_error("DialogueTool 검증: 연결의 from_node_id %s 가 존재하지 않습니다." % str(from_id))
			fatal = true
			ends_ok = false
		if not nodes.has(to_id):
			push_error("DialogueTool 검증: 연결의 to_node_id %s 가 존재하지 않습니다." % str(to_id))
			fatal = true
			ends_ok = false
		if not ends_ok:
			continue

		var from_g = id_to_gnode.get(from_id)
		var to_g = id_to_gnode.get(to_id)
		if from_g == null or to_g == null:
			continue
		if c.from_port >= from_g.get_output_port_count() or c.to_port >= to_g.get_input_port_count():
			push_warning("DialogueTool 검증: 포트 index가 범위를 벗어난 연결을 건너뜁니다 (%s→%s)." % [str(from_id), str(to_id)])
			continue

		var out_type: int = from_g.get_output_port_type(c.from_port)
		var in_type: int = to_g.get_input_port_type(c.to_port)
		# Flow는 Flow끼리만 연결돼야 한다. data/boolean은 서로 호환되는 값 포트다.
		# Flow와 비-Flow 값 포트를 섞는 경우만 치명적 오류로 본다.
		if (out_type == flow_type) != (in_type == flow_type):
			push_error("DialogueTool 검증: Flow↔Data 타입 불일치 연결 — node %s port %d → node %s port %d." % [str(from_id), c.from_port, str(to_id), c.to_port])
			fatal = true

	# (2) Start에서 Flow 도달 가능 + (5) 끊긴 Flow 경고 — flow 간선 BFS.
	if start_ids.size() == 1:
		var start_id = start_ids[0]
		var flow_adj := {}
		for c in connections:
			var fg = id_to_gnode.get(c.get("from_node_id"))
			var tg = id_to_gnode.get(c.get("to_node_id"))
			if fg == null or tg == null:
				continue
			if c.from_port >= fg.get_output_port_count() or c.to_port >= tg.get_input_port_count():
				continue
			if fg.get_output_port_type(c.from_port) == flow_type and tg.get_input_port_type(c.to_port) == flow_type:
				flow_adj.get_or_add(c.from_node_id, []).append(c.to_node_id)

		var reachable := {start_id: true}
		var queue: Array = [start_id]
		while not queue.is_empty():
			var cur = queue.pop_back()
			for nxt in flow_adj.get(cur, []):
				if not reachable.has(nxt):
					reachable[nxt] = true
					queue.append(nxt)

		if flow_adj.get(start_id, []).is_empty():
			push_warning("DialogueTool 검증: Start 노드에서 나가는 Flow 연결이 없습니다.")

		for nid in nodes:
			var def = nodes[nid].get("definition")
			if def is FlowDefinition and not (def is StartDef) and not reachable.has(nid):
				push_warning("DialogueTool 검증: 도달 불가능한 Flow 노드 (id %s, type %s)." % [str(nid), str(def.get_runtime_type())])

	return not fatal


func clear_graph() -> void:
	clear_connections()
	for node in get_children():
		if node is DialogueNode:
			node.queue_free()

func load_resource_action(path: String) -> void:
	if Engine.is_editor_hint():
		var undo_redo = EditorInterface.get_editor_undo_redo()
		undo_redo.create_action("load_resource")
		undo_redo.add_do_property(_path_label, "text", path)
		undo_redo.add_do_method(self, "load_resource", ResourceLoader.load(path))
		undo_redo.add_undo_method(self, "load_resource", capture_current_graphedit())
		undo_redo.add_undo_property(_path_label, "text", _path_label.text)	
		undo_redo.commit_action()
	else:
		load_resource(ResourceLoader.load(path))
	pass

func load_resource(resource: DialogueGraphResource) -> void:
	clear_graph()
	graph_resource = resource
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
		node.definition.graph_resource = weakref(graph_resource)
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

	call_deferred("reset_camera")			

func reset() -> void:
	_next_id = 1
	clear_connections()
	for node in get_children():
		if node is DialogueNode:
			if node.definition is StartDef:
				node.position_offset = begin_scroll_offset / zoom
				continue
			node.free()
	_path_label.text = "null"
	graph_resource = DialogueGraphResource.new()
	
func reset_camera() -> void:
	zoom = 1
	var start_node = get_start_node()
	
	scroll_offset = start_node.position_offset - size / 2 + start_node.size / 2
	
func get_start_node() -> DialogueNode:
	for child in get_children():
		if child is DialogueNode:
			if child.definition is StartDef:
				return child

	return null


var _highlighted_node: DialogueNode = null
var _highlight_prev_modulate: Color = Color.WHITE

# 현재 실행 중인 노드를 시각적으로 강조하는 hook.
# (예: 에디터 내 미리보기/디버거에서 DialoguePlayer.current_node_changed 를
# 이 메서드에 연결하면 실행 노드가 하이라이트된다.)
# 직전 강조 노드는 원래 modulate로 복원한다.
func highlight_node(node_id: int) -> void:
	clear_highlight()
	for child in get_children():
		if child is DialogueNode and child.id == node_id:
			_highlighted_node = child
			_highlight_prev_modulate = child.modulate
			child.modulate = Color(1.6, 1.6, 0.7)
			return


func clear_highlight() -> void:
	if _highlighted_node and is_instance_valid(_highlighted_node):
		_highlighted_node.modulate = _highlight_prev_modulate
	_highlighted_node = null
