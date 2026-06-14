@tool
class_name StateDefinition extends Resource
## 등록된 게임 상태 key 하나의 타입/default/lifetime 계약 (DT-005).
##
## Step 1 범위에서는 선언과 타입 매핑만 담당한다. runtime 값, mutation, snapshot은
## 보관하지 않는다(WorldStateStore의 책임, 후속 Step).
##
## value_type/lifetime은 Godot 4에 전역 enum이 없으므로 이 클래스 안에 named enum으로
## 두고, 다른 스크립트에서는 StateDefinition.StateValueType 형태로 참조한다.

enum StateValueType { BOOL, INT, FLOAT, STRING, STRING_NAME }
enum StateLifetime { SAVE, SESSION }

# 모든 필드 쓰기는 Resource의 changed 시그널을 발행한다. StateSchema가 이를 구독해
# 검증 후 deep mutation(예: schema.definitions[0].key = ...)이 일어나면 lookup을
# 무효화하고 다음 접근에서 재검증하게 한다. 이로써 오래된 lookup을 신뢰하지 않는다.
@export var key: StringName : set = _set_key
@export var value_type: StateValueType : set = _set_value_type
@export var default_value: Variant : set = _set_default_value
@export var lifetime: StateLifetime = StateLifetime.SAVE : set = _set_lifetime
@export var writable: bool = true : set = _set_writable
@export_multiline var description: String : set = _set_description
@export var tags: Array[StringName] = [] : set = _set_tags


func _set_key(v: StringName) -> void:
	key = v
	emit_changed()


func _set_value_type(v: StateValueType) -> void:
	value_type = v
	emit_changed()


func _set_default_value(v: Variant) -> void:
	default_value = v
	emit_changed()


func _set_lifetime(v: StateLifetime) -> void:
	lifetime = v
	emit_changed()


func _set_writable(v: bool) -> void:
	writable = v
	emit_changed()


func _set_description(v: String) -> void:
	description = v
	emit_changed()


func _set_tags(v: Array[StringName]) -> void:
	tags = v
	emit_changed()


## StateValueType -> 내장 Variant 타입(TYPE_*). 알 수 없는 enum이면 TYPE_NIL.
static func builtin_type_for(vt: int) -> int:
	match vt:
		StateValueType.BOOL: return TYPE_BOOL
		StateValueType.INT: return TYPE_INT
		StateValueType.FLOAT: return TYPE_FLOAT
		StateValueType.STRING: return TYPE_STRING
		StateValueType.STRING_NAME: return TYPE_STRING_NAME
		_: return TYPE_NIL


## value_type가 지원되는 enum 범위 안에 있는가.
static func is_known_value_type(vt: int) -> bool:
	return vt >= StateValueType.BOOL and vt <= StateValueType.STRING_NAME


## lifetime이 지원되는 enum 범위 안에 있는가.
static func is_known_lifetime(lt: int) -> bool:
	return lt == StateLifetime.SAVE or lt == StateLifetime.SESSION
