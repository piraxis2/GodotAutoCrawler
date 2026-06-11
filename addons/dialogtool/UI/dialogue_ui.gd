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
@onready var _portraits: Control = $Portraits

# DialogueUI가 Portrait 지속 상태를 소유한다(DialoguePlayer는 상태를 갖지 않음 — DT-002 Step 1).
# slot(left/center/right) -> {texture_path, actor, expression}. 렌더는 같은 이름의 TextureRect.
const _PORTRAIT_SLOTS := ["left", "center", "right"]
var _portrait_state: Dictionary = {}
var _say_lines: PackedStringArray = PackedStringArray()
var _say_line_index: int = -1
var _say_visible_text: String = ""


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
	# 새 대화 시작 전 이전 Portrait 상태를 정리한다(같은 UI가 재사용되는 경우 방어).
	_clear_portraits()
	_clear_say_lines()
	dialogue_player.start_dialogue.call_deferred(dialogue_resource)

func _ui_request(request: Dictionary) -> void:
	var request_type = request.get("type")
	match request_type:
		"display_text": _show_say_box(request)
		"offer_choice": _show_choice_box(request)
		"portrait_state": _handle_portrait_state(request)
	
func _show_say_box(request: Dictionary)	-> void:
	choice_box.visible = false
	say_box.visible = true
	
	speaker.text = request.get("speaker", "")
	# 줄바꿈은 같은 Say 노드 안의 페이지 경계로 취급한다. 첫 줄부터 표시하고,
	# 이후 클릭으로 한 줄씩 진행한 뒤 마지막 줄에서만 Player를 advance한다.
	var full_text := str(request.get("say", "")).replace("\r\n", "\n").replace("\r", "\n")
	_say_lines = full_text.split("\n", true)
	_say_line_index = 0
	_show_current_say_line()
	

func _show_choice_box(request: Dictionary)	-> void:
	choice_box.visible = true 
	say_box.visible = false 
	_clear_say_lines()

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
	_clear_say_lines()
	# 대화 종료 시 Portrait 상태/렌더를 정리한다.
	_clear_portraits()


func _show_current_say_line() -> void:
	if _say_line_index < 0 or _say_line_index >= _say_lines.size():
		return
	var current_line := _say_lines[_say_line_index]
	_say_visible_text += ("\n" if _say_line_index > 0 else "") + current_line
	say.text = _say_visible_text
	if current_line.is_empty():
		# 빈 줄도 한 페이지로 유지하되, 표시할 문자가 없으므로 즉시 완료 상태로 둔다.
		say.visible_ratio = 1.0
	else:
		# 이전 줄은 그대로 보이고 새로 추가된 줄만 타이핑되도록 공개 문자 수를 맞춘다.
		say.visible_characters = _say_visible_text.length() - current_line.length()
		say.start_from_visible_characters()


func _clear_say_lines() -> void:
	_say_lines = PackedStringArray()
	_say_line_index = -1
	_say_visible_text = ""


# Step 1 계약의 portrait_state 요청을 소비해 slot 단위로 렌더링/상태를 갱신한다.
# 비대기 명령이므로 Say와 독립적으로 상태가 유지된다(Say 핸들러는 Portrait를 건드리지 않음).
func _handle_portrait_state(request: Dictionary) -> void:
	if _portraits == null:
		return
	var slot := str(request.get("slot", "center"))
	var rect := _portraits.get_node_or_null(NodePath(slot)) as TextureRect
	if rect == null:
		push_warning("DialogueUI: unknown portrait slot '%s'." % slot)
		return

	match str(request.get("action", "")):
		"show":
			_portrait_show(slot, rect, request)
		"expression":
			_portrait_expression(slot, rect, request)
		"hide":
			_hide_portrait(slot, rect)
		_:
			push_warning("DialogueUI: unknown portrait action '%s'." % str(request.get("action", "")))


# show: slot 상태를 통째로 갱신하고 texture_path 이미지를 표시한다(빈 경로/로드 실패는 null).
func _portrait_show(slot: String, rect: TextureRect, request: Dictionary) -> void:
	var path := str(request.get("texture_path", ""))
	rect.texture = _load_texture(path)
	rect.visible = true
	_portrait_state[slot] = {
		"texture_path": path,
		"actor": str(request.get("actor", "")),
		"expression": str(request.get("expression", "")),
		"transition": str(request.get("transition", "none")),
	}


# expression: 기존 slot 상태를 기준으로 요청에서 제공된(비어있지 않은) 값만 갱신한다.
# 빈 texture_path는 기존 Texture를 제거하지 않는다. show 이전이어도 크래시하지 않는다.
func _portrait_expression(slot: String, rect: TextureRect, request: Dictionary) -> void:
	var state: Dictionary = _portrait_state.get(slot, {"texture_path": "", "actor": "", "expression": "", "transition": "none"}).duplicate()

	var path := str(request.get("texture_path", ""))
	if not path.is_empty():
		state["texture_path"] = path
		rect.texture = _load_texture(path)
	var actor := str(request.get("actor", ""))
	if not actor.is_empty():
		state["actor"] = actor
	var expr := str(request.get("expression", ""))
	if not expr.is_empty():
		state["expression"] = expr
	var transition := str(request.get("transition", ""))
	if not transition.is_empty():
		state["transition"] = transition

	rect.visible = true
	_portrait_state[slot] = state


func _hide_portrait(slot: String, rect: TextureRect) -> void:
	rect.texture = null
	rect.visible = false
	_portrait_state.erase(slot)


func _load_texture(path: String) -> Texture2D:
	if path.is_empty():
		return null
	if not ResourceLoader.exists(path):
		push_warning("DialogueUI: portrait texture not found: '%s'." % path)
		return null
	var res = load(path)
	if res is Texture2D:
		return res
	push_warning("DialogueUI: portrait resource is not a Texture2D: '%s'." % path)
	return null


func _clear_portraits() -> void:
	_portrait_state.clear()
	if _portraits == null:
		return
	for slot in _PORTRAIT_SLOTS:
		var rect := _portraits.get_node_or_null(NodePath(slot)) as TextureRect
		if rect:
			rect.texture = null
			rect.visible = false
	


func _on_button_pressed() -> void:
	if say.visible_ratio < 1.0:
		say.visible_ratio = 1.0
	elif _say_line_index >= 0 and _say_line_index + 1 < _say_lines.size():
		_say_line_index += 1
		_show_current_say_line()
	else:
		_clear_say_lines()
		dialogue_player.advance()	
