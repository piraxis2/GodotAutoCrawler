# DT-005 Step 6 통합 회귀 테스트.
# 실행:
#   godot --headless --path <project> res://addons/dialogtool/world_state/tests/dt005_step6_integration_test.tscn
#
# 한 store를 default 초기화 -> batch -> 단일 set/reset -> SAVE/SESSION -> snapshot JSON 왕복 ->
# reset_lifetime -> Dialogue read provider 주입까지 end-to-end로 묶어 검증한다(매트릭스 통합).
extends Node

const VT := StateDefinition.StateValueType
const LT := StateDefinition.StateLifetime

var _failures: int = 0
var _value_log: Array = []
var _reset_log: Array = []
var _import_log: Array = []


func _ready() -> void:
	_run_integration()
	if _failures == 0:
		print("[DT-005 Step6] ALL PASS")
		get_tree().quit(0)
	else:
		print("[DT-005 Step6] FAILED: %d assertion(s)" % _failures)
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


func _schema(defs: Array) -> StateSchema:
	var s := StateSchema.new()
	var typed: Array[StateDefinition] = []
	for d in defs:
		typed.append(d)
	s.definitions = typed
	return s


func _batch(arr: Array) -> Array[Dictionary]:
	var typed: Array[Dictionary] = []
	for d in arr:
		typed.append(d)
	return typed


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


func _errors_have(report: Dictionary, reason: String) -> bool:
	for e in report["errors"]:
		if e.get("reason") == reason:
			return true
	return false


func _log_keys() -> Array:
	var out: Array = []
	for e in _value_log:
		out.append(String(e["key"]))
	return out


func _n(type: StringName, params: Dictionary = {}) -> Dictionary:
	return {"type": type, "params": params}


func _c(from_id: int, from_port: int, to_id: int, to_port: int) -> Dictionary:
	return {"from_node_id": from_id, "from_port": from_port, "to_node_id": to_id, "to_port": to_port}


# --- 통합 시나리오 ----------------------------------------------------

func _run_integration() -> void:
	var store := WorldStateStore.new()
	store.schema = _schema([
		_def(&"quest.main.stage", VT.INT, 0),
		_def(&"actor.noabel.affinity", VT.INT, 0),
		_def(&"player.name", VT.STRING, "hero"),
		_def(&"world.locked", VT.BOOL, true, LT.SAVE, false),   # read-only
		_def(&"dialogue.first_met", VT.BOOL, false, LT.SESSION),
	])
	store.value_changed.connect(func(k, o, n): _value_log.append({"key": k, "old": o, "new": n}))
	store.state_reset.connect(func(lt): _reset_log.append(lt))
	store.snapshot_imported.connect(func(r): _import_log.append(r))

	# [A] default 초기화
	print("[A] default 초기화")
	_check("A.ready", store.initialize(), true)
	_check("A.stage", store.get_value(&"quest.main.stage"), 0)
	_check("A.name", store.get_value(&"player.name"), "hero")
	_check("A.locked", store.get_value(&"world.locked"), true)
	_check("A.session", store.get_value(&"dialogue.first_met"), false)

	# [B] atomic batch 성공 (입력 순서 signal + diff)
	print("[B] atomic batch 성공")
	_value_log.clear()
	var rb := store.apply_batch(_batch([
		{"key": &"player.name", "value": "noabel"},
		{"key": &"quest.main.stage", "value": 2},
		{"key": &"actor.noabel.affinity", "value": 35},
	]))
	_check("B.applied", rb["applied"], true)
	_check("B.diff", rb["diff"].size(), 3)
	_check("B.order", _log_keys(), ["player.name", "quest.main.stage", "actor.noabel.affinity"])
	_check("B.stage", store.get_value(&"quest.main.stage"), 2)

	# [C] batch 실패 (타입 오류) -> 전체 거부, 무변경
	print("[C] atomic batch 실패")
	_value_log.clear()
	var rc := store.apply_batch(_batch([
		{"key": &"actor.noabel.affinity", "value": 99},
		{"key": &"quest.main.stage", "value": "bad"},
	]))
	_check("C.applied", rc["applied"], false)
	_check_true("C.type_error", _errors_have(rc, "type_mismatch"))
	_check("C.affinity_unchanged", store.get_value(&"actor.noabel.affinity"), 35)
	_check("C.no_signal", _value_log.size(), 0)

	# [D] 단일 set/reset 정상·비정상
	print("[D] 단일 set/reset")
	_check("D.set_ok", store.set_value(&"quest.main.stage", 3), OK)
	_check("D.set_type", store.set_value(&"quest.main.stage", "x"), ERR_INVALID_DATA)
	_check("D.set_readonly", store.set_value(&"world.locked", false), ERR_UNAUTHORIZED)
	_check("D.reset_readonly_ok", store.reset_value(&"world.locked"), OK)  # 시스템 reset 허용
	_check("D.reset_stage", store.reset_value(&"quest.main.stage"), OK)
	_check("D.stage_default", store.get_value(&"quest.main.stage"), 0)

	# 이후 snapshot 비교를 위해 의미 있는 SAVE 값으로 다시 설정
	store.set_value(&"quest.main.stage", 4)
	store.set_value(&"actor.noabel.affinity", 50)
	store.set_value(&"dialogue.first_met", true)  # SESSION

	# [E] export: SAVE만, SESSION 제외
	print("[E] export SAVE-only")
	var snap := store.export_snapshot()
	_check_true("E.has_stage", snap["values"].has("quest.main.stage"))
	_check_true("E.no_session", not snap["values"].has("dialogue.first_met"))

	# [F] JSON 왕복 import (replace-load): SAVE 복원, SESSION 미변경
	print("[F] JSON snapshot 왕복 import")
	var parsed: Variant = JSON.parse_string(JSON.stringify(snap))
	store.set_value(&"quest.main.stage", 999)
	store.set_value(&"player.name", "scrambled")
	var rf := store.import_snapshot(parsed)
	_check("F.no_errors", rf["errors"].size(), 0)
	_check("F.stage_restored", store.get_value(&"quest.main.stage"), 4)
	_check("F.affinity_restored", store.get_value(&"actor.noabel.affinity"), 50)
	_check("F.name_restored", store.get_value(&"player.name"), "noabel")
	_check("F.session_untouched", store.get_value(&"dialogue.first_met"), true)

	# [G] reset_lifetime(SESSION): SESSION만 default, SAVE 유지, state_reset 발행
	print("[G] reset_lifetime SESSION")
	_reset_log.clear()
	store.reset_lifetime(LT.SESSION)
	_check("G.session_default", store.get_value(&"dialogue.first_met"), false)
	_check("G.save_kept", store.get_value(&"quest.main.stage"), 4)
	_check("G.reset_signal", _reset_log, [LT.SESSION])

	# [H] Dialogue read provider 통합: 실제 store를 Player에 주입
	print("[H] Dialogue read provider 통합 (실제 store)")
	var player := DialoguePlayer.new()
	player.set_read_state_provider(store)
	_check("H.has_state", player.has_state(&"quest.main.stage"), true)
	_check("H.read", player.read_state(&"quest.main.stage"), 4)
	_check("H.try_missing", player.try_read_state(&"nope.nope", -7), -7)
	# store mutation이 dialogue read에 즉시 반영된다.
	store.set_value(&"quest.main.stage", 7)
	_check("H.read_after_mutation", player.read_state(&"quest.main.stage"), 7)

	# Say 그래프를 store provider와 함께 실행(데이터 평가는 provider와 독립이지만 통합 동작 확인)
	var says: Array = []
	player.ui_request.connect(func(req: Dictionary):
		if req.get("type") == "display_text":
			says.append(req.get("say")))
	var res := DialogueGraphResource.new()
	res.runtime_nodes = {0: _n(&"start"), 1: _n(&"say", {"text": "hello"}), 2: _n(&"end")}
	var conns: Array[Dictionary] = [_c(0, 0, 1, 0), _c(1, 0, 2, 0)]
	res.runtime_connections = conns
	res.start_node_id = 0
	player.start_dialogue(res)
	_check("H.dialogue_say", says, ["hello"])
	player.free()

	# [I] 신호 무결성: import는 snapshot_imported를 발행했다
	print("[I] 신호 무결성")
	_check("I.import_signal", _import_log.size() >= 1, true)

	store.free()
