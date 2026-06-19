@tool
@abstract class_name StateEffectDef extends FlowDefinition

# State mutation Effect 노드(state_set/state_add)의 공통 베이스 (DT-009 Step 3, ADR-010 D7).
#
# Portrait와 같은 비대기 leaf Effect 명령이다: Effect 입력 포트만 가지며 실행 커서를 옮기거나
# wait state를 만들지 않는다. 실제 런타임 실행은 DialoguePlayer._run_effects의 타입 디스패치가
# runtime snapshot의 type/params로 수행한다(Step 2). 이 Definition은 mutation provider를 모른다.
#
# Abstract/ 폴더에 두어 노드 목록(dialogue_node_item_list.gd) 검색에서 제외한다.
# 에디터 UI는 Definition이 직접 만들지 않고 공유 state_effect_editor_adapter에 위임한다(ADR-002).
#
# 타입 보존(D7): literal/delta의 typeof()가 capture→save→reload→runtime snapshot에서 보존돼야 한다.
# 값은 capture 시점에 선택 타입으로 엄격히 파싱해 typed 값으로 저장하고, .tres Variant 직렬화가 typeof를
# 그대로 보존한다(DT-007/008 expected_value 왕복으로 확인됨). 런타임은 값을 변환하지 않고 그대로 넘기며,
# 타입 불일치는 Store가 strict하게 type_mismatch로 거부한다(ADR-010 — 조용한 변환 금지).

# Set은 Store 허용 5타입, Add는 숫자 2타입만 literal로 만들 수 있다.
const SET_VALUE_TYPES: Array = [TYPE_BOOL, TYPE_INT, TYPE_FLOAT, TYPE_STRING, TYPE_STRING_NAME]
const ADD_VALUE_TYPES: Array = [TYPE_INT, TYPE_FLOAT]

@export var key: StringName = &""


# leaf 비대기 명령: 런타임 실행은 snapshot 기반(_run_effects)이라 레거시 execute 경로는 안 쓴다.
func _is_done() -> bool:
	return true


func execute(_dialogue_player: Node) -> FlowDefinition:
	return null


# capture: 잘못된 literal은 저장 검증에서 차단해야 한다(""). ok면 "" 반환.
@abstract func validate_literal() -> String


# 에디터 텍스트 입력(String)을 선택 타입의 typed 값으로 엄격히 파싱한다(capture 경로).
# 숫자/BOOL이 형식에 맞지 않으면 조용히 0/false로 만들지 않고 원본 String을 그대로 둔다 —
# 그러면 typeof가 선택 타입과 어긋나 validate_literal이 저장을 막고, 런타임에선 Store가 거부한다.
static func coerce_text(text: String, t: int) -> Variant:
	var s := text.strip_edges()
	match t:
		TYPE_BOOL:
			var low := s.to_lower()
			if low == "true":
				return true
			if low == "false":
				return false
			return text   # 잘못된 BOOL 입력 → 원본 보존(타입 불일치)
		TYPE_INT:
			return s.to_int() if s.is_valid_int() else text
		TYPE_FLOAT:
			return s.to_float() if s.is_valid_float() else text
		TYPE_STRING:
			return text
		TYPE_STRING_NAME:
			return StringName(text)
	return text


# value/delta의 typeof가 선언 타입과 일치하고, 선언 타입이 허용 목록 안인지 검증한다.
# value_label은 오류 메시지용("value" 또는 "delta").
static func literal_error(value: Variant, declared_type: int, allowed: Array, value_label: String) -> String:
	if not (declared_type in allowed):
		return "%s 타입이 허용되지 않습니다(type=%d)" % [value_label, declared_type]
	if typeof(value) != declared_type:
		return "%s '%s'가 %s 타입이 아닙니다(잘못된 literal)" % [value_label, str(value), type_string(declared_type)]
	return ""
