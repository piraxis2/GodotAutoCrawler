class_name DialogueUI extends Control

@onready var speaker: Label = $MarginContainer/VBoxContainer/Name
@onready var say: RichTextLabel = $MarginContainer/VBoxContainer/Text
@onready var say_box: Container = $MarginContainer
@onready var choice_box: Container = $PanelContainer
@onready var choice_list: Container = $PanelContainer/VBoxContainer

@onready var dialogue_player: DialoguePlayer = $Node


func _ready() -> void:
	if dialogue_player:
		dialogue_player.ui_request.connect(_ui_request)
		dialogue_player.dialogue_end.connect(_on_dialogue_end)
	
func _ui_request(request: Dictionary) -> void:
	var request_type = request.get("type")
	match request_type:
		"display_text": _show_say_box(request) 
		"offer_choice": _show_choice_box(request)
	
func _show_say_box(request: Dictionary)	-> void:
	choice_box.visible = false
	say_box.visible = true
	
	speaker.text = request.get("speaker", "")
	say.text = request.get("say", "")
	say.start()
	

func _show_choice_box(request: Dictionary)	-> void:
	choice_box.visible = true 
	say_box.visible = false 

	for child in choice_list.get_children():
		child.queue_free()
	
	var choices = request.get("choices", [])
	for i in range(choices.size()):
		var button = Button.new()
		button.text = choices[i]
		button.pressed.connect(_on_choice_button_pressed.bind(i))
		choice_list.add_child(button)


func _on_choice_button_pressed(index: int) -> void:
	dialogue_player.select_choice(index)
	choice_box.visible = false
	
	
func _on_dialogue_end():
	say_box.visible = false
	choice_box.visible = false
	


func _on_button_pressed() -> void:
	if say.visible_ratio < 1.0:
		say.visible_ratio = 1.0
	else:
		dialogue_player.advance()	
