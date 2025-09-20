@tool
extends PopupMenu

var save_file_dialog: FileDialog
var load_file_dialog: FileDialog

@onready var graph_edit = $"../../../HSplitContainer/GraphEdit" 

func _ready() -> void:
	clear()
	add_item("Save", 0)
	add_item("Load", 1)
	add_item("New", 2)

	
	# Save Dialog
	save_file_dialog = FileDialog.new()
	save_file_dialog.name = "SaveFileDialog"
	save_file_dialog.file_mode = FileDialog.FILE_MODE_SAVE_FILE
	save_file_dialog.add_filter("*.tres", "Dialogue Graph Resource")
	save_file_dialog.file_selected.connect(_on_save_file_selected)
	get_tree().root.call_deferred("add_child", save_file_dialog)

	# Load Dialog
	load_file_dialog = FileDialog.new()
	load_file_dialog.name = "LoadFileDialog"
	load_file_dialog.file_mode = FileDialog.FILE_MODE_OPEN_FILE
	load_file_dialog.add_filter("*.tres", "Dialogue Graph Resource")
	load_file_dialog.file_selected.connect(_on_load_file_selected)
	get_tree().root.call_deferred("add_child", load_file_dialog)


func _on_id_pressed(id: int) -> void:
	match id:
		0: # Save
			save_file_dialog.popup_centered()
		1: # Load
			load_file_dialog.popup_centered()
		2: # New
			graph_edit.reset()

func _on_save_file_selected(path: String) -> void:
	if graph_edit:
		graph_edit.save_resource_action(path)
	else:
		push_error("GraphEdit node not found for saving.")

func _on_load_file_selected(path: String) -> void:
	if graph_edit:
		graph_edit.load_resource_action(path)
	else:
		push_error("GraphEdit node not found for loading.")
