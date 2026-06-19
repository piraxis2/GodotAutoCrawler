# SG-002 Step 1 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/world_core/save_game/tests/sg002_step1_save_flow_test.tscn
#
# 검증 범위(SG-002 Step 1 완료 조건):
# - manager resolution: 명시 주입 우선 / manager_path 해석 / unavailable·wrong-type·freed report.
# - metadata provider: 없음 / provider only / provider+caller override merge / unavailable / contract invalid.
# - metadata provider 오류 시 fail-closed(manager.save_slot 미호출).
# - save gate: no provider allow / allow / deny / unavailable / contract invalid(non-Dict, non-bool ok).
# - gate ok:false 시 fail-closed(manager.save_slot 미호출), error 구분(save_not_allowed vs gate 오류).
# - list_slots() manager unavailable single failure entry shape.
# - manager report passthrough(save 성공/실패, load, delete, list).
# - save_manual() 성공/실패 report shape 균일화(6키 보존).
# - can_save()가 manager 가용성과 무관함.
#
# 주의: Godot JSON.parse_string은 number를 float로 읽는다(`7`->`7.0`). int 의미 비교는 int()로 한다.
extends Node

const PREFIX := "sg002s1_"

var _failures: int = 0


func _ready() -> void:
	_cleanup_test_slots()

	_test_metadata_no_provider()
	_test_metadata_provider_only()
	_test_metadata_provider_caller_override()
	_test_metadata_provider_unavailable()
	_test_metadata_provider_non_object()
	_test_metadata_provider_contract_invalid()
	_test_gate_no_provider_allow()
	_test_gate_provider_allow()
	_test_gate_provider_deny()
	_test_gate_provider_unavailable()
	_test_gate_provider_non_object()
	_test_gate_provider_contract_invalid()
	_test_manager_unavailable_reports()
	_test_manager_wrong_type()
	_test_manager_freed_falls_through()
	_test_manager_path_resolution()
	_test_list_slots_unavailable_entry_shape()
	_test_manager_report_passthrough_save()
	_test_manager_report_passthrough_save_failure()
	_test_load_delete_list_passthrough()
	_test_save_report_shape_uniform()
	_test_can_save_independent_of_manager()

	_cleanup_test_slots()

	if _failures == 0:
		print("[SG-002 Step1] ALL PASS")
		get_tree().quit(0)
	else:
		print("[SG-002 Step1] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- fakes -----------------------------------------------------------

class FakeSection extends SaveSection:
	var payload_data: Dictionary = {}
	var last_restored: Dictionary = {}

	func capture_save() -> Dictionary:
		return {"ok": true, "payload": payload_data.duplicate(true), "reason": &""}

	func validate_save(_p: Dictionary) -> Dictionary:
		return {"ok": true, "reason": &""}

	func restore_save(p: Dictionary) -> Dictionary:
		last_restored = p.duplicate(true)
		return {"ok": true, "reason": &""}


# save_slot 호출 횟수를 기록하는 manager(fail-closed 검증용). is SaveGameManager 유지.
class SpyManager extends SaveGameManager:
	var save_calls: int = 0

	func save_slot(slot_id, metadata: Dictionary = {}) -> Dictionary:
		save_calls += 1
		return super.save_slot(slot_id, metadata)


# duck-type metadata provider.
class MetaProvider extends RefCounted:
	var data: Dictionary = {}
	var return_non_dict: bool = false
	var last_slot_id = null

	func make_save_metadata(slot_id):
		last_slot_id = slot_id
		if return_non_dict:
			return "not a dictionary"
		return data.duplicate(true)


# make_save_metadata 메서드가 없는 provider(unavailable 경로).
class NoMethodProvider extends RefCounted:
	var unrelated := 1


# duck-type save gate provider.
class GateProvider extends RefCounted:
	var allow: bool = true
	var reason = &""
	var return_non_dict: bool = false
	var return_non_bool_ok: bool = false
	var last_slot_id = null

	func query_save_gate(slot_id):
		last_slot_id = slot_id
		if return_non_dict:
			return [1, 2, 3]
		if return_non_bool_ok:
			return {"ok": "yes", "reason": reason}
		return {"ok": allow, "reason": reason}


# query_save_gate 메서드가 없는 gate provider(save_gate_unavailable 경로).
class NoMethodGate extends RefCounted:
	var unrelated := 1


# --- helpers ---------------------------------------------------------

func _new_flow() -> SaveFlow:
	var flow := SaveFlow.new()
	add_child(flow)
	return flow


func _new_manager() -> SpyManager:
	var m := SpyManager.new()
	add_child(m)
	return m


func _flow_with_manager(m: SaveGameManager) -> SaveFlow:
	var flow := _new_flow()
	flow.set_manager(m)
	return flow


# manager를 해석하지 못하게 만든 flow(주입 없음 + 존재하지 않는 path).
func _flow_no_manager() -> SaveFlow:
	var flow := _new_flow()
	flow.manager_path = ^"/root/__sg002_no_such_manager__"
	return flow


func _make_section(id: StringName, payload: Dictionary = {}) -> FakeSection:
	var s := FakeSection.new()
	s.section_id = id
	s.payload_data = payload
	add_child(s)
	return s


func _slot(name: String) -> String:
	return PREFIX + name


func _slot_file(sid: String) -> String:
	return SaveGameManager.SAVES_DIR + "/" + sid + ".json"


func _cleanup_test_slots() -> void:
	if not DirAccess.dir_exists_absolute(SaveGameManager.SAVES_DIR):
		return
	var d := DirAccess.open(SaveGameManager.SAVES_DIR)
	if d == null:
		return
	for fname in d.get_files():
		if fname.begins_with(PREFIX):
			d.remove(fname)


func _check(name: String, actual, expected) -> void:
	if str(actual) == str(expected):
		print("  PASS %s -> %s" % [name, str(actual)])
	else:
		_failures += 1
		print("  FAIL %s -> got %s, expected %s" % [name, str(actual), str(expected)])


func _check_true(name: String, cond: bool) -> void:
	_check(name, cond, true)


# 6키 shape 보존 검증.
func _check_report_keys(prefix: String, report: Dictionary) -> void:
	for key in ["ok", "slot_id", "error", "metadata", "manager_report", "gate"]:
		_check_true("%s.has_%s" % [prefix, key], report.has(key))


# --- metadata --------------------------------------------------------

func _test_metadata_no_provider() -> void:
	print("[A] metadata: provider 없음 -> caller만")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	var r := flow.save_manual(_slot("meta_none"), {"display_name": "Caller"})
	_check("A.ok", r["ok"], true)
	_check("A.meta_display", r["metadata"]["display_name"], "Caller")
	_check("A.meta_size", r["metadata"].size(), 1)


func _test_metadata_provider_only() -> void:
	print("[B] metadata: provider only")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	var p := MetaProvider.new()
	p.data = {"chapter": "Forest", "play_time_seconds": 120}
	flow.set_metadata_provider(p)
	var sid := _slot("meta_provider")
	var r := flow.save_manual(sid)
	_check("B.ok", r["ok"], true)
	_check("B.chapter", r["metadata"]["chapter"], "Forest")
	_check("B.playtime", int(r["metadata"]["play_time_seconds"]), 120)
	_check("B.provider_got_slot", p.last_slot_id, sid)


func _test_metadata_provider_caller_override() -> void:
	print("[C] metadata: provider base + caller override(shallow merge)")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	var p := MetaProvider.new()
	p.data = {"chapter": "Forest", "play_time_seconds": 120}
	flow.set_metadata_provider(p)
	var r := flow.save_manual(_slot("meta_merge"),
		{"display_name": "Before Boss", "chapter": "Boss Gate"})
	_check("C.ok", r["ok"], true)
	# caller가 같은 key(chapter)를 override, provider-only key(play_time)는 유지, caller-only(display_name) 추가.
	_check("C.chapter_override", r["metadata"]["chapter"], "Boss Gate")
	_check("C.playtime_kept", int(r["metadata"]["play_time_seconds"]), 120)
	_check("C.display_added", r["metadata"]["display_name"], "Before Boss")
	_check("C.size", r["metadata"].size(), 3)


func _test_metadata_provider_unavailable() -> void:
	print("[D] metadata: provider 메서드 없음 -> unavailable, fail-closed")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	flow.set_metadata_provider(NoMethodProvider.new())
	var sid := _slot("meta_unavail")
	var r := flow.save_manual(sid)
	_check("D.ok", r["ok"], false)
	_check("D.error", r["error"], &"metadata_provider_unavailable")
	_check("D.no_save_call", m.save_calls, 0)
	_check("D.no_file", FileAccess.file_exists(_slot_file(sid)), false)
	_check_report_keys("D", r)
	# gate는 검사됐으므로(provider 없음 allow) gate.ok true, manager_report는 {}.
	_check("D.gate_ok", r["gate"]["ok"], true)
	_check("D.manager_report_empty", r["manager_report"].is_empty(), true)


func _test_metadata_provider_non_object() -> void:
	print("[D2] metadata: non-Object provider -> unavailable, fail-closed(SCRIPT ERROR 없음)")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	# Object가 아닌 값(String/int/Array)을 넘겨도 타입 오류 없이 unavailable로 정규화돼야 한다.
	for non_object in ["not an object", 42, [1, 2]]:
		m.save_calls = 0
		flow.set_metadata_provider(non_object)
		var sid := _slot("meta_nonobj")
		var r := flow.save_manual(sid)
		_check("D2.error[%s]" % typeof(non_object), r["error"], &"metadata_provider_unavailable")
		_check("D2.no_save_call[%s]" % typeof(non_object), m.save_calls, 0)
		_check("D2.no_file[%s]" % typeof(non_object), FileAccess.file_exists(_slot_file(sid)), false)


func _test_metadata_provider_contract_invalid() -> void:
	print("[E] metadata: provider 반환 non-Dictionary -> contract invalid, fail-closed")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	var p := MetaProvider.new()
	p.return_non_dict = true
	flow.set_metadata_provider(p)
	var sid := _slot("meta_contract")
	var r := flow.save_manual(sid)
	_check("E.ok", r["ok"], false)
	_check("E.error", r["error"], &"metadata_provider_contract_invalid")
	_check("E.no_save_call", m.save_calls, 0)
	_check("E.no_file", FileAccess.file_exists(_slot_file(sid)), false)


# --- save gate -------------------------------------------------------

func _test_gate_no_provider_allow() -> void:
	print("[F] gate: provider 없음 -> can_save allow")
	var flow := _new_flow()
	var g := flow.can_save(&"any")
	_check("F.ok", g["ok"], true)
	_check("F.reason", g["reason"], &"")


func _test_gate_provider_allow() -> void:
	print("[G] gate: provider allow")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	var gate := GateProvider.new()
	gate.allow = true
	flow.set_save_gate_provider(gate)
	var sid := _slot("gate_allow")
	var g := flow.can_save(sid)
	_check("G.can_save_ok", g["ok"], true)
	_check("G.provider_got_slot", gate.last_slot_id, sid)
	var r := flow.save_manual(sid)
	_check("G.save_ok", r["ok"], true)
	_check("G.save_called", m.save_calls, 1)
	_check("G.gate_in_report", r["gate"]["ok"], true)


func _test_gate_provider_deny() -> void:
	print("[H] gate: provider deny -> save_not_allowed, fail-closed")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	var gate := GateProvider.new()
	gate.allow = false
	gate.reason = &"in_combat"
	flow.set_save_gate_provider(gate)
	var g := flow.can_save(&"any")
	_check("H.can_save_ok", g["ok"], false)
	_check("H.can_save_reason", g["reason"], &"in_combat")
	var sid := _slot("gate_deny")
	var r := flow.save_manual(sid)
	_check("H.save_ok", r["ok"], false)
	_check("H.error", r["error"], &"save_not_allowed")
	_check("H.no_save_call", m.save_calls, 0)
	_check("H.no_file", FileAccess.file_exists(_slot_file(sid)), false)
	_check("H.metadata_empty", r["metadata"].is_empty(), true)
	_check("H.manager_report_empty", r["manager_report"].is_empty(), true)
	_check("H.gate_preserved", r["gate"]["reason"], &"in_combat")
	_check_report_keys("H", r)


func _test_gate_provider_unavailable() -> void:
	print("[I] gate: 메서드 없음 -> save_gate_unavailable, fail-closed")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	flow.set_save_gate_provider(NoMethodGate.new())
	var g := flow.can_save(&"any")
	_check("I.can_save_ok", g["ok"], false)
	_check("I.can_save_reason", g["reason"], &"save_gate_unavailable")
	var sid := _slot("gate_unavail")
	var r := flow.save_manual(sid)
	_check("I.save_ok", r["ok"], false)
	_check("I.error", r["error"], &"save_gate_unavailable")
	_check("I.no_save_call", m.save_calls, 0)
	_check("I.no_file", FileAccess.file_exists(_slot_file(sid)), false)


func _test_gate_provider_non_object() -> void:
	print("[I2] gate: non-Object provider -> save_gate_unavailable, fail-closed(SCRIPT ERROR 없음)")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	for non_object in ["not an object", 42, [1, 2]]:
		m.save_calls = 0
		flow.set_save_gate_provider(non_object)
		var g := flow.can_save(&"any")
		_check("I2.can_save_ok[%s]" % typeof(non_object), g["ok"], false)
		_check("I2.can_save_reason[%s]" % typeof(non_object), g["reason"], &"save_gate_unavailable")
		var sid := _slot("gate_nonobj")
		var r := flow.save_manual(sid)
		_check("I2.save_error[%s]" % typeof(non_object), r["error"], &"save_gate_unavailable")
		_check("I2.no_save_call[%s]" % typeof(non_object), m.save_calls, 0)
		_check("I2.no_file[%s]" % typeof(non_object), FileAccess.file_exists(_slot_file(sid)), false)


func _test_gate_provider_contract_invalid() -> void:
	print("[J] gate: 반환 non-Dict / ok 비-bool -> save_gate_contract_invalid, fail-closed")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	# (1) non-Dictionary 반환.
	var gate := GateProvider.new()
	gate.return_non_dict = true
	flow.set_save_gate_provider(gate)
	var g1 := flow.can_save(&"any")
	_check("J.nondict_ok", g1["ok"], false)
	_check("J.nondict_reason", g1["reason"], &"save_gate_contract_invalid")
	var sid := _slot("gate_contract")
	var r1 := flow.save_manual(sid)
	_check("J.nondict_save_error", r1["error"], &"save_gate_contract_invalid")
	_check("J.nondict_no_save_call", m.save_calls, 0)
	# (2) ok가 bool이 아님.
	var gate2 := GateProvider.new()
	gate2.return_non_bool_ok = true
	flow.set_save_gate_provider(gate2)
	var g2 := flow.can_save(&"any")
	_check("J.nonbool_ok", g2["ok"], false)
	_check("J.nonbool_reason", g2["reason"], &"save_gate_contract_invalid")
	var r2 := flow.save_manual(sid)
	_check("J.nonbool_save_error", r2["error"], &"save_gate_contract_invalid")
	_check("J.nonbool_no_save_call", m.save_calls, 0)
	_check("J.no_file", FileAccess.file_exists(_slot_file(sid)), false)


# --- manager resolution ----------------------------------------------

func _test_manager_unavailable_reports() -> void:
	print("[K] manager unavailable: save/load/delete general report, has false")
	var flow := _flow_no_manager()
	var s := flow.save_manual(_slot("noman"))
	_check("K.save_ok", s["ok"], false)
	_check("K.save_error", s["error"], &"manager_unavailable")
	_check_report_keys("K.save", s)
	var l := flow.load_manual(_slot("noman"))
	_check("K.load_ok", l["ok"], false)
	_check("K.load_error", l["error"], &"manager_unavailable")
	var d := flow.delete_slot(_slot("noman"))
	_check("K.delete_ok", d["ok"], false)
	_check("K.delete_error", d["error"], &"manager_unavailable")
	_check("K.has_slot", flow.has_slot(_slot("noman")), false)


func _test_manager_wrong_type() -> void:
	print("[L] manager_path가 SaveGameManager가 아니면 unavailable")
	var flow := _new_flow()
	var plain := Node.new()
	plain.name = "SG002WrongType"
	add_child(plain)
	flow.manager_path = plain.get_path()
	var r := flow.save_manual(_slot("wrongtype"))
	_check("L.error", r["error"], &"manager_unavailable")
	plain.queue_free()


func _test_manager_freed_falls_through() -> void:
	print("[M] 주입 manager가 freed면 manager_path로 폴백/없으면 unavailable")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _new_flow()
	flow.set_manager(m)
	flow.manager_path = ^"/root/__sg002_no_such_manager__"
	# 정상 동작 확인 후 free.
	_check("M.before_has", flow.has_slot(_slot("none")), false)  # 파일 없음이지만 manager는 해석됨
	remove_child(m)
	m.free()
	# freed 주입 + 존재하지 않는 path -> manager_unavailable.
	var r := flow.save_manual(_slot("freed"))
	_check("M.after_error", r["error"], &"manager_unavailable")


func _test_manager_path_resolution() -> void:
	print("[N] manager_path 해석(set_manager 주입 없이 NodePath로 해석)")
	# manager는 in-tree(테스트 노드 하위)이고, set_manager를 호출하지 않아 resolution이 manager_path
	# 분기를 타게 한다. NodePath는 절대경로(get_path)로 어디서든 해석된다.
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _new_flow()
	flow.manager_path = m.get_path()
	var sid := _slot("pathres")
	var r := flow.save_manual(sid)
	_check("N.save_ok", r["ok"], true)
	_check("N.has_slot", flow.has_slot(sid), true)


func _test_list_slots_unavailable_entry_shape() -> void:
	print("[O] list_slots() manager unavailable single failure entry shape")
	var flow := _flow_no_manager()
	var slots := flow.list_slots()
	_check("O.size", slots.size(), 1)
	_check("O.ok", slots[0]["ok"], false)
	_check("O.slot_id", slots[0]["slot_id"], &"")
	_check("O.error", slots[0]["error"], &"manager_unavailable")


# --- passthrough -----------------------------------------------------

func _test_manager_report_passthrough_save() -> void:
	print("[P] manager report passthrough: save 성공")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"affinity": 50}))
	var flow := _flow_with_manager(m)
	var sid := _slot("passthrough_save")
	var r := flow.save_manual(sid, {"display_name": "PT"})
	_check("P.ok", r["ok"], true)
	_check("P.error_empty", r["error"], &"")
	# manager report 원본 보존(path/sections/slot_id).
	_check_true("P.manager_has_path", r["manager_report"].has("path"))
	_check_true("P.manager_has_sections", r["manager_report"].has("sections"))
	_check("P.manager_slot_id", r["manager_report"]["slot_id"], StringName(sid))


func _test_manager_report_passthrough_save_failure() -> void:
	print("[Q] manager report passthrough: invalid_slot_id 그대로 노출")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	# 잘못된 slot_id -> manager가 invalid_slot_id 반환, facade가 error로 노출 + 원본 보존.
	var r := flow.save_manual("bad id")
	_check("Q.ok", r["ok"], false)
	_check("Q.error", r["error"], &"invalid_slot_id")
	_check("Q.manager_report_error", r["manager_report"]["error"], &"invalid_slot_id")
	_check("Q.save_called", m.save_calls, 1)  # gate/metadata 통과 후 실제 호출됨
	_check_report_keys("Q", r)


func _test_load_delete_list_passthrough() -> void:
	print("[R] load/delete/list passthrough(정보 손실 없음)")
	var m := _new_manager()
	var sec := _make_section(&"world", {"affinity": 7, "name": "x"})
	m.register_section(sec)
	var flow := _flow_with_manager(m)
	var sid := _slot("ldl")
	_check("R.save_ok", flow.save_manual(sid)["ok"], true)
	# load: manager.load_slot의 recovered_from_backup/source/restore 보존.
	sec.last_restored = {}
	var lr := flow.load_manual(sid)
	_check("R.load_ok", lr["ok"], true)
	_check_true("R.load_has_recovered", lr.has("recovered_from_backup"))
	_check("R.load_source", lr["source"], &"primary")
	_check_true("R.load_has_restore", lr.has("restore"))
	_check("R.restored_affinity", int(sec.last_restored["affinity"]), 7)
	# list: manager list 그대로.
	var found := false
	for e in flow.list_slots():
		if String(e["slot_id"]) == sid:
			found = true
			_check("R.list_meta_present", e.has("metadata"), true)
	_check_true("R.list_found", found)
	# delete: manager delete 그대로.
	var dr := flow.delete_slot(sid)
	_check("R.delete_ok", dr["ok"], true)
	_check("R.has_after", flow.has_slot(sid), false)


func _test_save_report_shape_uniform() -> void:
	print("[S] save_manual report shape 균일화(성공/실패 모두 6키)")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"x": 1}))
	var flow := _flow_with_manager(m)
	# 성공.
	var ok_r := flow.save_manual(_slot("shape_ok"))
	_check_report_keys("S.success", ok_r)
	_check_true("S.success_metadata_dict", ok_r["metadata"] is Dictionary)
	_check_true("S.success_manager_dict", ok_r["manager_report"] is Dictionary)
	_check_true("S.success_gate_dict", ok_r["gate"] is Dictionary)
	# 실패(manager unavailable).
	var fail_r := _flow_no_manager().save_manual(_slot("shape_fail"))
	_check_report_keys("S.fail", fail_r)
	_check("S.fail_metadata_empty", fail_r["metadata"].is_empty(), true)
	_check("S.fail_manager_empty", fail_r["manager_report"].is_empty(), true)
	_check("S.fail_gate_empty", fail_r["gate"].is_empty(), true)


func _test_can_save_independent_of_manager() -> void:
	print("[T] can_save()는 manager 가용성과 무관")
	# manager 없음 + gate provider 없음 -> can_save allow.
	var flow := _flow_no_manager()
	var g := flow.can_save(&"any")
	_check("T.can_save_ok", g["ok"], true)
	# 그래도 save_manual은 manager_unavailable.
	var r := flow.save_manual(_slot("indep"))
	_check("T.save_error", r["error"], &"manager_unavailable")
