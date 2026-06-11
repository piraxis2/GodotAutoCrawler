@tool
class_name DialogueDebuggerPlugin extends EditorDebuggerPlugin

# 디버그 실행(별도 프로세스)에서 보낸 현재 실행 노드를 받아 에디터 GraphEdit에
# 하이라이트한다. graph_edit는 플러그인 등록 시 dialoguetool.gd가 주입한다.
var graph_edit


func _has_capture(capture: String) -> bool:
	return capture == "dialogue"


func _capture(message: String, data: Array, _session_id: int) -> bool:
	if message == "dialogue:current_node":
		if graph_edit and is_instance_valid(graph_edit):
			var node_id: int = int(data[0]) if data.size() > 0 else -1
			if node_id < 0:
				graph_edit.clear_highlight()
			else:
				graph_edit.highlight_node(node_id)
		return true
	return false


func _setup_session(session_id):
	return
