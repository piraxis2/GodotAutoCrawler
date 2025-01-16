extends Node

var _consoleWindow: Window

func _ready() -> void:
	pass

func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.keycode == KEY_QUOTELEFT and event.pressed:
		if _consoleWindow == null:
			_consoleWindow = load("res://addons/devconsole/UI/consoleWindow.tscn").instantiate()
			get_tree().root.add_child(_consoleWindow)
			_consoleWindow.connect("close_requested", Callable(self, "_on_close_requested"))


func _on_close_requested() -> void:
	_consoleWindow = null
