class_name DialogueUI extends Control

# 게임 코드가 구독할 수 있도록 내부 DialoguePlayer의 시그널을 그대로 중계한다.
signal dialogue_started
signal dialogue_end
signal ui_request(request_data: Dictionary)

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
		# 외부로 중계
		dialogue_player.dialogue_started.connect(dialogue_started.emit)
		dialogue_player.dialogue_end.connect(dialogue_end.emit)
		dialogue_player.ui_request.connect(ui_request.emit)


# 게임 코드용 진입점: 리소스 하나를 넘겨 대화를 시작한다.
# (자식 player의 _ready가 끝난 뒤 시작하도록 deferred — 첫 노드 유실 방지.)
func play(dialogue_resource: DialogueGraphResource) -> void:
	visible = true
	dialogue_player.start_dialogue.call_deferred(dialogue_resource)

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

	# 단순 queue_free가 아니라 즉시 detach한다. 프레임이 끝나기 전에 선택창이 다시
	# 표시될 경우 낡은 버튼이 남거나 시그널을 다시 발생시키지 않도록 하기 위함.
	for child in choice_list.get_children():
		choice_list.remove_child(child)
		child.queue_free()

	var choices = request.get("choices", [])
	for i in range(choices.size()):
		var button = Button.new()
		button.text = str(choices[i])
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
