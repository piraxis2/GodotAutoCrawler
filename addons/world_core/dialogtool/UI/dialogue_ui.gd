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

# deferred 시작 대기 요청 {resource, read_provider, mutation_provider}. 같은 프레임에 play()가
# 여러 번 호출되면 마지막 요청만 시작한다(latest-wins). resource/read/mutation provider를 한 묶음으로
# 묶어 분리되지 않게 한다(ADR-010 D9 — 폐기된 대화가 잘못된 provider로 mutation하지 못하게 함).
var _pending_start: Dictionary = {}
var _start_scheduled: bool = false


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
# read_state_provider(선택)는 DialoguePlayer에 주입할 read 상태 provider다(DT-005 Step 5).
# mutation_state_provider(선택)는 state_* Effect가 사용할 mutation provider다(DT-009 Step 2, ADR-010 D1).
func play(dialogue_resource: DialogueGraphResource, read_state_provider = null, mutation_state_provider = null) -> void:
	visible = true
	# 새 대화 시작 전 이전 Portrait 상태를 정리한다(같은 UI가 재사용되는 경우 방어).
	_clear_portraits()
	_clear_say_lines()
	# resource와 두 provider를 한 묶음으로 deferred 시작한다. provider만 즉시 공유 필드에 저장하면
	# 같은 프레임의 다음 play()가 그 필드를 덮어써, 먼저 큐된 시작이 잘못된 provider로 평가된다.
	# 같은 프레임 연속 호출은 마지막 요청만 시작한다(latest-wins).
	_pending_start = {
		"resource": dialogue_resource,
		"read_provider": read_state_provider,
		"mutation_provider": mutation_state_provider,
	}
	if not _start_scheduled:
		_start_scheduled = true
		_deferred_start.call_deferred()


# deferred 단일 dispatcher: 마지막 pending 요청만 resource+read+mutation provider를 함께 바인딩해 시작한다.
func _deferred_start() -> void:
	_start_scheduled = false
	if _pending_start.is_empty():
		return
	var req: Dictionary = _pending_start
	_pending_start = {}
	dialogue_player.set_read_state_provider(req["read_provider"])
	dialogue_player.set_mutation_state_provider(req["mutation_provider"])
	dialogue_player.start_dialogue(req["resource"])


# 폐기되는 UI의 대기 중 deferred 시작을 취소한다(Manager가 같은 프레임에 교체할 때 사용).
# 이미 큐된 _deferred_start가 돌더라도 pending이 비어 있어 시작/평가하지 않는다.
func cancel_pending_start() -> void:
	_pending_start = {}

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
