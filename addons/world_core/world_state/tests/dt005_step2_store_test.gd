# DT-005 Step 2 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/world_core/world_state/tests/dt005_step2_store_test.tscn
#
# 검증 범위:
# - 유효 schema로만 Store가 ready (null/invalid schema는 not-ready)
# - default 초기화
# - 등록 key만 set, strict type validation(암시적 변환 금지)
# - 실패한 set은 값/시그널을 바꾸지 않음
# - read-only는 gameplay set 거부, reset은 허용
# - 같은 값 set/reset은 성공하되 value_changed 무발행
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime

var _failures: int = 0
var _signal_log: Array = []


func _ready() -> void:
	_test_not_ready_null_schema()
	_test_not_ready_invalid_schema()
	_test_default_initialization()
	_test_set_valid_and_signal()
	_test_set_unknown_key()
	_test_set_readonly_denied()
	_test_set_type_mismatch()
	_test_set_same_value_no_signal()
	_test_reset_restores_default()
	_test_reset_allowed_on_readonly()
	_test_reset_same_as_default_no_signal()
	_test_try_get_and_has_key()
	_test_contract_isolated_from_schema_mutation()
	_test_explicit_reinitialization()

	if _failures == 0:
		print("[DT-005 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-005 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- 헬퍼 -------------------------------------------------------------

func _def(key: StringName, vtype: int, default_value: Variant,
		lifetime: int = LT.SAVE, writable: bool = true) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	d.lifetime = lifetime
	d.writable = writable
	return d


func _schema(defs: Array, version: int = 1) -> StateSchema:
	var s := StateSchema.new()
	s.schema_version = version
	var typed: Array[StateDefinition] = []
	for d in defs:
		typed.append(d)
	s.definitions = typed
	return s


# 표준 테스트 schema: gold(int), name(String), hp(float), mood(StringName),
# locked(bool, read-only).
func _make_store() -> WorldStateStore:
	var s := _schema([
		_def(&"player.gold", VT.INT, 100),
		_def(&"player.name", VT.STRING, "hero"),
		_def(&"player.hp", VT.FLOAT, 10.0),
		_def(&"actor.mood", VT.STRING_NAME, &"calm"),
		_def(&"world.locked", VT.BOOL, true, LT.SAVE, false),
	])
	var store := WorldStateStore.new()
	store.schema = s
	store.value_changed.connect(_on_value_changed)
	store.initialize()
	return store


func _on_value_changed(key: StringName, old_value: Variant, new_value: Variant) -> void:
	_signal_log.append({"key": key, "old": old_value, "new": new_value})


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# --- 시나리오 ---------------------------------------------------------

func _test_not_ready_null_schema() -> void:
	print("[A] null schema -> not ready")
	var store := WorldStateStore.new()
	store.schema = null
	var ok := store.initialize()
	_check("A.init", ok, false)
	_check("A.ready", store.is_store_ready(), false)
	_check("A.get_null", store.get_value(&"player.gold"), null)
	_check("A.set_unavailable", store.set_value(&"player.gold", 1), ERR_UNAVAILABLE)
	store.free()


func _test_not_ready_invalid_schema() -> void:
	print("[B] invalid schema -> not ready")
	var s := _schema([_def(&"Bad", VT.INT, 0)]) # 잘못된 key 형식
	var store := WorldStateStore.new()
	store.schema = s
	var ok := store.initialize()
	_check("B.init", ok, false)
	_check("B.ready", store.is_store_ready(), false)
	store.free()


func _test_default_initialization() -> void:
	print("[C] default 초기화")
	var store := _make_store()
	_check("C.ready", store.is_store_ready(), true)
	_check("C.gold", store.get_value(&"player.gold"), 100)
	_check("C.name", store.get_value(&"player.name"), "hero")
	_check("C.hp", store.get_value(&"player.hp"), 10.0)
	_check("C.mood", store.get_value(&"actor.mood"), "calm")
	_check("C.mood_typeof", typeof(store.get_value(&"actor.mood")), TYPE_STRING_NAME)
	_check("C.locked", store.get_value(&"world.locked"), true)
	store.free()


func _test_set_valid_and_signal() -> void:
	print("[D] 정상 set + value_changed")
	var store := _make_store()
	_signal_log.clear()
	var err := store.set_value(&"player.gold", 250)
	_check("D.err", err, OK)
	_check("D.value", store.get_value(&"player.gold"), 250)
	_check("D.signal_count", _signal_log.size(), 1)
	_check("D.signal_key", _signal_log[0]["key"], &"player.gold")
	_check("D.signal_old", _signal_log[0]["old"], 100)
	_check("D.signal_new", _signal_log[0]["new"], 250)
	store.free()


func _test_set_unknown_key() -> void:
	print("[E] 미등록 key set 거부")
	var store := _make_store()
	_signal_log.clear()
	var err := store.set_value(&"nope.nope", 1)
	_check("E.err", err, ERR_DOES_NOT_EXIST)
	_check("E.no_signal", _signal_log.size(), 0)
	_check("E.not_registered", store.has_key(&"nope.nope"), false)
	store.free()


func _test_set_readonly_denied() -> void:
	print("[F] read-only key gameplay set 거부")
	var store := _make_store()
	_signal_log.clear()
	var err := store.set_value(&"world.locked", false)
	_check("F.err", err, ERR_UNAUTHORIZED)
	_check("F.unchanged", store.get_value(&"world.locked"), true)
	_check("F.no_signal", _signal_log.size(), 0)
	store.free()


func _test_set_type_mismatch() -> void:
	print("[G] 타입 불일치 set 거부 (값/시그널 불변)")
	var store := _make_store()
	_signal_log.clear()
	# int key에 String
	_check("G.int<-str", store.set_value(&"player.gold", "x"), ERR_INVALID_DATA)
	# float key에 int (암시적 변환 금지)
	_check("G.float<-int", store.set_value(&"player.hp", 5), ERR_INVALID_DATA)
	# String key에 StringName
	_check("G.str<-sn", store.set_value(&"player.name", &"hi"), ERR_INVALID_DATA)
	# StringName key에 String
	_check("G.sn<-str", store.set_value(&"actor.mood", "angry"), ERR_INVALID_DATA)
	# 값 불변
	_check("G.gold_unchanged", store.get_value(&"player.gold"), 100)
	_check("G.hp_unchanged", store.get_value(&"player.hp"), 10.0)
	_check("G.name_unchanged", store.get_value(&"player.name"), "hero")
	_check("G.mood_unchanged", store.get_value(&"actor.mood"), "calm")
	_check("G.no_signal", _signal_log.size(), 0)
	store.free()


func _test_set_same_value_no_signal() -> void:
	print("[H] 같은 값 set -> 성공, 시그널 무발행")
	var store := _make_store()
	_signal_log.clear()
	var err := store.set_value(&"player.gold", 100) # default와 동일
	_check("H.err", err, OK)
	_check("H.no_signal", _signal_log.size(), 0)
	store.free()


func _test_reset_restores_default() -> void:
	print("[I] reset -> default 복원 + 변경 시 시그널")
	var store := _make_store()
	store.set_value(&"player.gold", 999)
	_signal_log.clear()
	var err := store.reset_value(&"player.gold")
	_check("I.err", err, OK)
	_check("I.value", store.get_value(&"player.gold"), 100)
	_check("I.signal_count", _signal_log.size(), 1)
	_check("I.signal_new", _signal_log[0]["new"], 100)
	_check("I.signal_old", _signal_log[0]["old"], 999)
	store.free()


func _test_reset_allowed_on_readonly() -> void:
	print("[J] read-only key도 reset 허용")
	var store := _make_store()
	# read-only라 gameplay set은 막히지만, 시스템 reset은 default로 복원돼야 한다.
	var err := store.reset_value(&"world.locked")
	_check("J.err", err, OK)
	_check("J.value", store.get_value(&"world.locked"), true)
	store.free()


func _test_reset_same_as_default_no_signal() -> void:
	print("[K] 이미 default면 reset 성공하되 시그널 무발행")
	var store := _make_store()
	_signal_log.clear()
	var err := store.reset_value(&"player.gold") # 이미 100
	_check("K.err", err, OK)
	_check("K.no_signal", _signal_log.size(), 0)
	store.free()


func _test_try_get_and_has_key() -> void:
	print("[L] try_get_value / has_key")
	var store := _make_store()
	_check("L.has_known", store.has_key(&"player.gold"), true)
	_check("L.has_unknown", store.has_key(&"nope.nope"), false)
	_check("L.try_known", store.try_get_value(&"player.gold"), 100)
	_check("L.try_unknown_fallback", store.try_get_value(&"nope.nope", -1), -1)
	_check("L.try_unknown_default_null", store.try_get_value(&"nope.nope"), null)
	store.free()


func _test_contract_isolated_from_schema_mutation() -> void:
	print("[M] 초기화 후 schema 변경이 Store 계약에 섞이지 않음")
	# gold(int,100,writable), locked(bool,true,read-only)
	var gold := _def(&"player.gold", VT.INT, 100)
	var locked := _def(&"world.locked", VT.BOOL, true, LT.SAVE, false)
	var s := _schema([gold, locked])
	var store := WorldStateStore.new()
	store.schema = s
	store.initialize()
	_check("M.ready", store.is_store_ready(), true)

	# 초기화 후 schema/Definition을 마구 바꾼다(재초기화 없이).
	gold.default_value = 500          # default 변경
	gold.writable = false             # writable 변경
	gold.value_type = VT.STRING       # 타입 변경
	s.definitions.append(_def(&"player.xp", VT.INT, 7))  # key 추가(in-place)
	s.definitions.erase(locked)       # key 삭제(in-place)

	# 1) default 변경은 reset에 섞이지 않는다 → compile된 100으로 복원.
	store.set_value(&"player.gold", 250)
	_check("M.reset_uses_compiled_default", store.reset_value(&"player.gold"), OK)
	_check("M.gold_default_100", store.get_value(&"player.gold"), 100)

	# 2) writable 변경 무시 → 여전히 set 가능.
	_check("M.gold_still_writable", store.set_value(&"player.gold", 300), OK)
	_check("M.gold_set_value", store.get_value(&"player.gold"), 300)

	# 3) 타입 변경 무시 → 여전히 INT만 허용.
	_check("M.gold_still_int", store.set_value(&"player.gold", 7), OK)
	_check("M.gold_reject_string", store.set_value(&"player.gold", "x"), ERR_INVALID_DATA)

	# 4) 추가된 key는 Store에 없다.
	_check("M.new_key_absent", store.has_key(&"player.xp"), false)
	_check("M.new_key_set_denied", store.set_value(&"player.xp", 1), ERR_DOES_NOT_EXIST)

	# 5) 삭제된 key는 Store 계약에 유지된다 — get/set/reset 모두 일관.
	_check("M.deleted_key_present", store.has_key(&"world.locked"), true)
	_check("M.deleted_key_get", store.get_value(&"world.locked"), true)
	_check("M.deleted_key_still_readonly", store.set_value(&"world.locked", false), ERR_UNAUTHORIZED)
	_check("M.deleted_key_reset_ok", store.reset_value(&"world.locked"), OK)
	store.free()


func _test_explicit_reinitialization() -> void:
	print("[N] 명시적 재초기화로만 새 계약/default 반영")
	var store := WorldStateStore.new()
	store.schema = _schema([_def(&"player.gold", VT.INT, 100)])
	store.initialize()
	store.set_value(&"player.gold", 300)
	_check("N.pre_gold", store.get_value(&"player.gold"), 300)
	_check("N.pre_no_xp", store.has_key(&"player.xp"), false)

	# 새 schema로 교체 후 재초기화.
	store.schema = _schema([
		_def(&"player.gold", VT.INT, 50),
		_def(&"player.xp", VT.INT, 7),
	])
	_check("N.reinit", store.initialize(), true)
	_check("N.gold_new_default", store.get_value(&"player.gold"), 50) # runtime 값도 default로 리셋
	_check("N.has_xp", store.has_key(&"player.xp"), true)
	_check("N.xp_value", store.get_value(&"player.xp"), 7)

	# invalid schema로 재초기화하면 not-ready.
	store.schema = _schema([_def(&"Bad", VT.INT, 0)])
	_check("N.reinit_invalid", store.initialize(), false)
	_check("N.not_ready", store.is_store_ready(), false)
	store.free()
