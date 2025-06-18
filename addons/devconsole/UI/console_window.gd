extends Window

@export var console_text_label: RichTextLabel
@export var console_input: LineEdit
@export var console_scroll_container: ScrollContainer

var history: Array[String]     = []
var current_history_index: int = 0


# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	_load_history()
	console_input.text_submitted.connect(_add_text_from_bottom)
	pass # Replace with function body.


func _exit_tree() -> void:
	console_input.text_submitted.disconnect(_add_text_from_bottom)
	_save_history()
	pass


func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.keycode == KEY_UP and event.pressed:
		if current_history_index < len(history) - 1:
			current_history_index += 1
			console_input.text = history[len(history) - 1 - current_history_index]
		pass
	if event is InputEventKey and event.keycode == KEY_DOWN and event.pressed:
		if current_history_index > 0:
			current_history_index -= 1
			console_input.text = history[len(history) - 1 - current_history_index]
		elif current_history_index == 0:
			console_input.text = ""
		pass
	pass


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass


func _add_text_from_bottom(new_text: String) -> void:
	if new_text.strip_edges() != "" and (history.is_empty() or history[-1] != new_text):
		history.append(new_text)
	console_text_label.append_text(new_text + "\n")
	console_text_label.scroll_to_line(console_text_label.get_line_count() - 1)
	_scroll_to_bottom()
	_save_history()
	_call_func(new_text)
	
func _call_func(command: String) -> void:
	if has_method(command):
		call(command)
	else:
		GdsCheatManager.call(command)

func _load_history() -> void:
	var file: FileAccess = FileAccess.open("user://console_history.txt", FileAccess.READ)
	if file:
		history = []
		var lines := []
		while not file.eof_reached():
			var line: String = file.get_line()
			if not history.has(line):
				history.append(line)
			lines.append(line)
		file.close()
		var all_text := "\n".join(lines)
		console_text_label.append_text(all_text)
		console_text_label.scroll_to_line(console_text_label.get_line_count() - 1)
		_scroll_to_bottom()


func _save_history() -> void:
	var file: FileAccess = FileAccess.open("user://console_history.txt", FileAccess.WRITE)
	if file:
		for line in history:
			file.store_line(line)
		file.close()

func _clear_console() -> void:
		console_text_label.clear()
		history.clear()
		_save_history()
		current_history_index = 0
		_scroll_to_bottom()


func _scroll_to_bottom():
	await get_tree().process_frame # UI가 갱신된 후 실행
	if console_scroll_container and console_scroll_container.get_v_scroll_bar():
		console_scroll_container.scroll_vertical = console_scroll_container.get_v_scroll_bar().max_value
