@tool
class_name NodeTypeRegistry extends RefCounted

# 노드의 런타임 타입(StringName, DialogueDefinition.get_runtime_type와 일치)에서
# 에디터 어댑터로의 중앙 조회.
#
# 어댑터는 class_name이 아니라 preload(경로)로 생성한다 — 그래야 전역 클래스
# 캐시가 낡아도(새 스크립트가 아직 등록되지 않아도) 어댑터를 로드할 수 있어,
# 에디터 재시작 없이 노드 UI가 정상 동작한다.

const _ADAPTER_DIR := "res://addons/dialogtool/Editor/Adapter/"

static var _adapters: Dictionary = {}
static var _initialized := false


static func _ensure_registered() -> void:
	if _initialized:
		return
	_initialized = true
	_adapters[&"start"] = load(_ADAPTER_DIR + "start_editor_adapter.gd").new()
	_adapters[&"end"] = load(_ADAPTER_DIR + "end_editor_adapter.gd").new()
	_adapters[&"branch"] = load(_ADAPTER_DIR + "branch_editor_adapter.gd").new()
	_adapters[&"say"] = load(_ADAPTER_DIR + "say_editor_adapter.gd").new()
	_adapters[&"choice"] = load(_ADAPTER_DIR + "choice_editor_adapter.gd").new()
	_adapters[&"variable"] = load(_ADAPTER_DIR + "variable_editor_adapter.gd").new()
	_adapters[&"expression"] = load(_ADAPTER_DIR + "expression_editor_adapter.gd").new()
	_adapters[&"autoload"] = load(_ADAPTER_DIR + "autoload_editor_adapter.gd").new()
	_adapters[&"scene_function"] = load(_ADAPTER_DIR + "scene_function_editor_adapter.gd").new()
	_adapters[&"description"] = load(_ADAPTER_DIR + "description_editor_adapter.gd").new()
	_adapters[&"test"] = load(_ADAPTER_DIR + "test_editor_adapter.gd").new()


static func get_adapter(type: StringName) -> Variant:
	_ensure_registered()
	return _adapters.get(type, null)


static func has_adapter(type: StringName) -> bool:
	_ensure_registered()
	return _adapters.has(type)
