extends RichTextLabel

@export var speed: float = 1.0
@export var delay_per_char: float = 0.05

var elapsedtime: float = 0.0
var read_finished: bool = true
signal on_read_finished

func _process(delta: float) -> void:
	if visible_ratio < 1.0:	
		elapsedtime += delta * speed
		if elapsedtime >= delay_per_char:
			elapsedtime = 0.0
			visible_characters += 1
	else:
		if !read_finished:
			on_read_finished.emit()
			read_finished = true
			
func start() -> void:
	elapsedtime = 0.0
	visible_ratio = 0.0
	read_finished = false


func start_from_visible_characters() -> void:
	elapsedtime = 0.0
	read_finished = false
