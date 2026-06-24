extends Node

# 전투/이벤트 씬에서 대화를 실행하는 최소 예제 (참고용 — 어디에도 자동 연결되지 않음).
#
# 핵심: 리소스 하나를 DialogueManager.play()에 넘기고, dialogue_end를 기다린다.

@export var dialogue_resource: DialogueGraphResource


# 예: 이벤트 트리거(전투 시작 전 컷씬 등)에서 호출.
func start_event_dialogue() -> void:
	if dialogue_resource == null:
		return
	# 한 번만 받도록 CONNECT_ONE_SHOT.
	DialogueManager.dialogue_end.connect(_on_dialogue_done, CONNECT_ONE_SHOT)
	DialogueManager.play(dialogue_resource)


func _on_dialogue_done() -> void:
	# 대화가 끝난 뒤 진행할 게임 로직 (예: 전투 시작, 보상 지급, 다음 페이즈).
	print("dialogue finished -> resume gameplay")


# 대화 도중 표현 이벤트가 필요하면 ui_request를 구독한다(초상화 교체 등).
func _subscribe_ui_request_example() -> void:
	DialogueManager.ui_request.connect(func(req):
		if req.get("type") == "display_text":
			var portrait: String = req.get("portrait", "")
			# 여기서 초상화/사운드 등 부가 표현 처리
			pass
	)


# --- C# 사용 예 (참고) ---
#
#   public partial class BattleEvent : Node
#   {
#       [Export] public Resource DialogueResource;
#
#       public void StartEventDialogue()
#       {
#           var dm = GetNode("/root/DialogueManager");
#           dm.Connect("dialogue_end", Callable.From(OnDialogueDone),
#                      (uint)GodotObject.ConnectFlags.OneShot);
#           dm.Call("play", DialogueResource);
#       }
#
#       private void OnDialogueDone() => GD.Print("dialogue finished -> resume gameplay");
#   }
