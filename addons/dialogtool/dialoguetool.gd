@tool
extends EditorPlugin

const MainPanel = preload("res://addons/dialogtool/dialoguetool_main.tscn")
var main_panel_instance

const UTILITY_SINGLETON_NAME = "DialogueToolUtil"
const UTILITY_SINGLETON_PATH = "res://addons/dialogtool/dialoguetool_util.gd"

func _enter_tree() -> void:
	add_autoload_singleton(UTILITY_SINGLETON_NAME, UTILITY_SINGLETON_PATH)
	main_panel_instance = MainPanel.instantiate()
	main_panel_instance.size_flags_vertical = Control.SIZE_EXPAND_FILL
	EditorInterface.get_editor_main_screen().add_child(main_panel_instance)
	
	_make_visible(false)

	# Initialization of the plugin goes here.
	pass


func _exit_tree() -> void:
	remove_autoload_singleton(UTILITY_SINGLETON_NAME)
	if main_panel_instance:
		main_panel_instance.queue_free()
	# Clean-up of the plugin goes here.
	pass
func _has_main_screen():
	return true


func _make_visible(visible):
	if main_panel_instance:
		main_panel_instance.visible = visible
	pass


func _get_plugin_name():
	return "Dialogue"


func _get_plugin_icon():
	return load("res://addons/dialogtool/Icon/dialogue_tool_icon.png")
