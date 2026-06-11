@tool
extends "res://addons/dialogtool/Editor/Adapter/dialogue_editor_adapter.gd"

# SceneFunction 노드의 캡처 (SceneFunctionDef._capture에서 추출).
# UI는 scene_function_node.tscn이 제공하므로 apply_params는 비워 둔다.

func capture_params(node: DialogueNode) -> Dictionary:
	return {"func_name": node.option_button.get_selected_metadata()}
