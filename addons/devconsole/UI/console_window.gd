extends Window

@export var console_text_label: RichTextLabel
@export var console_input: LineEdit

var history: Array[String] = []

# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	call_deferred("_load_history")
	console_input.text_submitted.connect(add_text_from_bottom);
	pass # Replace with function body.
	
func _exit_tree() -> void:
	console_input.text_submitted.disconnect(add_text_from_bottom);
	_save_history()
	pass
	
func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.keycode == KEY_UP and event.pressed:
		pass
	if event is InputEventKey and event.keycode == KEY_DOWN and event.pressed:
		pass
	pass

# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass

func add_text_from_bottom(new_text: String) -> void:
	console_text_label.append_text("\n" + new_text)
	console_text_label.scroll_to_line(console_text_label.get_line_count() - 1)
	_save_history()

func _load_history() -> void:
	var file: FileAccess = FileAccess.open("user://console_history.txt", FileAccess.READ)
	if file:
		history = []
		while not file.eof_reached():
			var line: String = file.get_line()
			history.append(line)
			console_text_label.append_text("\n" + line)
		file.close()

func _save_history() -> void:
	var file: FileAccess = FileAccess.open("user://console_history.txt", FileAccess.WRITE)
	if file:
		for line in history:
			file.store_line(line)
		file.close()
