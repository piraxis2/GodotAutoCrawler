@tool
class_name WorldStateReadDef extends DataDefinition
## State Read Data 노드 (DT-013 Step 2, ADR-015 D1/D2/D6).
##
## WorldState 단일 key 값을 Dialogue Data Flow에 공급하는 leaf Data 노드다. ConditionSet처럼 boolean만
## 내는 노드가 아니라 bool/int/float/String/StringName 값을 그대로 Branch/Choice 조건/Expression 입력에
## 공급한다. 런타임 평가는 이 Definition이 아니라 DialoguePlayer._eval_data()의 state_read 분기가 수행한다
## (Step 1): 주입된 read provider에서 key를 strict typeof로 읽어 Data value로 돌려준다. 이 Definition은
## provider를 모르고 어떤 평가/lookup도 하지 않는다(순수 데이터).
##
## 이름은 DT-007 leaf Resource인 전역 클래스 StateCondition / DT-008 WorldStateConditionDef와 같은
## WorldState 계열 명명을 따른다(노드 목록/타이틀 표시 이름은 class_name에서 "Def"를 떼어 "WorldStateRead").
##
## output port는 항상 generic data 1개다(BOOL도 boolean port로 바꾸지 않음 — ADR-015 D2). editor가
## data↔boolean 연결을 호환 처리하므로 Branch/Choice boolean 조건 입력에도 연결된다. expected type은
## 런타임 strict validation 역할이지 포트 카테고리가 아니다.

# 런타임 expected type으로 허용하는 5타입(Variant.Type 상수). value_type 단일 source of truth.
const READ_VALUE_TYPES: Array = [TYPE_BOOL, TYPE_INT, TYPE_FLOAT, TYPE_STRING, TYPE_STRING_NAME]

# WorldState key 문자열. 빈 값/형식 불일치는 저장 검증에서 차단한다(validate_structure).
@export var key: StringName = &""
# 런타임 expected type. Variant.Type(TYPE_BOOL/INT/FLOAT/STRING/STRING_NAME 중 하나).
@export var value_type: int = TYPE_BOOL

# key 형식 source of truth = StateSchema.KEY_PATTERN(ConditionValidator와 동일). schema lookup은 하지 않고
# 형식만 검사한다(D6: editor는 provider-free, 실제 key 존재는 runtime provider가 판정).
static var _key_regex: RegEx


func get_runtime_type() -> StringName:
	return &"state_read"


func get_runtime_params() -> Dictionary:
	# 값은 변환하지 않고 그대로 넘긴다. 런타임이 read provider에서 strict typeof로 읽는다(Step 1).
	return {
		"key": key,
		"value_type": value_type,
	}


func _get_dialogue_node() -> String:
	return "res://addons/dialogtool/Node/world_state_read_node.tscn"


func _node_init(node: DialogueNode) -> void:
	# 에디터 Adapter가 data output 슬롯과 key/type 위젯을 구성한다. 캐시가 낡아 adapter가 null이어도
	# UI만 비고 graceful degrade(런타임 평가는 영향 없음).
	var adapter := get_editor_adapter()
	if adapter:
		adapter.apply_params(node, {"key": key, "value_type": value_type})


func _capture(node: DialogueNode) -> void:
	var adapter := get_editor_adapter()
	if adapter == null:
		return
	var params: Dictionary = adapter.capture_params(node)
	if params.has("key"):
		key = params["key"]
	if params.has("value_type"):
		value_type = params["value_type"]


func _get_data_output(_port: int) -> Variant:
	# 에디터/Build 미리보기 경로. 런타임 read는 주입된 read provider가 필요하므로 여기서는 읽지 않고
	# null을 반환한다. 실제 값은 DialoguePlayer가 런타임에 provider로 읽는다(ADR-015 D3).
	return null


# 저장 검증용 구조 검사(provider-free, D6). ok면 "", 아니면 사람이 읽을 오류 문자열을 반환한다.
# value_type이 허용 5타입 밖이거나, key가 비었거나 StateSchema.KEY_PATTERN과 맞지 않으면 저장을 막는다.
func validate_structure() -> String:
	if not (value_type in READ_VALUE_TYPES):
		return "value_type이 허용되지 않습니다(type=%d). BOOL/INT/FLOAT/STRING/STRING_NAME만 가능합니다." % value_type
	var key_str := String(key)
	if key_str.is_empty():
		return "State key가 비어 있습니다."
	if _get_key_regex().search(key_str) == null:
		return "State key '%s'가 WorldState key 형식(%s)과 맞지 않습니다." % [key_str, StateSchema.KEY_PATTERN]
	return ""


static func _get_key_regex() -> RegEx:
	if _key_regex == null:
		_key_regex = RegEx.new()
		_key_regex.compile(StateSchema.KEY_PATTERN)
	return _key_regex


# 그래프 표시용 타입 라벨(OptionButton 항목 + summary 공유). type_string은 소문자라 ADR Editor Node Shape의
# 대문자 표기(BOOL/INT/FLOAT/STRING/STRING_NAME)를 별도 맵으로 둔다.
static func type_label(t: int) -> String:
	match t:
		TYPE_BOOL: return "BOOL"
		TYPE_INT: return "INT"
		TYPE_FLOAT: return "FLOAT"
		TYPE_STRING: return "STRING"
		TYPE_STRING_NAME: return "STRING_NAME"
		_: return "type %d" % t
