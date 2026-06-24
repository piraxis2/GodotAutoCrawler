@tool
extends Control
var property_selector_instance

@onready var scene_path: Label = $VSplitContainer/HBoxContainer/HBoxContainer/HBoxContainer2/PanelContainer2/ScenePathLabel
@onready var resource_path: Label = $VSplitContainer/HBoxContainer/HBoxContainer/HBoxContainer/PanelContainer/PathLabel
@onready var graph_edit: GraphEdit = $VSplitContainer/HSplitContainer/GraphEdit

func _on_button_button_up() -> void:
	if scene_path.text == "null" or scene_path.text.is_empty():
		print("scene경로가 존재하지 않습니다")
		return
		
	if resource_path.text == "null" or resource_path.text.is_empty():
		print("리소스가 저장되지 않았습니다.")
		return
	else:
		graph_edit.save_resource_action(resource_path.text)
		
		
	var godot_executable = OS.get_executable_path()
	
	var editor_settings = EditorInterface.get_editor_settings()
	var remote_port = editor_settings.get_setting("network/debug/remote_port")
	var remote_host = editor_settings.get_setting("network/debug/remote_host")

	var debug_address = "tcp://{host}:{port}".format({"host": remote_host, "port": remote_port})

	var debugger_args =["--remote-debug", debug_address] 
	
	var custum_args = ["--scene", scene_path.text, "--dialogue_resource",  resource_path.text, "--is_dialogue_debug_mod", "true"]

	print(custum_args)
	print(OS.execute_with_pipe(godot_executable, custum_args + debugger_args, false))
