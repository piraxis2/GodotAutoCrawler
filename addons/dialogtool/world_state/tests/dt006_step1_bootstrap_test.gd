# DT-006 Step 1 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/dialogtool/world_state/tests/dt006_step1_bootstrap_test.tscn
#
# 검증 범위:
# - 게임용 bootstrap Schema(.tres)가 valid이며 key_count == 6
# - 각 key의 type/default/lifetime/writable가 확정 계약과 일치
# - Store scene을 instantiate하면 is_store_ready() == true (invalid 오류 없음)
# - Schema .tres 저장 -> cache 무시 재로드 왕복에서 필드 보존
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime
const SCHEMA_PATH := "res://addons/dialogtool/examples/world_state_schema_example.tres"
const STORE_SCENE := "res://addons/dialogtool/world_state/world_state_store.tscn"
const TMP_PATH := "user://dt006_tmp_schema.tres"

# 확정된 bootstrap 계약(소스 오브 트루스): key -> [value_type, default, lifetime, writable]
var _expected := {
	&"quest.main.stage": [VT.INT, 0, LT.SAVE, true],
	&"actor.example.affinity": [VT.INT, 0, LT.SAVE, true],
	&"player.health": [VT.FLOAT, 100.0, LT.SAVE, true],
	&"player.display_name": [VT.STRING, "", LT.SAVE, true],
	&"world.build.channel": [VT.STRING_NAME, &"dev", LT.SAVE, false],
	&"session.intro.seen": [VT.BOOL, false, LT.SESSION, true],
}

var _failures: int = 0


func _ready() -> void:
	_test_schema_valid_and_contract()
	_test_store_scene_ready()
	_test_schema_roundtrip()

	if _failures == 0:
		print("[DT-006 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-006 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# 로드한 schema가 확정 계약과 일치하는지 검사한다.
func _assert_contract(prefix: String, schema: StateSchema) -> void:
	var r := schema.validate()
	_check("%s.valid" % prefix, r["valid"], true)
	_check("%s.key_count" % prefix, r["key_count"], 6)
	for key in _expected:
		var exp: Array = _expected[key]
		var d := schema.get_definition(key)
		_check_true("%s.has[%s]" % [prefix, key], d != null)
		if d == null:
			continue
		_check("%s.type[%s]" % [prefix, key], d.value_type, exp[0])
		_check("%s.default[%s]" % [prefix, key], d.default_value, exp[1])
		_check("%s.default_typeof[%s]" % [prefix, key], typeof(d.default_value), StateDefinition.builtin_type_for(exp[0]))
		_check("%s.lifetime[%s]" % [prefix, key], d.lifetime, exp[2])
		_check("%s.writable[%s]" % [prefix, key], d.writable, exp[3])


func _test_schema_valid_and_contract() -> void:
	print("[A] bootstrap Schema .tres valid + 계약 일치")
	var schema: StateSchema = ResourceLoader.load(SCHEMA_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	_check_true("A.loaded", schema != null)
	if schema == null:
		return
	_assert_contract("A", schema)


func _test_store_scene_ready() -> void:
	print("[B] Store scene instantiate -> ready (invalid 오류 없음)")
	var packed: PackedScene = ResourceLoader.load(STORE_SCENE, "", ResourceLoader.CACHE_MODE_IGNORE)
	_check_true("B.scene_loaded", packed != null)
	if packed == null:
		return
	var store = packed.instantiate()
	# scene의 _ready()가 자동 initialize 하도록 트리에 추가한다.
	add_child(store)
	_check("B.ready", store.is_store_ready(), true)
	# bootstrap default를 읽을 수 있다.
	_check("B.stage_default", store.get_value(&"quest.main.stage"), 0)
	_check("B.health_default", store.get_value(&"player.health"), 100.0)
	_check("B.channel_default", store.get_value(&"world.build.channel"), "dev")
	_check("B.channel_typeof", typeof(store.get_value(&"world.build.channel")), TYPE_STRING_NAME)
	_check("B.session_default", store.get_value(&"session.intro.seen"), false)
	# read-only key는 gameplay set 거부.
	_check("B.channel_readonly", store.set_value(&"world.build.channel", &"prod"), ERR_UNAUTHORIZED)
	store.queue_free()


func _test_schema_roundtrip() -> void:
	print("[C] Schema .tres 저장 -> cache 무시 재로드 왕복 보존")
	var schema: StateSchema = ResourceLoader.load(SCHEMA_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	if schema == null:
		_check("C.loaded", false, true)
		return
	var err := ResourceSaver.save(schema, TMP_PATH)
	_check("C.save_ok", err, OK)
	var reloaded: StateSchema = ResourceLoader.load(TMP_PATH, "", ResourceLoader.CACHE_MODE_IGNORE)
	_check_true("C.reloaded", reloaded != null)
	if reloaded != null:
		_assert_contract("C", reloaded)
	_cleanup()


func _cleanup() -> void:
	if FileAccess.file_exists(TMP_PATH):
		var err := DirAccess.remove_absolute(ProjectSettings.globalize_path(TMP_PATH))
		_check("C.cleanup", err, OK)
