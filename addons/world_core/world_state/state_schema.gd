@tool
class_name StateSchema extends Resource
## 등록된 StateDefinition 집합과 그 validation/lookup (DT-005).
##
## 책임:
## - key 형식/중복/공백 검사
## - default value 타입 검사 (암시적 변환 금지, 정확히 일치)
## - value_type/lifetime enum 범위 검사
## - schema_version 검사
## - 구조화된 validation 결과 제공
## - 검증을 모두 통과한 경우에만 key -> StateDefinition lookup 공개
##
## 오류가 하나라도 있으면 부분 lookup을 만들지 않는다. invalid schema는
## 빈 lookup만 제공한다.
##
## Step 1 범위: runtime 값/mutation/snapshot은 없다(WorldStateStore, 후속 Step).

## canonical key 문법. lower snake case dot path, 최소 두 segment(첫 segment = namespace).
const KEY_PATTERN := "^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)+$"

# 필드가 바뀌면 검증 결과를 무효화한다(setter -> invalidate).
@export var schema_version: int = 1 : set = _set_schema_version
@export var definitions: Array[StateDefinition] = [] : set = _set_definitions

# validation을 통과한 경우에만 채워진다. 실패 시 항상 비어 있다.
var _lookup: Dictionary = {}
var _validated: bool = false
var _last_result: Dictionary = {}
# changed 시그널을 구독 중인 Definition들. 재검증 시 구독을 현재 definitions에 맞춘다.
var _watched: Array[StateDefinition] = []
# 마지막 검증 시점의 definitions 구조 지문. in-place 배열 변경(append/erase/remove_at/
# 인덱스 대입)은 setter를 거치지 않으므로, 접근 시 이 지문과 비교해 재검증을 강제한다.
var _watch_size: int = -1
var _watch_hash: int = 0

static var _key_regex: RegEx


func _set_schema_version(v: int) -> void:
	schema_version = v
	invalidate()


func _set_definitions(v: Array[StateDefinition]) -> void:
	definitions = v
	invalidate()


## 검증 결과와 lookup을 버린다. 다음 접근 시 재검증된다.
func invalidate() -> void:
	_validated = false
	_lookup = {}
	_last_result = {}


# 현재 definitions의 changed 시그널만 구독하도록 정리한다.
# 검증 후 Definition 필드가 바뀌면(_set_* -> emit_changed) invalidate가 호출돼
# 오래된 lookup을 신뢰하지 않는다.
func _rewatch() -> void:
	for d in _watched:
		if d != null and d.changed.is_connected(_on_definition_changed):
			d.changed.disconnect(_on_definition_changed)
	_watched.clear()
	for d in definitions:
		if d != null:
			if not d.changed.is_connected(_on_definition_changed):
				d.changed.connect(_on_definition_changed)
			_watched.append(d)


func _on_definition_changed() -> void:
	invalidate()


static func _get_key_regex() -> RegEx:
	if _key_regex == null:
		_key_regex = RegEx.new()
		_key_regex.compile(KEY_PATTERN)
	return _key_regex


## schema 전체를 검증하고 구조화된 결과를 반환한다.
##
## 결과 구조:
## {
##   "valid": bool,
##   "errors": Array[Dictionary],   # {code, index, key, message}
##   "error_codes": Array[String],  # 빠른 단언용 code 목록
##   "key_count": int,              # 통과 시 lookup 크기, 실패 시 0
## }
##
## 오류가 하나라도 있으면 lookup을 만들지 않는다(부분 공개 금지).
func validate() -> Dictionary:
	# 현재 definitions에 맞춰 changed 구독을 갱신한다(deep mutation 감지).
	_rewatch()

	var errors: Array[Dictionary] = []

	if schema_version < 1:
		errors.append(_error("schema_version_invalid", -1, &"",
			"schema_version must be >= 1 (got %d)" % schema_version))

	var regex := _get_key_regex()
	var seen: Dictionary = {}

	for i in definitions.size():
		var d: StateDefinition = definitions[i]
		if d == null:
			errors.append(_error("definition_null", i, &"",
				"definition at index %d is null" % i))
			continue

		if not StateDefinition.is_known_value_type(d.value_type):
			errors.append(_error("value_type_invalid", i, d.key,
				"unknown value_type enum %d" % d.value_type))
		if not StateDefinition.is_known_lifetime(d.lifetime):
			errors.append(_error("lifetime_invalid", i, d.key,
				"unknown lifetime enum %d" % d.lifetime))

		var key_str := String(d.key)
		if key_str.is_empty():
			errors.append(_error("key_empty", i, d.key, "key is empty"))
		elif regex.search(key_str) == null:
			errors.append(_error("key_invalid_format", i, d.key,
				"key '%s' does not match %s" % [key_str, KEY_PATTERN]))
		elif seen.has(d.key):
			errors.append(_error("key_duplicate", i, d.key,
				"duplicate key '%s' (first at index %d)" % [key_str, seen[d.key]]))
		else:
			seen[d.key] = i

		# default 타입은 value_type을 알 수 있을 때만 검사한다(매핑 불가 시 중복 보고 방지).
		if StateDefinition.is_known_value_type(d.value_type):
			var expected := StateDefinition.builtin_type_for(d.value_type)
			var actual := typeof(d.default_value)
			if actual != expected:
				errors.append(_error("default_type_mismatch", i, d.key,
					"default_value builtin type %d does not match value_type %d (expected %d)"
						% [actual, d.value_type, expected]))

	var valid := errors.is_empty()
	_lookup = {}
	if valid:
		for d in definitions:
			_lookup[d.key] = d
	_validated = true

	var error_codes: Array[String] = []
	for e in errors:
		error_codes.append(e["code"])

	_last_result = {
		"valid": valid,
		"errors": errors,
		"error_codes": error_codes,
		"key_count": _lookup.size(),
	}
	# 다음 접근에서 in-place 배열 변경을 감지하기 위한 구조 지문을 기록한다.
	_watch_size = definitions.size()
	_watch_hash = definitions.hash()
	# 호출자가 결과를 변조해도 내부 상태(is_valid 등)가 바뀌지 않도록 deep copy를 반환한다.
	return _last_result.duplicate(true)


func _error(code: String, index: int, key: StringName, message: String) -> Dictionary:
	return {"code": code, "index": index, "key": key, "message": message}


## 검증을 통과했는가. 아직 검증하지 않았으면 한 번 검증한다.
func is_valid() -> bool:
	_ensure_validated()
	return _last_result.get("valid", false)


## key가 lookup에 있는가. invalid schema에서는 항상 false.
func has_key(key: StringName) -> bool:
	_ensure_validated()
	return _lookup.has(key)


## key에 해당하는 StateDefinition. 없으면(또는 invalid schema면) null.
func get_definition(key: StringName) -> StateDefinition:
	_ensure_validated()
	return _lookup.get(key, null)


## 등록된 key 목록. invalid schema에서는 빈 배열.
func keys() -> Array:
	_ensure_validated()
	return _lookup.keys()


## 마지막 validate() 결과(없으면 검증 후 반환). 변조 방지를 위해 deep copy를 반환한다.
func last_result() -> Dictionary:
	_ensure_validated()
	return _last_result.duplicate(true)


func _ensure_validated() -> void:
	# 검증이 없거나, setter를 거치지 않은 in-place 배열 변경(크기 또는 구조 지문 변화)이
	# 감지되면 재검증한다. size 비교(O(1))가 append/erase/remove_at의 흔한 경우를
	# 단락 처리하고, hash 비교가 인덱스 대입까지 잡는다.
	if _validated and definitions.size() == _watch_size and definitions.hash() == _watch_hash:
		return
	validate()
