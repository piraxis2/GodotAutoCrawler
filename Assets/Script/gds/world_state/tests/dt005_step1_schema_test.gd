# DT-005 Step 1 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://Assets/Script/gds/world_state/tests/dt005_step1_schema_test.tscn
#
# 검증 범위:
# - 정상 schema validation 통과 + key lookup 동작
# - 모든 필수 오류 사례 검출(빈/잘못된/중복 key, default 타입 불일치, null Definition,
#   잘못된 enum, schema_version < 1)
# - 오류가 하나라도 있으면 부분 lookup을 공개하지 않음(빈 lookup)
# - .tres 저장 -> cache 무시 재로드 왕복에서 순서/필드 보존
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const TMP_PATH := "user://dt005_tmp_schema.tres"

var _failures: int = 0


func _ready() -> void:
	_test_valid_schema_and_lookup()
	_test_empty_key()
	_test_single_segment_key()
	_test_invalid_format_keys()
	_test_duplicate_key()
	_test_default_type_mismatch()
	_test_strict_no_implicit_conversion()
	_test_null_definition()
	_test_invalid_value_type_enum()
	_test_invalid_lifetime_enum()
	_test_schema_version_too_low()
	_test_invalid_schema_hides_partial_lookup()
	_test_roundtrip_save_reload()
	_test_cache_invalidation_after_mutation()
	_test_result_immutability()
	_test_inplace_array_mutation()

	if _failures == 0:
		print("[DT-005 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-005 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- 헬퍼 -------------------------------------------------------------

func _def(key: StringName, vtype: int, default_value: Variant,
		lifetime: int = LT.SAVE, writable: bool = true,
		description: String = "", tags: Array[StringName] = []) -> StateDefinition:
	var d := StateDefinition.new()
	d.key = key
	d.value_type = vtype
	d.default_value = default_value
	d.lifetime = lifetime
	d.writable = writable
	d.description = description
	d.tags = tags
	return d


func _schema(defs: Array, version: int = 1) -> StateSchema:
	var s := StateSchema.new()
	s.schema_version = version
	var typed: Array[StateDefinition] = []
	for d in defs:
		typed.append(d)
	s.definitions = typed
	return s


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# error_codes에 특정 code가 있는지.
func _has_code(result: Dictionary, code: String) -> bool:
	return result.get("error_codes", []).has(code)


# --- 시나리오 ---------------------------------------------------------

func _test_valid_schema_and_lookup() -> void:
	print("[A] 정상 schema validation + lookup")
	var s := _schema([
		_def(&"quest.main.stage", VT.INT, 0),
		_def(&"actor.noabel.affinity", VT.INT, 35, LT.SAVE),
		_def(&"dialogue.blacksmith.first_met", VT.BOOL, false, LT.SESSION),
		_def(&"player.stat.charisma", VT.FLOAT, 1.0),
		_def(&"world.region.name", VT.STRING, "town"),
		_def(&"actor.noabel.mood", VT.STRING_NAME, &"calm"),
	])
	var r := s.validate()
	_check("A.valid", r["valid"], true)
	_check("A.error_count", r["errors"].size(), 0)
	_check("A.key_count", r["key_count"], 6)
	_check("A.is_valid", s.is_valid(), true)
	_check_true("A.has_key", s.has_key(&"actor.noabel.affinity"))
	_check_true("A.not_has_unknown", not s.has_key(&"nope.nope"))
	var d := s.get_definition(&"player.stat.charisma")
	_check("A.lookup_type", d.value_type, VT.FLOAT)
	_check("A.lookup_default", d.default_value, 1.0)
	_check("A.keys_count", s.keys().size(), 6)


func _test_empty_key() -> void:
	print("[B] 빈 key")
	var s := _schema([_def(&"", VT.INT, 0)])
	var r := s.validate()
	_check("B.valid", r["valid"], false)
	_check_true("B.code", _has_code(r, "key_empty"))


func _test_single_segment_key() -> void:
	print("[C] 단일 segment key (두 segment 미만)")
	var s := _schema([_def(&"quest", VT.INT, 0)])
	var r := s.validate()
	_check("C.valid", r["valid"], false)
	_check_true("C.code", _has_code(r, "key_invalid_format"))


func _test_invalid_format_keys() -> void:
	print("[D] 잘못된 key 형식(대문자/공백/연속 dot/trailing dot/leading digit)")
	var bad_keys: Array[StringName] = [
		&"Quest.main",       # 대문자
		&"quest .main",      # 공백
		&"quest..main",      # 연속 dot
		&"quest.main.",      # trailing dot
		&".quest.main",      # leading dot
		&"1quest.main",      # leading digit
		&"quest.MAIN",       # segment 대문자
	]
	for k in bad_keys:
		var s := _schema([_def(k, VT.INT, 0)])
		var r := s.validate()
		_check("D.invalid[%s]" % k, r["valid"], false)
		_check_true("D.code[%s]" % k, _has_code(r, "key_invalid_format"))


func _test_duplicate_key() -> void:
	print("[E] 중복 key")
	var s := _schema([
		_def(&"quest.main.stage", VT.INT, 0),
		_def(&"quest.main.stage", VT.INT, 1),
	])
	var r := s.validate()
	_check("E.valid", r["valid"], false)
	_check_true("E.code", _has_code(r, "key_duplicate"))


func _test_default_type_mismatch() -> void:
	print("[F] default 타입 불일치 (INT key에 String default)")
	var s := _schema([_def(&"quest.main.stage", VT.INT, "not-an-int")])
	var r := s.validate()
	_check("F.valid", r["valid"], false)
	_check_true("F.code", _has_code(r, "default_type_mismatch"))


func _test_strict_no_implicit_conversion() -> void:
	print("[G] 암시적 변환 금지")
	# int -> float 금지: FLOAT key에 int default.
	var s1 := _schema([_def(&"player.stat.charisma", VT.FLOAT, 5)])
	var r1 := s1.validate()
	_check("G.int_to_float.valid", r1["valid"], false)
	_check_true("G.int_to_float.code", _has_code(r1, "default_type_mismatch"))
	# String/StringName 구분: STRING key에 StringName default.
	var s2 := _schema([_def(&"world.region.name", VT.STRING, &"town")])
	var r2 := s2.validate()
	_check("G.string_vs_sn.valid", r2["valid"], false)
	_check_true("G.string_vs_sn.code", _has_code(r2, "default_type_mismatch"))
	# null default도 타입 불일치(BOOL key에 null).
	var s3 := _schema([_def(&"dialogue.x.flag", VT.BOOL, null)])
	var r3 := s3.validate()
	_check("G.null_default.valid", r3["valid"], false)
	_check_true("G.null_default.code", _has_code(r3, "default_type_mismatch"))


func _test_null_definition() -> void:
	print("[H] null Definition 항목")
	var s := _schema([
		_def(&"quest.main.stage", VT.INT, 0),
		null,
	])
	var r := s.validate()
	_check("H.valid", r["valid"], false)
	_check_true("H.code", _has_code(r, "definition_null"))


func _test_invalid_value_type_enum() -> void:
	print("[I] 잘못된 value_type enum")
	var d := _def(&"quest.main.stage", VT.INT, 0)
	d.value_type = 99 # 범위 밖
	var s := _schema([d])
	var r := s.validate()
	_check("I.valid", r["valid"], false)
	_check_true("I.code", _has_code(r, "value_type_invalid"))


func _test_invalid_lifetime_enum() -> void:
	print("[J] 잘못된 lifetime enum")
	var d := _def(&"quest.main.stage", VT.INT, 0)
	d.lifetime = 7 # 범위 밖
	var s := _schema([d])
	var r := s.validate()
	_check("J.valid", r["valid"], false)
	_check_true("J.code", _has_code(r, "lifetime_invalid"))


func _test_schema_version_too_low() -> void:
	print("[K] schema_version < 1")
	var s := _schema([_def(&"quest.main.stage", VT.INT, 0)], 0)
	var r := s.validate()
	_check("K.valid", r["valid"], false)
	_check_true("K.code", _has_code(r, "schema_version_invalid"))


func _test_invalid_schema_hides_partial_lookup() -> void:
	print("[L] 오류가 있으면 부분 lookup 비공개")
	# 첫 Definition은 정상, 둘째는 잘못된 key. 정상 key도 lookup에 노출되면 안 된다.
	var s := _schema([
		_def(&"quest.main.stage", VT.INT, 0),
		_def(&"BadKey", VT.INT, 0),
	])
	var r := s.validate()
	_check("L.valid", r["valid"], false)
	_check("L.key_count", r["key_count"], 0)
	_check_true("L.no_valid_key", not s.has_key(&"quest.main.stage"))
	_check("L.get_returns_null", s.get_definition(&"quest.main.stage"), null)
	_check("L.keys_empty", s.keys().size(), 0)


func _test_roundtrip_save_reload() -> void:
	print("[M] .tres 저장 -> cache 무시 재로드 왕복")
	var tags: Array[StringName] = [&"persistent", &"affinity"]
	var s := _schema([
		_def(&"quest.main.stage", VT.INT, 2, LT.SAVE, true, "메인 퀘스트 단계", [&"quest"] as Array[StringName]),
		_def(&"actor.noabel.affinity", VT.INT, 35, LT.SAVE, false, "노아벨 호감도", tags),
		_def(&"dialogue.blacksmith.first_met", VT.BOOL, false, LT.SESSION, true, "첫 대면 여부"),
		_def(&"world.region.name", VT.STRING, "town", LT.SAVE, true, "지역 이름"),
		_def(&"actor.noabel.mood", VT.STRING_NAME, &"calm", LT.SESSION, true, "기분 태그"),
	])
	# 저장 전에도 정상이어야 한다.
	_check("M.pre_valid", s.validate()["valid"], true)

	var save_err := ResourceSaver.save(s, TMP_PATH)
	_check("M.save_ok", save_err, OK)

	# cache를 무시하고 재로드한다.
	var loaded: StateSchema = ResourceLoader.load(TMP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	_check_true("M.loaded_not_null", loaded != null)
	if loaded == null:
		_cleanup()
		return

	_check("M.version", loaded.schema_version, s.schema_version) # version 보존
	_check("M.def_count", loaded.definitions.size(), s.definitions.size())

	# Definition 순서와 모든 필드 보존 확인.
	for i in s.definitions.size():
		var a: StateDefinition = s.definitions[i]
		var b: StateDefinition = loaded.definitions[i]
		_check("M[%d].key" % i, b.key, a.key)
		_check("M[%d].value_type" % i, b.value_type, a.value_type)
		_check("M[%d].default" % i, b.default_value, a.default_value)
		_check("M[%d].default_typeof" % i, typeof(b.default_value), typeof(a.default_value))
		_check("M[%d].lifetime" % i, b.lifetime, a.lifetime)
		_check("M[%d].writable" % i, b.writable, a.writable)
		_check("M[%d].description" % i, b.description, a.description)
		_check("M[%d].tags" % i, b.tags, a.tags)

	# 재로드한 schema도 validation + lookup이 동작한다.
	var lr := loaded.validate()
	_check("M.reloaded_valid", lr["valid"], true)
	_check_true("M.reloaded_lookup", loaded.has_key(&"actor.noabel.affinity"))
	# StringName default가 재로드 후에도 StringName 타입으로 보존되는지(strict 구분).
	var sn_def := loaded.get_definition(&"actor.noabel.mood")
	_check("M.sn_typeof", typeof(sn_def.default_value), TYPE_STRING_NAME)

	_cleanup()


func _test_cache_invalidation_after_mutation() -> void:
	print("[N] 검증 후 mutation 시 lookup 무효화")

	# N1: 검증 후 기존 Definition의 key를 잘못된 값으로 deep mutation.
	var d := _def(&"quest.main.stage", VT.INT, 0)
	var s := _schema([d])
	_check("N1.pre_valid", s.is_valid(), true)
	_check_true("N1.pre_has", s.has_key(&"quest.main.stage"))
	d.key = &"BadKey" # setter -> changed -> schema invalidate
	_check("N1.post_valid", s.is_valid(), false)
	_check_true("N1.post_no_old_key", not s.has_key(&"quest.main.stage"))
	_check("N1.post_get_null", s.get_definition(&"quest.main.stage"), null)

	# N2: 검증 후 value_type을 바꿔 default와 불일치를 만든다.
	var d2 := _def(&"actor.x.affinity", VT.INT, 5)
	var s2 := _schema([d2])
	_check("N2.pre_valid", s2.is_valid(), true)
	d2.value_type = VT.STRING # 이제 default 5(int)와 불일치
	_check("N2.post_valid", s2.is_valid(), false)
	_check_true("N2.post_code", _has_code(s2.validate(), "default_type_mismatch"))

	# N3: schema_version setter로 무효화.
	var s3 := _schema([_def(&"world.flag.on", VT.BOOL, true)])
	_check("N3.pre_valid", s3.is_valid(), true)
	s3.schema_version = 0 # setter -> invalidate
	_check("N3.post_valid", s3.is_valid(), false)

	# N4: definitions를 통째로 재할당하면 lookup이 새로 만들어진다.
	var s4 := _schema([_def(&"quest.a.b", VT.INT, 1)])
	_check_true("N4.pre_has", s4.has_key(&"quest.a.b"))
	var empty: Array[StateDefinition] = []
	s4.definitions = empty # setter -> invalidate
	_check_true("N4.post_no_old", not s4.has_key(&"quest.a.b"))
	_check("N4.post_count", s4.last_result()["key_count"], 0)


func _test_result_immutability() -> void:
	print("[O] validate/last_result 결과 변조가 내부 상태에 영향 없음")

	# 정상 schema: 반환된 dict를 변조해도 is_valid는 유지된다.
	var s := _schema([_def(&"quest.main.stage", VT.INT, 0)])
	var r := s.validate()
	r["valid"] = false
	r["errors"].append({"code": "tampered"})
	r["error_codes"].append("tampered")
	_check("O.valid_still_true", s.is_valid(), true)
	_check("O.fresh_valid", s.validate()["valid"], true)
	_check_true("O.no_tampered_code", not _has_code(s.validate(), "tampered"))

	# 잘못된 schema: error_codes를 비워도 다음 조회에서 다시 나타난다.
	var bad := _schema([_def(&"Bad", VT.INT, 0)])
	var br := bad.validate()
	br["error_codes"].clear()
	_check_true("O.code_persists", _has_code(bad.last_result(), "key_invalid_format"))


func _test_inplace_array_mutation() -> void:
	print("[P] in-place 배열 변경 시 lookup 무효화 (setter 우회 경로)")

	# P1: 검증 후 invalid Definition을 append → 다음 접근에서 무효 감지.
	var s1 := _schema([_def(&"quest.a.b", VT.INT, 0)])
	_check("P1.pre_valid", s1.is_valid(), true)
	s1.definitions.append(_def(&"Bad", VT.INT, 0)) # setter 우회
	_check("P1.post_valid", s1.is_valid(), false)

	# P2: 검증 후 valid Definition을 append → lookup에 새 key 반영.
	var s2 := _schema([_def(&"quest.a.b", VT.INT, 0)])
	_check("P2.pre_count", s2.last_result()["key_count"], 1)
	s2.definitions.append(_def(&"actor.x.affinity", VT.INT, 5))
	_check("P2.post_count", s2.last_result()["key_count"], 2)
	_check_true("P2.post_has_new", s2.has_key(&"actor.x.affinity"))

	# P3: 검증 후 erase → 해당 key가 lookup에서 사라진다.
	var keep := _def(&"world.flag.on", VT.BOOL, true)
	var drop := _def(&"world.flag.off", VT.BOOL, false)
	var s3 := _schema([keep, drop])
	_check_true("P3.pre_has", s3.has_key(&"world.flag.off"))
	s3.definitions.erase(drop)
	_check_true("P3.post_no_dropped", not s3.has_key(&"world.flag.off"))
	_check_true("P3.post_has_kept", s3.has_key(&"world.flag.on"))

	# P4: 검증 후 remove_at → 크기 변화 감지.
	var s4 := _schema([
		_def(&"quest.a.b", VT.INT, 0),
		_def(&"quest.c.d", VT.INT, 0),
	])
	_check("P4.pre_count", s4.last_result()["key_count"], 2)
	s4.definitions.remove_at(0)
	_check("P4.post_count", s4.last_result()["key_count"], 1)
	_check_true("P4.post_no_removed", not s4.has_key(&"quest.a.b"))

	# P5: 인덱스 대입(크기 동일) → hash 지문으로 감지.
	var s5 := _schema([_def(&"quest.a.b", VT.INT, 0)])
	_check_true("P5.pre_has_old", s5.has_key(&"quest.a.b"))
	s5.definitions[0] = _def(&"actor.y.affinity", VT.INT, 9) # 크기 동일, 인스턴스 교체
	_check_true("P5.post_no_old", not s5.has_key(&"quest.a.b"))
	_check_true("P5.post_has_new", s5.has_key(&"actor.y.affinity"))
	# 인덱스 대입으로 invalid key를 넣으면 invalid가 된다.
	s5.definitions[0] = _def(&"Bad", VT.INT, 0)
	_check("P5.invalid_after_bad", s5.is_valid(), false)


func _cleanup() -> void:
	if FileAccess.file_exists(TMP_PATH):
		var err := DirAccess.remove_absolute(ProjectSettings.globalize_path(TMP_PATH))
		_check("cleanup.removed", err, OK)
	else:
		print("  (cleanup) temp file already absent")
