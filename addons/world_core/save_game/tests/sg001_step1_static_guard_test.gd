# SG-001 Step 1 정적 가드 테스트.
# 실행:
#   godot --headless --path <project> res://addons/world_core/save_game/tests/sg001_step1_static_guard_test.tscn
#
# 목적: SaveGame core 제품 코드(SaveSection / SaveGameManager)가 WorldState / DialogueTool 등
# domain-specific system을 직접 참조하지 않는다는 경계를 정적으로 보장한다(ADR-013).
# core 제품 코드 파일의 소스 텍스트를 읽어 금지 식별자/경로가 없는지 확인한다.
extends Node

const CORE_FILES := [
	"res://addons/world_core/save_game/save_section.gd",
	"res://addons/world_core/save_game/save_game_manager.gd",
]

# 금지 토큰: domain class_name / 경로 / preload 흔적.
const FORBIDDEN := [
	"WorldState",
	"WorldStateStore",
	"WorldStateRuntime",
	"StateSchema",
	"StateDefinition",
	"ConditionSet",
	"ConditionEvaluator",
	"DialogueTool",
	"DialoguePlayer",
	"DialogueManager",
	"dialogtool",
	"world_state",
]

var _failures: int = 0


func _ready() -> void:
	for path in CORE_FILES:
		_scan(path)
	if _failures == 0:
		print("[SG-001 Step1 StaticGuard] ALL PASS")
		get_tree().quit(0)
	else:
		print("[SG-001 Step1 StaticGuard] FAILED: %d violation(s)" % _failures)
		get_tree().quit(1)


func _scan(path: String) -> void:
	print("[scan] %s" % path)
	if not FileAccess.file_exists(path):
		_failures += 1
		print("  FAIL file missing: %s" % path)
		return
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		_failures += 1
		print("  FAIL cannot open: %s" % path)
		return
	var text := f.get_as_text()
	f.close()
	# 주석(설계 경계를 설명하는 docstring 포함)은 제외하고 실제 코드만 검사한다.
	# core 소스에는 문자열 리터럴 안에 '#'이 없으므로 각 줄에서 첫 '#' 이후를 잘라낸다.
	var code := _strip_comments(text)
	for token in FORBIDDEN:
		if code.find(token) != -1:
			_failures += 1
			print("  FAIL found forbidden token '%s' in code of %s" % [token, path])
		else:
			print("  PASS no '%s'" % token)


func _strip_comments(text: String) -> String:
	var out := ""
	for line in text.split("\n"):
		var hash_at := (line as String).find("#")
		if hash_at != -1:
			line = (line as String).substr(0, hash_at)
		out += line + "\n"
	return out
