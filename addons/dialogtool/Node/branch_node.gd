@tool
extends DialogueNode


func _ready() -> void:
	delete_button = load("res://addons/dialogtool/Node/Sub/delete_button.tscn").instantiate()
	add_child(delete_button)
	move_child(delete_button, 0)
	delete_button.on_click_delete.connect(_on_delete_button_pressed)
	
	if definition:
		definition._node_init(self)
		title = str(definition.get_script().get_global_name()).left(-3)
