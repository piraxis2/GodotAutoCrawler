extends Node

var _consoleWindow: Window 
var _debug_canvas: CanvasLayer
var _debug_label: Label

var _isUsingDebugLine: bool

func _ready() -> void:
	pass

func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.keycode == KEY_QUOTELEFT and event.pressed:
		if _consoleWindow == null:
			_consoleWindow = load("res://addons/devconsole/UI/consoleWindow.tscn").instantiate()
			get_tree().root.add_child(_consoleWindow)
			_consoleWindow.connect("close_requested", Callable(self, "_on_close_requested"))
			
			
func _on_close_requested() -> void:
	if _consoleWindow:
		_consoleWindow.queue_free()
	_consoleWindow = null
#astar용 debug라인 노출	
func use_astar_debug_line() -> void:
	_isUsingDebugLine = true
	debug_label()
#astar용 debug라인 노출 해제
func unuse_astar_debug_line() -> void:
	_isUsingDebugLine = false
	debug_label()
	
func debug_label() -> void: 
	if _debug_label:
		_debug_label.queue_free()
	if _isUsingDebugLine:
		_debug_canvas = CanvasLayer.new()
		get_tree().root.add_child(_debug_canvas)
		_debug_label = Label.new()
		_debug_label.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
		_debug_label.name = "DebugLabel"
		_debug_label.global_position = Vector2(0, 0)
		_debug_label.label_settings = LabelSettings.new()
		_debug_label.label_settings.font_size = 10
		_debug_label.label_settings.font_color = "#090"
		_debug_canvas.add_child(_debug_label)
		_debug_label.text = "UsingDebugLine..."
		
# 현재 씬을 다시 로드
func reload_current_scene() -> void:
	get_tree().reload_current_scene()
