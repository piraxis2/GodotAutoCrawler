extends Node

# 게임 코드에서 대화를 한 줄로 실행하는 전역 API (오토로드).
#
# 사용 (GDScript):
#   DialogueManager.dialogue_end.connect(_on_dialogue_done)
#   DialogueManager.play(load("res://my_dialogue.tres"))
#
# 사용 (C#):
#   var dm = GetNode("/root/DialogueManager");
#   dm.Connect("dialogue_end", Callable.From(OnDialogueDone));
#   dm.Call("play", resource);
#
# DialogueUI 씬을 CanvasLayer로 최상단에 띄우고, 종료되면 자동으로 정리한다.

const DIALOGUE_UI_SCENE := "res://addons/dialogtool/UI/Dialogue_UI.tscn"

signal dialogue_started
signal dialogue_end
signal ui_request(request_data: Dictionary)

var _layer: CanvasLayer = null
var _ui: DialogueUI = null


# 리소스 하나를 넘겨 대화를 시작한다. 진행 중이던 대화는 정리된다.
func play(dialogue_resource: DialogueGraphResource) -> void:
	if dialogue_resource == null:
		push_error("DialogueManager: dialogue_resource is null.")
		return

	_dismiss()

	_layer = CanvasLayer.new()
	_layer.layer = 128 # 게임 UI 위에 표시
	add_child(_layer)

	_ui = load(DIALOGUE_UI_SCENE).instantiate()
	_layer.add_child(_ui)

	_ui.dialogue_started.connect(dialogue_started.emit)
	_ui.ui_request.connect(ui_request.emit)
	# 종료 신호에 발신 UI를 바인딩 — 교체된 이전 대화의 지연 신호를 식별해 무시한다.
	_ui.dialogue_end.connect(_on_end.bind(_ui))

	_ui.play(dialogue_resource)


# 현재 대화가 표시 중인지.
func is_playing() -> bool:
	return _ui != null and is_instance_valid(_ui)


func _on_end(source_ui) -> void:
	# play()로 이미 다른 대화로 교체된 뒤 도착한 지연 신호면 무시한다. (P2)
	if source_ui != _ui:
		return
	# 종료 핸들러가 곧바로 새 대화를 시작해도 그 새 대화를 지우지 않도록,
	# 먼저 정리한 뒤 신호를 발신한다. (P1)
	_dismiss()
	dialogue_end.emit()


func _dismiss() -> void:
	if _layer != null and is_instance_valid(_layer):
		_layer.queue_free()
	_layer = null
	_ui = null

# --- 설계만 남겨둔 부분 (이번 범위 밖) ---
# 부작용 기능(SceneFunction 실행, Autoload 프로퍼티 write 등)은 아직 넣지 않는다.
# 향후 DialoguePlayer의 data evaluator(_get_data_value)에 다음을 추가하는 식으로 확장:
#   - &"scene_function": 현재 씬의 함수를 호출해 결과를 반환
#   - &"autoload": "/root/<name>" 의 프로퍼티를 read; write는 별도 "effect" 노드로
# write/effect는 실행 시점·되돌리기(undo)·세이브 영향이 있어 별도 설계가 필요하므로,
# 지금은 read/표현 중심의 안전한 실행만 제공한다.
