extends Window

@export var console_text_label: RichTextLabel
@export var console_input: LineEdit


# Called when the node enters the scene tree for the first time.
func _ready() -> void:
	pass # Replace with function body.

# Called every frame. 'delta' is the elapsed time since the previous frame.
func _process(delta: float) -> void:
	pass

func add_text_from_bottom(new_text: String) -> void:
	console_text_label.append_text("\n" + new_text)
	console_text_label.scroll_to_line(console_text_label.get_line_count() - 1)


func _on_close_requested() -> void:
	queue_free()
