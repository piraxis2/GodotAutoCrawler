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
	var node_name_to_gnode = {}

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
			node_name_to_gnode[node.name] = node

			if node.definition is StartDef:
				graph_resource.start_node_id = node.id

	graph_resource.nodes = node_datas

	var effect_type: int = DialogueNode.port_type.effect
	var connections_data: Array[Dictionary] = []
	for c in get_connection_list():
		var from_id = node_name_to_id[c.from_node]
		var to_id = node_name_to_id[c.to_node]
		if from_id != null and to_id != null:
			var connection := {
				"from_node_id": from_id,
				"from_port": c.from_port,
				"to_node_id": to_id,
				"to_port": c.to_port
			}
			# 출력 포트 타입이 effect면 비대기 Effect 연결로 표시한다(ADR-005).
			# kind를 포트 타입에서 파생하므로 저장/재로드 후 재캡처해도 동일하게 복원된다.
			var from_g = node_name_to_gnode.get(c.from_node)
			if from_g != null and c.from_port < from_g.get_output_port_count():
				if from_g.get_output_port_type(c.from_port) == effect_type:
					connection["kind"] = DialogueGraphResource.CONNECTION_KIND_EFFECT
					# Choice의 항목별 Effect 출력은 choice_index를 보존한다(ADR-010 Step 3b).
					# 항목 index를 모르는(공통) Effect는 choice_index를 기록하지 않아 shared로 동작한다.
					if from_g.has_method("effect_choice_index_for_port"):
						var ci: int = from_g.effect_choice_index_for_port(c.from_port)
						if ci >= 0:
							connection["choice_index"] = ci
			connections_data.append(connection)
	
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
	var effect_type: int = DialogueNode.port_type.effect
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

	# (4) 연결 양 끝 노드 존재 + (3) 포트 카테고리 + (A) 주 Flow 단일성 + (B) Effect 화이트리스트.
	# flow_groups["from_id:from_port"] = {from_id, from_port, targets:[{id,port,type} ...]}.
	var flow_groups := {}
	var effect_adj := {}         # from_id -> [to_id ...] : Effect 간선(순환 검사용).
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
		var to_type: StringName = _node_runtime_type(nodes, to_id)
		# 포트는 카테고리(flow / value(data·boolean) / effect)가 같아야 연결할 수 있다.
		# flow끼리, data/boolean 값 포트끼리, effect끼리만 허용하고 카테고리가 다르면 치명적 오류.
		if _port_category(out_type) != _port_category(in_type):
			push_error("DialogueTool 검증: 포트 카테고리 불일치 연결 — node %s(out type %d) port %d → node %s(in type %d) port %d." % [str(from_id), out_type, c.from_port, str(to_id), in_type, c.to_port])
			fatal = true
			continue

		var from_type: StringName = _node_runtime_type(nodes, from_id)
		if out_type == flow_type:
			# (A) 주 Flow 출력 포트별 대상 수집(나중에 2개 이상이면 차단).
			var key := "%d:%d" % [from_id, c.from_port]
			if not flow_groups.has(key):
				flow_groups[key] = {"from_id": from_id, "from_port": c.from_port, "from_type": from_type, "targets": []}
			flow_groups[key]["targets"].append({"id": to_id, "port": c.to_port, "type": to_type})
		elif out_type == effect_type:
			# (B) Effect 대상 type whitelist: Portrait + State mutation(state_set/state_add)만 허용.
			if not DialogueGraphResource.is_effect_target_type(to_type):
				push_error("DialogueTool 검증: 허용되지 않은 Effect 대상입니다 — %s. Effect는 Portrait 또는 State Set/Add만 허용합니다." % _format_port_edge(from_id, from_type, c.from_port, to_id, to_type, c.to_port))
				fatal = true
			# 순환 검사용 Effect 간선(포트 포함).
			effect_adj.get_or_add(from_id, []).append({"to": to_id, "from_port": c.from_port, "to_port": c.to_port})

	# (A) 한 실행 지점의 주 Flow 대상은 최대 하나. 여러 개면 조용히 첫 연결만 실행되므로 차단.
	for key in flow_groups:
		var group: Dictionary = flow_groups[key]
		var targets: Array = group["targets"]
		if targets.size() > 1:
			var edge_descs: Array = []
			for t in targets:
				edge_descs.append(_format_port_edge(group["from_id"], group["from_type"], group["from_port"], t["id"], t["type"], t["port"]))
			push_error("DialogueTool 검증: 주 Flow 대상이 둘 이상입니다 — %s. 한 Flow 포트에는 하나만 연결하세요." % " ; ".join(edge_descs))
			fatal = true

	# (C) Effect 순환 차단. Portrait는 에디터에서 Effect 출력이 없어 정상 그래프엔 생기지 않지만,
	# 수작업/레거시 리소스 방어를 위해 Effect 간선의 순환을 검사한다.
	var cycle_edges: Array = _find_effect_cycle(effect_adj)
	if not cycle_edges.is_empty():
		var cycle_descs: Array = []
		for e in cycle_edges:
			cycle_descs.append(_format_port_edge(e["from_id"], _node_runtime_type(nodes, e["from_id"]), e["from_port"], e["to_id"], _node_runtime_type(nodes, e["to_id"]), e["to_port"]))
		push_error("DialogueTool 검증: Effect 연결에 순환이 있습니다 — 경로: %s. Effect는 순환할 수 없습니다." % " → ".join(cycle_descs))
		fatal = true

	# (2) Start에서 도달 가능 + (5) 끊긴 Flow 경고.
	# flow_adj: 끊긴 Flow 검사용(flow 간선만). reach_adj: 도달성 검사용(flow + effect 간선).
	# Effect 연결로만 닿는 Portrait도 "도달 가능"으로 본다(ADR-005).
	if start_ids.size() == 1:
		var start_id = start_ids[0]
		var flow_adj := {}
		var reach_adj := {}
		for c in connections:
			var fg = id_to_gnode.get(c.get("from_node_id"))
			var tg = id_to_gnode.get(c.get("to_node_id"))
			if fg == null or tg == null:
				continue
			if c.from_port >= fg.get_output_port_count() or c.to_port >= tg.get_input_port_count():
				continue
			var ot: int = fg.get_output_port_type(c.from_port)
			var it: int = tg.get_input_port_type(c.to_port)
			if ot == flow_type and it == flow_type:
				flow_adj.get_or_add(c.from_node_id, []).append(c.to_node_id)
				reach_adj.get_or_add(c.from_node_id, []).append(c.to_node_id)
			elif ot == effect_type and it == effect_type:
				reach_adj.get_or_add(c.from_node_id, []).append(c.to_node_id)

		var reachable := {start_id: true}
		var queue: Array = [start_id]
		while not queue.is_empty():
			var cur = queue.pop_back()
			for nxt in reach_adj.get(cur, []):
				if not reachable.has(nxt):
					reachable[nxt] = true
					queue.append(nxt)

		if flow_adj.get(start_id, []).is_empty():
			push_warning("DialogueTool 검증: Start 노드에서 나가는 Flow 연결이 없습니다.")

		for nid in nodes:
			var def = nodes[nid].get("definition")
			if def is FlowDefinition and not (def is StartDef) and not reachable.has(nid):
				push_warning("DialogueTool 검증: 도달 불가능한 Flow 노드 (id %s, type %s)." % [str(nid), str(def.get_runtime_type())])

	# State Set/Add literal 타입 검증(DT-009 Step 3). 잘못된 literal(예: INT value에 "abc")은
	# 타입 불일치 값으로 캡처되므로 저장을 차단한다 — 런타임에서 조용히 0/false로 변환되지 않게 한다.
	for nid in nodes:
		var sdef = nodes[nid].get("definition")
		if sdef is StateEffectDef:
			var lit_err: String = sdef.validate_literal()
			if not lit_err.is_empty():
				push_error("DialogueTool 검증: State 노드 (id %s) — %s." % [str(nid), lit_err])
				fatal = true

	# State Read 구조 검증(DT-013 Step 2, ADR-015 D6). key empty/형식 불일치, value_type이 허용 5타입
	# 밖이면 저장을 차단한다. schema에 key가 실제로 있는지는 검사하지 않는다(runtime provider가 판정).
	for nid in nodes:
		var rdef = nodes[nid].get("definition")
		if rdef is WorldStateReadDef:
			var read_err: String = rdef.validate_structure()
			if not read_err.is_empty():
				push_error("DialogueTool 검증: State Read 노드 (id %s) — %s." % [str(nid), read_err])
				fatal = true

	return not fatal


# 포트 타입을 연결 호환 카테고리로 묶는다. 같은 카테고리끼리만 연결할 수 있다.
# 0=flow, 1=value(data·boolean), 2=effect.
func _port_category(port_type_value: int) -> int:
	if port_type_value == DialogueNode.port_type.flow:
		return 0
	if port_type_value == DialogueNode.port_type.effect:
		return 2
	return 1


# nodes 스냅샷에서 node_id의 런타임 타입을 얻는다(없으면 &"?").
func _node_runtime_type(nodes: Dictionary, node_id) -> StringName:
	if not nodes.has(node_id):
		return &"?"
	var def = nodes[node_id].get("definition")
	if def == null:
		return &"?"
	return def.get_runtime_type()


# 연결 한 줄을 "node A(type) out-port P → node B(type) in-port Q" 형식으로 포맷한다.
# 모든 validation 오류 메시지가 이 헬퍼를 공유해 node id/type/port를 일관되게 포함한다.
func _format_port_edge(from_id, from_type, from_port: int, to_id, to_type, to_port: int) -> String:
	return "node %s(type %s) out-port %d → node %s(type %s) in-port %d" % [str(from_id), str(from_type), from_port, str(to_id), str(to_type), to_port]


# Effect 간선 인접 리스트에서 순환을 찾아 그 간선 경로를 반환한다(닫힌 형태).
# adj[from_id] = [{to, from_port, to_port} ...]. 반환은 {from_id, from_port, to_id, to_port} 배열.
# 순환이 없으면 빈 배열. 오류 메시지에 정확한 port를 싣기 위해 node id가 아닌 간선을 돌려준다.
func _find_effect_cycle(adj: Dictionary) -> Array:
	var visiting := {}
	var visited := {}
	var parent_edge := {}
	for node in adj.keys():
		if not visited.has(node):
			var cyc := _effect_cycle_dfs(node, adj, visiting, visited, parent_edge)
			if not cyc.is_empty():
				return cyc
	return []


func _effect_cycle_dfs(node, adj: Dictionary, visiting: Dictionary, visited: Dictionary, parent_edge: Dictionary) -> Array:
	visiting[node] = true
	for e in adj.get(node, []):
		var nxt = e["to"]
		var edge := {"from_id": node, "from_port": e["from_port"], "to_id": nxt, "to_port": e["to_port"]}
		if visiting.has(nxt):
			# 순환 발견: 현재 경로(parent_edge)를 nxt까지 거슬러 올라가 간선들을 모으고 닫는 간선을 더한다.
			var cyc: Array = []
			var walk = node
			while walk != nxt:
				var pe: Dictionary = parent_edge[walk]
				cyc.push_front(pe)
				walk = pe["from_id"]
			cyc.append(edge)
			return cyc
		if not visited.has(nxt):
			parent_edge[nxt] = edge
			var r := _effect_cycle_dfs(nxt, adj, visiting, visited, parent_edge)
			if not r.is_empty():
				return r
	visiting.erase(node)
	visited[node] = true
	return []


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
		if from_name == null or to_name == null:
			continue

		var from_port: int = connection.from_port
		var to_port: int = connection.to_port

		# Effect 연결(kind=="effect")은 저장된 포트 index가 아니라 노드의 Effect 포트로
		# 정규화한다. Step 1 시대 리소스는 Effect를 Flow 포트(0→0)로 저장했으므로, kind를
		# 무시하고 그대로 연결하면 Effect가 Flow로 둔갑한다(연결 의미 손실). kind를 신뢰해
		# Effect 출력/입력 포트를 찾아 매핑하고, 매핑할 수 없으면 조용히 Flow로 바꾸지 않고
		# 오류로 보고한 뒤 그 연결을 건너뛴다.
		if connection.get("kind", "") == DialogueGraphResource.CONNECTION_KIND_EFFECT:
			var from_g: DialogueNode = get_node_or_null(NodePath(from_name))
			var to_g: DialogueNode = get_node_or_null(NodePath(to_name))
			var effect_in := _find_effect_port(to_g, false)
			# Effect 출력 포트 정규화(ADR-010 Step 3b):
			# - choice_index 있음: 유효한 int면 해당 항목 effect 포트. 잘못된 타입/범위는 첫 포트로
			#   fallback하지 않고(공통→항목0 오염 방지) 오류 후 연결을 건너뛴다.
			# - choice_index 없음: Choice면 전용 공통 effect 포트, 비-Choice(Start/Say)면 첫 effect 포트.
			var effect_out := -1
			if connection.has("choice_index"):
				var ci: Variant = connection["choice_index"]
				if typeof(ci) == TYPE_INT and from_g != null and from_g.has_method("effect_port_for_choice_index"):
					effect_out = from_g.effect_port_for_choice_index(ci)
			else:
				if from_g != null and from_g.has_method("common_effect_port"):
					effect_out = from_g.common_effect_port()
				else:
					effect_out = _find_effect_port(from_g, true)
			if effect_out == -1 or effect_in == -1:
				push_error("DialogueTool: Effect 연결을 매핑할 Effect 포트가 없습니다(잘못된 choice_index 포함) — node %s → node %s. 연결을 건너뜁니다." % [str(connection.from_node_id), str(connection.to_node_id)])
				continue
			from_port = effect_out
			to_port = effect_in

		connect_node(from_name, from_port, to_name, to_port)

	call_deferred("reset_camera")


# gnode에서 첫 Effect 포트 index를 찾는다(없으면 -1). is_output=true면 출력 포트,
# false면 입력 포트를 검사한다. Effect 연결을 로드할 때 포트 정규화에 사용한다.
func _find_effect_port(gnode: DialogueNode, is_output: bool) -> int:
	if gnode == null:
		return -1
	var effect_type: int = DialogueNode.port_type.effect
	if is_output:
		for i in gnode.get_output_port_count():
			if gnode.get_output_port_type(i) == effect_type:
				return i
	else:
		for i in gnode.get_input_port_count():
			if gnode.get_input_port_type(i) == effect_type:
				return i
	return -1

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
