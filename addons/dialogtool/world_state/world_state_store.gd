class_name WorldStateStore extends Node
## 등록된 게임 상태의 runtime 값을 보관하는 타입 안전한 store (DT-005 Step 2~3).
##
## 유효한 StateSchema로 초기화된 경우에만 ready 상태가 된다. 등록된 key만 읽고 쓸 수
## 있으며, set은 strict type validation을 거친다(암시적 변환 금지). 잘못된 set은 값을
## 바꾸지 않고 Error를 반환한다.
##
## Step 2 범위: read/write/reset + value_changed.
## Step 3 범위: SAVE/SESSION lifetime 구분, reset_lifetime, snapshot export/import.
## Step 4 범위: atomic mutation batch(apply_batch).
## Dialogue provider seam(Step 5)은 범위 밖이다.
##
## 영속 경계: Store는 파일 경로/저장 슬롯을 모른다. JSON 호환 snapshot의 export/import만
## 제공하고 실제 직렬화/파일 저장은 외부 SaveGame/PlayerData가 담당한다.
##
## JSON-safe 도메인: snapshot이 JSON 호환이므로 INT는 ±(2^53-1) 범위, FLOAT는 finite만 허용한다.
## 이 제약을 쓰기 경계 전체에서 강제한다 — 범위 밖 schema_version/default는 ready를 거부하고,
## set_value/import도 도메인 위반을 거부한다. 따라서 ready Store의 모든 값은 항상 JSON snapshot으로
## 무손실 export 가능하다.
##
## 알림 transaction 정책: value_changed 발행 중에는 mutation(set/reset/reset_lifetime/import)을
## 거부한다(ERR_BUSY 등). 다중 key 작업이 모든 값을 먼저 반영한 뒤 신호를 모아 발행하므로, 알림
## 도중 재진입 mutation을 막아 이미 staged된 event가 변형된 값과 함께 뒤늦게 발행되는 것을 방지한다.
##
## autoload .tscn으로 쓸 수 있도록 schema를 @export로 노출하지만, 테스트 주입을 위해
## new() + schema 설정 + initialize() 경로도 지원한다.
##
## 계약 고정: initialize()는 schema에서 runtime 계약(타입/default/lifetime/writable)을 private
## map으로 compile하고, 이후 read/write/reset/snapshot은 mutable한 StateSchema/StateDefinition을
## 다시 조회하지 않는다. 따라서 초기화 후 schema를 교체하거나 key/타입/default/writable을 바꿔도
## Store는 기존 계약을 유지하며, 변경 반영은 initialize() 재호출(명시적 재초기화)로만 이뤄진다.
## 이로써 두 계약이 섞이는 문제와 runtime hot path의 schema 재조회 비용을 모두 피한다.

## gameplay/시스템 변경으로 값이 실제로 바뀐 경우에만 발행한다(같은 값 set/reset은 무발행).
signal value_changed(key: StringName, old_value: Variant, new_value: Variant)
## reset_lifetime이 해당 lifetime 전체를 default로 되돌린 뒤 1회 발행한다.
signal state_reset(lifetime: StateDefinition.StateLifetime)
## import_snapshot 종료 시 applied/ignored/errors report와 함께 발행한다.
signal snapshot_imported(report: Dictionary)

@export var schema: StateSchema

var _ready_state: bool = false
var _values: Dictionary = {}    # StringName -> Variant (runtime 값)
# compile된 runtime 계약.
# StringName -> { builtin_type:int, default:Variant, lifetime:int, writable:bool }.
# initialize() 시점의 schema 스냅샷이며 이후 schema 변경과 독립적이다.
var _contract: Dictionary = {}
var _schema_version: int = 0    # compile 시점의 schema_version 스냅샷
# value_changed 알림 발행 중 여부. 알림 중 재진입 mutation을 차단해(transaction 정책) 이미
# staged된 batch event가 뒤늦게 변형된 값과 함께 발행되는 것을 막는다.
var _in_notification: bool = false


func _ready() -> void:
	initialize()


## 현재 schema로 store를 초기화한다. 유효한 schema일 때만 ready가 되고, 계약을 compile한 뒤
## default 값을 채운다. 반환: 성공 여부. 실패 시 store는 not-ready이며 _values/_contract는 비어 있다.
func initialize() -> bool:
	# 알림 transaction: value_changed 발행 중에는 재초기화도 금지한다. 이 검사는 기존 상태를
	# 비우기 전에 수행해야 in-flight batch가 손상되지 않는다.
	if _in_notification:
		push_error("WorldStateStore: initialize during value_changed notification; ignored")
		return false

	_ready_state = false
	_values = {}
	_contract = {}
	_schema_version = 0

	if schema == null:
		push_error("WorldStateStore: schema is null; store not ready")
		return false
	if not schema.is_valid():
		push_error("WorldStateStore: schema is invalid; store not ready")
		return false

	# JSON-safe 정책: snapshot으로 보존 불가능한 schema_version은 ready 상태를 거부한다.
	if schema.schema_version < 1 or schema.schema_version > JSON_SAFE_INT_MAX:
		push_error("WorldStateStore: schema_version %d out of JSON-safe range; store not ready"
			% schema.schema_version)
		return false

	_schema_version = schema.schema_version
	for key in schema.keys():
		var d: StateDefinition = schema.get_definition(key)
		var bt := StateDefinition.builtin_type_for(d.value_type)
		# JSON-safe 정책: default가 도메인(INT safe 범위 / finite FLOAT)을 벗어나면 ready를 거부한다.
		# 이로써 ready Store의 모든 값이 JSON snapshot으로 무손실 export 가능함을 보장한다.
		if not _value_in_domain(bt, d.default_value):
			push_error("WorldStateStore: default for '%s' out of JSON-safe domain; store not ready" % key)
			_values = {}
			_contract = {}
			_schema_version = 0
			return false
		# value 타입은 모두 value-type이라 대입이 복사다(default 스냅샷이 schema와 독립).
		_contract[key] = {
			"builtin_type": bt,
			"default": d.default_value,
			"lifetime": d.lifetime,
			"writable": d.writable,
		}
		_values[key] = d.default_value
	_ready_state = true
	return true


## 유효한 schema로 초기화돼 사용 가능한 상태인가.
func is_store_ready() -> bool:
	return _ready_state


# --- Provider 계약 (DT-005 Step 5) -----------------------------------
# Dialogue/조건 평가 계층에 노출하는 좁은 view. Store 전체 API보다 작은 표면을 쓴다.
# read provider와 mutation provider를 분리해, 읽기만 필요한 ConditionEvaluator가 쓰기 API에
# 의존하지 않게 한다. WorldStateStore가 두 계약을 모두 구현하므로 그대로 주입할 수 있다.

## read provider: 등록 여부.
func has_state(key: StringName) -> bool:
	return has_key(key)


## read provider: 값 읽기(미등록/not-ready면 null).
func read_state(key: StringName) -> Variant:
	return get_value(key)


## read provider: 실패 허용 값 읽기.
func try_read_state(key: StringName, fallback: Variant = null) -> Variant:
	return try_get_value(key, fallback)


## mutation provider: 단일 gameplay 변경.
func set_state(key: StringName, value: Variant) -> Error:
	return set_value(key, value)


## mutation provider: atomic 일괄 변경.
func apply_state_batch(changes: Array[Dictionary]) -> Dictionary:
	return apply_batch(changes)


## mutation provider: 숫자 state에 대한 원자적 Add (DT-009 Step 1, ADR-010 D3/D4).
##
## 현재 값 read, strict numeric type 확인, overflow/domain 확인, commit을 Store 내부에서
## 한 transaction으로 수행한다(read→calculate→set 분리 금지). INT state에는 정확히 INT delta,
## FLOAT state에는 정확히 FLOAT delta만 허용하며 int↔float 암시 변환은 없다. BOOL/String/
## StringName 등 비숫자 state는 거부한다.
##
## 실패(not-ready/busy/unknown/read-only/type/domain)는 값과 signal을 모두 바꾸지 않는다.
## delta가 0이거나 결과가 기존 값과 같으면 성공하되 변경 없이(changed=false) value_changed를
## 발행하지 않는다. 실제로 값이 바뀐 경우에만 기존 commit 경계(_stage/_emit_changes)를 재사용해
## value_changed를 정확히 1회 발행한다.
##
## 반환은 authoritative report Dictionary다(ADR-010 D10 계약):
## { applied: bool, changed: bool, operation: "add", key: StringName,
##   old_value: Variant, new_value: Variant, error: StringName }
## old_value/new_value는 Store가 확정한 값이며, 성공 시에만 채운다(실패 시 null).
## error reason(StringName): store_not_ready / store_busy / unknown_key / read_only /
##   type_mismatch / out_of_domain. 성공 시 &"".
func add_state(key: StringName, delta: Variant) -> Dictionary:
	var report: Dictionary = {
		"applied": false,
		"changed": false,
		"operation": "add",
		"key": key,
		"old_value": null,
		"new_value": null,
		"error": &"",
	}

	if not _ready_state:
		push_error("WorldStateStore: add_state on not-ready store (key '%s')" % key)
		report["error"] = &"store_not_ready"
		return report
	if _in_notification:
		push_error("WorldStateStore: add_state during value_changed notification (key '%s')" % key)
		report["error"] = &"store_busy"
		return report
	if not _contract.has(key):
		push_error("WorldStateStore: add_state on unregistered key '%s'" % key)
		report["error"] = &"unknown_key"
		return report
	var c: Dictionary = _contract[key]
	if not c["writable"]:
		push_error("WorldStateStore: add_state denied on read-only key '%s'" % key)
		report["error"] = &"read_only"
		return report
	var bt: int = c["builtin_type"]
	# 숫자 state(INT/FLOAT)만 Add 가능하다. 비숫자 state는 타입 불일치로 거부한다.
	if bt != TYPE_INT and bt != TYPE_FLOAT:
		push_error("WorldStateStore: add_state on non-numeric key '%s'" % key)
		report["error"] = &"type_mismatch"
		return report
	# delta는 state 타입과 정확히 같아야 한다(int↔float 암시 변환 없음).
	if typeof(delta) != bt:
		push_error("WorldStateStore: add_state delta type mismatch on key '%s' (got %d, expected %d)"
			% [key, typeof(delta), bt])
		report["error"] = &"type_mismatch"
		return report

	# ADR-010 전제: 두 피연산자 모두 JSON-safe 도메인(INT ±(2^53-1) / FLOAT finite) 안이어야 한다.
	# delta를 덧셈 전에 검사하지 않으면 범위 밖 delta가 결과에서 상쇄돼 거짓 승인될 수 있고
	# (예: -1 + 2^53 = 2^53-1), FLOAT NAN/INF delta도 통과한다. 두 피연산자가 도메인 안이면
	# int64 합은 ±(2^54-2)로 wrap이 불가능하므로 별도 wrap 감지가 필요 없다.
	if not _value_in_domain(bt, delta):
		push_error("WorldStateStore: add_state delta out of JSON-safe domain on key '%s'" % key)
		report["error"] = &"out_of_domain"
		return report

	var old_value: Variant = _values[key]
	var new_value: Variant = old_value + delta

	# 결과가 JSON-safe 도메인 안인지 확인한다(경계 초과 / FLOAT finite+finite=inf overflow).
	if not _value_in_domain(bt, new_value):
		push_error("WorldStateStore: add_state result out of JSON-safe domain on key '%s'" % key)
		report["error"] = &"out_of_domain"
		return report

	# commit: 기존 _stage/_emit_changes 경계를 재사용한다. 같은 값이면 _stage가 {}를 돌려
	# value_changed를 발행하지 않는다. authoritative old/new는 발행 전에 report에 캡처한다.
	var change := _stage(key, new_value)
	report["old_value"] = old_value
	report["new_value"] = new_value
	report["applied"] = true
	report["changed"] = not change.is_empty()
	if not change.is_empty():
		_emit_changes([change])
	return report


## key가 등록돼 있고 store가 ready인가.
func has_key(key: StringName) -> bool:
	return _ready_state and _values.has(key)


## 등록 key의 현재 값. 미등록 key/not-ready면 오류를 기록하고 null을 반환한다.
func get_value(key: StringName) -> Variant:
	if not _ready_state:
		push_error("WorldStateStore: get_value on not-ready store (key '%s')" % key)
		return null
	if not _values.has(key):
		push_error("WorldStateStore: get_value on unregistered key '%s'" % key)
		return null
	return _values[key]


## 실패 허용 read. not-ready/미등록이면 오류 없이 fallback을 반환한다.
func try_get_value(key: StringName, fallback: Variant = null) -> Variant:
	if _ready_state and _values.has(key):
		return _values[key]
	return fallback


## gameplay mutation. strict type validation을 통과한 경우에만 값을 바꾼다.
## 반환:
## - OK: 적용됨(또는 같은 값이라 변경 없이 성공)
## - ERR_UNAVAILABLE: store not-ready
## - ERR_BUSY: value_changed 알림 발행 중(재진입 mutation 금지)
## - ERR_DOES_NOT_EXIST: 미등록 key
## - ERR_UNAUTHORIZED: read-only(writable=false) key에 대한 gameplay set
## - ERR_INVALID_DATA: 타입 불일치 또는 JSON-safe 도메인 위반(2^53 초과 INT, INF/NAN FLOAT)
func set_value(key: StringName, value: Variant) -> Error:
	if not _ready_state:
		push_error("WorldStateStore: set_value on not-ready store (key '%s')" % key)
		return ERR_UNAVAILABLE
	if _in_notification:
		push_error("WorldStateStore: set_value during value_changed notification (key '%s')" % key)
		return ERR_BUSY
	if not _contract.has(key):
		push_error("WorldStateStore: set_value on unregistered key '%s'" % key)
		return ERR_DOES_NOT_EXIST
	var c: Dictionary = _contract[key]
	if not c["writable"]:
		push_error("WorldStateStore: set_value denied on read-only key '%s'" % key)
		return ERR_UNAUTHORIZED
	if typeof(value) != c["builtin_type"]:
		push_error("WorldStateStore: set_value type mismatch on key '%s' (got %d, expected %d)"
			% [key, typeof(value), c["builtin_type"]])
		return ERR_INVALID_DATA
	if not _value_in_domain(c["builtin_type"], value):
		push_error("WorldStateStore: set_value out of JSON-safe domain on key '%s'" % key)
		return ERR_INVALID_DATA
	_commit(key, value)
	return OK


## 값을 compile된 default로 되돌린다. reset은 시스템 작업이므로 read-only key에도 허용된다.
## 반환: OK / ERR_UNAVAILABLE / ERR_BUSY / ERR_DOES_NOT_EXIST.
func reset_value(key: StringName) -> Error:
	if not _ready_state:
		push_error("WorldStateStore: reset_value on not-ready store (key '%s')" % key)
		return ERR_UNAVAILABLE
	if _in_notification:
		push_error("WorldStateStore: reset_value during value_changed notification (key '%s')" % key)
		return ERR_BUSY
	if not _contract.has(key):
		push_error("WorldStateStore: reset_value on unregistered key '%s'" % key)
		return ERR_DOES_NOT_EXIST
	_commit(key, _contract[key]["default"])
	return OK


## 주어진 lifetime의 모든 key를 default로 되돌린다(시스템 작업, read-only도 포함).
## 값이 바뀐 key마다 value_changed를 발행하고, 마지막에 state_reset(lifetime)을 1회 발행한다.
func reset_lifetime(lifetime: StateDefinition.StateLifetime) -> void:
	if not _ready_state:
		push_error("WorldStateStore: reset_lifetime on not-ready store")
		return
	if _in_notification:
		push_error("WorldStateStore: reset_lifetime during value_changed notification")
		return
	# 모든 대상 값을 먼저 반영한 뒤 신호를 모아 발행한다(부분 상태 노출 방지).
	var pending: Array = []
	for key in _contract:
		var c: Dictionary = _contract[key]
		if c["lifetime"] == lifetime:
			var change := _stage(key, c["default"])
			if not change.is_empty():
				pending.append(change)
	_emit_changes(pending)
	state_reset.emit(lifetime)


## snapshot을 import할 수 있는지 비변경(read-only)으로 점검한다. 값/시그널을 바꾸지 않는다.
## import_snapshot의 whole-reject envelope(최상위 구조 + schema_version)와 같은 규칙을 쓰며,
## transactional restore가 mutation 전에 호출한다(ADR-007 D4a). 개별 key 유효성은 검사하지 않는다.
## 반환: { "ok": bool, "reason": String }. reason: store_not_ready / malformed_snapshot /
## schema_version_mismatch / "".
func peek_snapshot_compatibility(snapshot: Dictionary) -> Dictionary:
	if not _ready_state:
		return {"ok": false, "reason": "store_not_ready"}
	var sv_int := _as_exact_int(snapshot.get("schema_version"))
	if not (snapshot.has("schema_version") and sv_int["ok"] \
			and snapshot.has("values") and typeof(snapshot["values"]) == TYPE_DICTIONARY):
		return {"ok": false, "reason": "malformed_snapshot"}
	if sv_int["value"] != _schema_version:
		return {"ok": false, "reason": "schema_version_mismatch"}
	return {"ok": true, "reason": ""}


## 지정 lifetime(기본 SAVE) 값들을 JSON 호환 snapshot으로 내보낸다.
## StringName 값은 String으로 정규화한다. SESSION은 기본 export에 포함되지 않는다.
## 형식: { "schema_version": int, "values": { key: wire_value, ... } }
func export_snapshot(lifetime: StateDefinition.StateLifetime = StateDefinition.StateLifetime.SAVE) -> Dictionary:
	var values: Dictionary = {}
	if _ready_state:
		for key in _contract:
			var c: Dictionary = _contract[key]
			if c["lifetime"] == lifetime:
				values[String(key)] = _to_wire(c["builtin_type"], _values[key])
	else:
		push_error("WorldStateStore: export_snapshot on not-ready store")
	return {
		"schema_version": _schema_version,
		"values": values,
	}


## snapshot을 replace-load 한다(SAVE lifetime 대상).
##
## 정책:
## - 최상위 구조(Dictionary + schema_version:int + values:Dictionary)나 schema_version 불일치는
##   commit 전에 전체 거부하고 아무 값도 바꾸지 않는다.
## - 통과하면 SAVE key들의 최종 값을 먼저 계산한다: snapshot에 유효 값이 있으면 그 값,
##   없으면 default. (snapshot에 없는 SAVE key는 default로 리셋되는 replace-load.)
## - 중간 신호 없이 key마다 1회만 commit한다(값이 바뀐 경우 value_changed).
## - unknown key, SESSION key, 타입 불일치 항목은 개별적으로 무시하고 report에 남긴다.
## - read-only key에도 적용된다(시스템 작업).
##
## 반환/발행 report: { "applied": [key...], "ignored": [{key, reason}...], "errors": [{key, reason}...] }
func import_snapshot(snapshot: Dictionary) -> Dictionary:
	var report: Dictionary = {"applied": [], "ignored": [], "errors": []}

	if not _ready_state:
		report["errors"].append({"key": "", "reason": "store_not_ready"})
		return _finish_import(report)
	if _in_notification:
		report["errors"].append({"key": "", "reason": "store_busy"})
		return _finish_import(report)

	# 1) 최상위 구조 검증. schema_version은 JSON 왕복을 고려하되 finite한 정확 정수만 허용한다.
	var sv_int := _as_exact_int(snapshot.get("schema_version"))
	if not (snapshot.has("schema_version") and sv_int["ok"] \
			and snapshot.has("values") and typeof(snapshot["values"]) == TYPE_DICTIONARY):
		report["errors"].append({"key": "", "reason": "malformed_snapshot"})
		return _finish_import(report)

	# 2) schema_version 일치 검증(불일치는 전체 거부).
	if sv_int["value"] != _schema_version:
		report["errors"].append({"key": "", "reason": "schema_version_mismatch"})
		return _finish_import(report)

	var wire_values: Dictionary = snapshot["values"]

	# 3) snapshot 항목을 분류하고 유효한 SAVE 값만 desired에 모은다.
	var desired: Dictionary = {}  # StringName -> Variant (적용할 값)
	for raw_key in wire_values:
		var key := StringName(raw_key)
		if not _contract.has(key):
			report["ignored"].append({"key": String(key), "reason": "unknown_key"})
			continue
		var c: Dictionary = _contract[key]
		if c["lifetime"] != StateDefinition.StateLifetime.SAVE:
			report["ignored"].append({"key": String(key), "reason": "session_key"})
			continue
		var coerced := _coerce_wire_value(c["builtin_type"], wire_values[raw_key])
		if not coerced["ok"]:
			report["errors"].append({"key": String(key), "reason": "type_mismatch"})
			continue
		desired[key] = coerced["value"]

	# 4) 모든 SAVE key의 최종 값을 먼저 _values에 반영하고(부분 상태 노출 방지), staged 변경을 모은다.
	var pending: Array = []
	for key in _contract:
		var c: Dictionary = _contract[key]
		if c["lifetime"] != StateDefinition.StateLifetime.SAVE:
			continue
		var target: Variant = desired[key] if desired.has(key) else c["default"]
		var change := _stage(key, target)
		if not change.is_empty():
			pending.append(change)
		if desired.has(key):
			report["applied"].append(String(key))

	# 5) 모든 값이 반영된 뒤 결정된 순서(contract 순서)로 value_changed를 발행한다.
	_emit_changes(pending)
	return _finish_import(report)


# 모든 import 종료 경로의 발행/반환 정책을 통일한다. signal과 반환값에 각각 독립적인 deep copy를
# 써서 동기 subscriber가 반환 report를(또는 호출자가 signal report를) 변조하지 못하게 한다.
func _finish_import(report: Dictionary) -> Dictionary:
	snapshot_imported.emit(report.duplicate(true))
	return report.duplicate(true)


## 여러 변경을 한 묶음으로 atomic하게 적용한다(한 선택의 여러 Effect 등).
##
## change 형식: { "key": StringName|String, "value": Variant }. key는 StringName 또는 String만
## 허용하며 그 외 타입(null/int 등)은 malformed_change로 보고한다(런타임 오류 대신 구조화된 실패).
## 모든 변경을 먼저 검증하고, 하나라도 실패하면 batch 전체를 거부한다(값/시그널 불변, 부분 적용 없음).
## 같은 key가 두 번 이상 나오면 의도를 추측하지 않고 batch 전체를 거부한다.
## 성공 시 모든 값을 먼저 반영한 뒤 입력 순서대로 value_changed를 발행한다(부분 상태 노출 방지).
## gameplay mutation이므로 read-only key는 거부한다. 도메인/타입 규칙은 set_value와 같다.
##
## 반환 report:
## { "applied": bool, "diff": [{key, old, new}...], "errors": [{index, key, reason}...] }
## diff는 실제로 값이 바뀐 항목만 입력 순서로 기록한다(value_changed 발행과 1:1).
## errors reason: store_not_ready / store_busy / malformed_change / unknown_key / read_only /
##   type_mismatch / out_of_domain / duplicate_key.
func apply_batch(changes: Array[Dictionary]) -> Dictionary:
	var report: Dictionary = {"applied": false, "diff": [], "errors": []}

	if not _ready_state:
		report["errors"].append({"index": -1, "key": "", "reason": "store_not_ready"})
		return report
	if _in_notification:
		report["errors"].append({"index": -1, "key": "", "reason": "store_busy"})
		return report

	# 1) 모든 변경을 먼저 검증한다(부분 적용 없음). 모든 오류를 모은다.
	# key 타입을 먼저 검사하고(런타임 오류 방지), 중복은 다른 오류와 독립적으로(첫 등장이 오류여도)
	# 앞당겨 검사해 같은 key 재등장을 항상 잡는다.
	var seen: Dictionary = {}            # StringName -> 첫 등장 index
	var validated: Array = []            # [{key, value}] 입력 순서
	for i in changes.size():
		var change: Dictionary = changes[i]
		if not (change.has("key") and change.has("value")):
			report["errors"].append({"index": i, "key": "", "reason": "malformed_change"})
			continue
		var raw_key: Variant = change["key"]
		var key_type := typeof(raw_key)
		if key_type != TYPE_STRING_NAME and key_type != TYPE_STRING:
			report["errors"].append({"index": i, "key": "", "reason": "malformed_change"})
			continue
		var key := StringName(raw_key)
		# 중복은 type/domain 검사보다 먼저, 독립적으로 본다.
		if seen.has(key):
			report["errors"].append({"index": i, "key": String(key), "reason": "duplicate_key"})
			continue
		seen[key] = i
		var value: Variant = change["value"]
		if not _contract.has(key):
			report["errors"].append({"index": i, "key": String(key), "reason": "unknown_key"})
			continue
		var c: Dictionary = _contract[key]
		if not c["writable"]:
			report["errors"].append({"index": i, "key": String(key), "reason": "read_only"})
			continue
		if typeof(value) != c["builtin_type"]:
			report["errors"].append({"index": i, "key": String(key), "reason": "type_mismatch"})
			continue
		if not _value_in_domain(c["builtin_type"], value):
			report["errors"].append({"index": i, "key": String(key), "reason": "out_of_domain"})
			continue
		validated.append({"key": key, "value": value})

	# 2) 하나라도 오류면 batch 전체를 거부한다(값/시그널 불변).
	if not report["errors"].is_empty():
		return report

	# 3) 모든 값을 먼저 반영하고 staged 변경을 입력 순서로 모은다.
	var pending: Array = []
	for v in validated:
		var change := _stage(v["key"], v["value"])
		if not change.is_empty():
			pending.append(change)

	# 4) diff 기록(실제 변경, 입력 순서) 후 입력 순서로 value_changed를 발행한다.
	for ch in pending:
		report["diff"].append({"key": ch["key"], "old": ch["old"], "new": ch["new"]})
	report["applied"] = true
	_emit_changes(pending)
	return report


# 내부 값 -> JSON 호환 wire 값. StringName만 String으로 정규화한다.
func _to_wire(builtin_type: int, value: Variant) -> Variant:
	if builtin_type == TYPE_STRING_NAME:
		return String(value)
	return value


# wire 값 -> schema 타입 복원. 시스템 작업이라 JSON 왕복(정수 float<->int)과 StringName 복원을
# 허용하지만, 손실 입력(비정수 float, 비-finite, 범위 초과)은 거부한다.
# 반환: { "ok": bool, "value": Variant }
func _coerce_wire_value(builtin_type: int, wire: Variant) -> Dictionary:
	var t := typeof(wire)
	match builtin_type:
		TYPE_BOOL:
			if t == TYPE_BOOL:
				return {"ok": true, "value": wire}
		TYPE_INT:
			# JSON 왕복에서 정수가 float로 올 수 있다. finite한 정확 정수만 허용한다.
			return _as_exact_int(wire)
		TYPE_FLOAT:
			# int wire는 float 변환 시 정밀도 손실을 막기 위해 JSON-safe 범위만 허용한다
			# (그 범위 안의 정수는 double로 정확히 표현된다). float wire는 finite만 허용(inf/nan 거부).
			if t == TYPE_INT:
				if wire >= JSON_SAFE_INT_MIN and wire <= JSON_SAFE_INT_MAX:
					return {"ok": true, "value": float(wire)}
			elif t == TYPE_FLOAT and is_finite(wire):
				return {"ok": true, "value": wire}
		TYPE_STRING:
			if t == TYPE_STRING:
				return {"ok": true, "value": wire}
		TYPE_STRING_NAME:
			# wire format에서 StringName은 String으로 저장된다.
			if t == TYPE_STRING or t == TYPE_STRING_NAME:
				return {"ok": true, "value": StringName(wire)}
	return {"ok": false, "value": null}


# JSON 안전 정수 한계: ±(2^53 - 1). Godot JSON은 숫자를 double로 파싱하므로 이 범위를 넘는 정수는
# 왕복에서 정밀도가 보존되지 않는다(2^53+1 -> 2^53 등). snapshot은 JSON 호환 wire이므로 이 범위
# 밖의 정수는 조용히 손상시키지 않고 명시적으로 거부한다.
const JSON_SAFE_INT_MAX := 9007199254740991   # 2^53 - 1
const JSON_SAFE_INT_MIN := -9007199254740991


# wire 값을 정확한 int로 해석한다. int 그대로, 또는 finite하고 정확히 정수인 float만 허용하며,
# 모두 JSON 안전 정수 범위 안이어야 한다. 1.5/1.000001/inf/nan/2^53 초과/±2^63은 거부한다.
# 반환: { "ok": bool, "value": int }
func _as_exact_int(wire: Variant) -> Dictionary:
	var t := typeof(wire)
	if t == TYPE_INT:
		if wire >= JSON_SAFE_INT_MIN and wire <= JSON_SAFE_INT_MAX:
			return {"ok": true, "value": wire}
		return {"ok": false, "value": 0}
	if t == TYPE_FLOAT:
		# 경계값(2^53-1)은 double로 정확히 표현되므로 float 비교가 안전하다.
		if is_finite(wire) and wire == floor(wire) \
				and wire >= float(JSON_SAFE_INT_MIN) and wire <= float(JSON_SAFE_INT_MAX):
			return {"ok": true, "value": int(wire)}
	return {"ok": false, "value": 0}


# 단일 key 변경: 값을 반영하고 바뀐 경우 value_changed를 발행한다(단일 key는 부분 상태 없음).
func _commit(key: StringName, value: Variant) -> void:
	var change := _stage(key, value)
	if not change.is_empty():
		_emit_changes([change])


# 값을 _values에 즉시 반영하되 signal은 발행하지 않는다. 실제로 바뀌면 {key, old, new}를,
# 같은 값이면 빈 {}를 반환한다. 다중 key 작업이 모든 값을 먼저 반영한 뒤 신호를 모아 발행하도록
# 돕는다(부분 적용 상태 노출 방지).
func _stage(key: StringName, value: Variant) -> Dictionary:
	var old_value: Variant = _values[key]
	if _same_value(old_value, value):
		return {}
	_values[key] = value
	return {"key": key, "old": old_value, "new": value}


# staged 변경들을 입력 순서대로 발행한다. 모든 값이 이미 반영된 뒤 호출돼야 한다.
# 발행 중에는 _in_notification을 세워 재진입 mutation을 차단한다(stale event 방지).
func _emit_changes(changes: Array) -> void:
	if changes.is_empty():
		return
	_in_notification = true
	for ch in changes:
		value_changed.emit(ch["key"], ch["old"], ch["new"])
	_in_notification = false


# 같은 타입의 두 값 비교(허용 타입은 모두 == 비교가 안전한 value 타입이다).
func _same_value(a: Variant, b: Variant) -> bool:
	return typeof(a) == typeof(b) and a == b


# 값이 JSON-safe 도메인 안인가. INT는 ±(2^53-1), FLOAT는 finite여야 한다. 그 외 타입은 무제한.
# 타입 자체 검사는 호출 전에 끝났다고 가정한다.
func _value_in_domain(builtin_type: int, value: Variant) -> bool:
	match builtin_type:
		TYPE_INT:
			return value >= JSON_SAFE_INT_MIN and value <= JSON_SAFE_INT_MAX
		TYPE_FLOAT:
			return is_finite(value)
	return true
