# SG-003 Step 2 검증용 헤드리스 테스트.
# 실행:
#   godot --headless --path <project> res://addons/save_game/tests/sg003_step2_host_flow_test.tscn
#
# 목적: SG-003 Step 1 문서(SaveGame-User-Guide §12)의 host save slot UI integration contract가 실제
# `SaveFlow + SaveGameManager` report 위에서 동작 가능한지 production UI 없이 검증한다. 검증은 테스트
# 파일 내부 test-only `FakeSaveSlotHostController`(host가 유지할 수 있는 상태 모델만 흉내, UI 위젯 아님)로
# 한다. 이 controller는 public API가 아니다.
#
# 검증 범위(SG-003 Step 2 완료 조건):
# - whole-list `manager_unavailable`(단일 slot_id:&"" entry)을 list_state=manager_unavailable + slot count 0으로 분류.
# - non-empty slot_id를 가진 per-slot `parse_error`/`corrupt`를 failure card로 격리, 정상 slot 표시 비차단.
# - {}/unknown keys/wrong display-key types metadata를 host normalization이 crash 없이 fallback 처리 + raw 보존.
# - save gate deny/unavailable/contract invalid가 save를 fail-closed(manager.save_slot 미호출).
# - save_manual 성공/실패 report의 6키 shape를 host state(last_action)가 보존.
# - load_manual의 recovered_from_backup/source/restore + raw 실패 reason을 host state가 보존.
# - delete 후 list refresh 흐름.
#
# 주의: Godot JSON.parse_string은 number를 float로 읽는다(`7`->`7.0`). int 의미 비교는 int()로 한다.
extends Node

const PREFIX := "sg003s2_"

var _failures: int = 0


func _ready() -> void:
	_cleanup_test_slots()

	_test_list_manager_unavailable()
	_test_list_per_slot_failure_isolation()
	_test_metadata_fallback()
	_test_save_gate_fail_closed()
	_test_save_report_shape_preserved()
	_test_load_backup_recovery_preserved()
	_test_load_failure_reason_preserved()
	_test_delete_then_refresh()

	_cleanup_test_slots()

	if _failures == 0:
		print("[SG-003 Step2] ALL PASS")
		get_tree().quit(0)
	else:
		print("[SG-003 Step2] FAILED: %d assertion(s)" % _failures)
		get_tree().quit(1)


# --- fakes -----------------------------------------------------------

class FakeSection extends SaveSection:
	var payload_data: Dictionary = {}
	var last_restored: Variant = null

	func capture_save() -> Dictionary:
		return {"ok": true, "payload": payload_data.duplicate(true), "reason": &""}

	func validate_save(_p: Dictionary) -> Dictionary:
		return {"ok": true, "reason": &""}

	func restore_save(p: Dictionary) -> Dictionary:
		last_restored = p.duplicate(true)
		return {"ok": true, "reason": &""}


# save_slot 호출 횟수를 기록하는 manager(gate fail-closed 검증용). is SaveGameManager 유지.
class SpyManager extends SaveGameManager:
	var save_calls: int = 0

	func save_slot(slot_id, metadata: Dictionary = {}) -> Dictionary:
		save_calls += 1
		return super.save_slot(slot_id, metadata)


# duck-type save gate provider(deny/contract invalid 경로).
class GateProvider extends RefCounted:
	var allow: bool = true
	var reason = &""
	var return_non_dict: bool = false

	func query_save_gate(_slot_id):
		if return_non_dict:
			return [1, 2, 3]
		return {"ok": allow, "reason": reason}


# query_save_gate 메서드가 없는 gate provider(save_gate_unavailable 경로).
class NoMethodGate extends RefCounted:
	var unrelated := 1


# test-only host controller. UI 위젯이 아니라 host가 유지할 수 있는 상태 모델만 만든다.
# SaveGame core에 추가되는 제품 helper가 아니며 public API도 아니다(SG-003 Resolved Recommendation 1).
class FakeSaveSlotHostController extends RefCounted:
	var flow: SaveFlow
	# 문서(Task)의 상태 모델 shape.
	var list_state: StringName = &"ready"
	var slot_cards: Array = []
	var selected_slot_id = &""
	var can_save_state: Dictionary = {}
	var last_action: Dictionary = {}

	func _init(f: SaveFlow) -> void:
		flow = f

	# §12.1 slot list 분류.
	func refresh_list() -> void:
		var entries := flow.list_slots()
		# whole-list failure: 단일 manager_unavailable entry(slot_id:&"").
		if entries.size() == 1 and not entries[0].get("ok", false) \
				and String(entries[0].get("slot_id", &"")) == "" \
				and entries[0].get("error", &"") == &"manager_unavailable":
			list_state = &"manager_unavailable"
			slot_cards = []
			return
		list_state = &"ready"
		var cards: Array = []
		for e in entries:
			cards.append(_classify_entry(e))
		slot_cards = cards

	func _classify_entry(e: Dictionary) -> Dictionary:
		if e.get("ok", false):
			return {
				"kind": &"normal",
				"slot_id": e.get("slot_id", &""),
				"created_at_unix": int(e.get("created_at_unix", 0)),
				"updated_at_unix": int(e.get("updated_at_unix", 0)),
				"display": _normalize_metadata(e.get("slot_id", &""), e.get("metadata", {})),
				"raw_metadata": e.get("metadata", {}),
			}
		# per-slot failure(non-empty slot_id): raw error 보존.
		return {
			"kind": &"failure",
			"slot_id": e.get("slot_id", &""),
			"error": e.get("error", &"unknown"),
		}

	# §12.5 metadata display fallback. {}/unknown keys/wrong types에서 crash 없이 fallback 표시값을 만든다.
	func _normalize_metadata(slot_id, meta) -> Dictionary:
		var raw: Dictionary = meta if meta is Dictionary else {}
		# display_name: String이 아니거나 비어 있으면 slot_id로 fallback.
		var display_name := String(slot_id)
		if raw.get("display_name", null) is String and String(raw["display_name"]) != "":
			display_name = raw["display_name"]
		# play_time_seconds: number가 아니면 "—" fallback.
		var play_time := "—"
		var pt = raw.get("play_time_seconds", null)
		if pt is int or pt is float:
			play_time = _format_playtime(int(pt))
		return {
			"display_name": display_name,
			"play_time": play_time,
			"chapter": _str_or_empty(raw.get("chapter", null)),
			"location": _str_or_empty(raw.get("location", null)),
			"mode": _str_or_empty(raw.get("mode", null)),
		}

	func _str_or_empty(v) -> String:
		return v if v is String else ""

	func _format_playtime(seconds: int) -> String:
		var s := maxi(seconds, 0)
		return "%02d:%02d" % [s / 60, s % 60]

	func select(slot_id) -> void:
		selected_slot_id = slot_id
		can_save_state = flow.can_save(slot_id)

	# §12.2 manual save. 성공/실패 모두 6키 shape를 host state에 보존.
	func do_save(slot_id, metadata := {}) -> Dictionary:
		var r := flow.save_manual(slot_id, metadata)
		last_action = {
			"kind": &"save",
			"ok": r.get("ok", false),
			"slot_id": r.get("slot_id", &""),
			"error": r.get("error", &""),
			"metadata": r.get("metadata", {}),
			"manager_report": r.get("manager_report", {}),
			"gate": r.get("gate", {}),
		}
		if r.get("ok", false):
			refresh_list()
		return r

	# §12.3 manual load. recovered_from_backup/source/restore + raw reason을 .get으로 안전 보존.
	func do_load(slot_id) -> Dictionary:
		var r := flow.load_manual(slot_id)
		last_action = {
			"kind": &"load",
			"ok": r.get("ok", false),
			"slot_id": r.get("slot_id", &""),
			"error": r.get("error", &""),
			"recovered_from_backup": r.get("recovered_from_backup", false),
			"source": r.get("source", &""),
			"restore": r.get("restore", {}),
		}
		return r

	# §12.4 delete. 성공/실패와 무관하게 list refresh.
	func do_delete(slot_id) -> Dictionary:
		var r := flow.delete_slot(slot_id)
		last_action = {
			"kind": &"delete",
			"ok": r.get("ok", false),
			"slot_id": r.get("slot_id", &""),
			"error": r.get("error", &""),
		}
		refresh_list()
		return r

	func find_card(slot_id) -> Dictionary:
		for c in slot_cards:
			if String(c.get("slot_id", &"")) == String(slot_id):
				return c
		return {}


# --- helpers ---------------------------------------------------------

func _new_flow_with_manager(m: SaveGameManager) -> SaveFlow:
	var flow := SaveFlow.new()
	add_child(flow)
	flow.set_manager(m)
	return flow


func _new_manager() -> SpyManager:
	var m := SpyManager.new()
	add_child(m)
	return m


func _make_section(id: StringName, payload: Dictionary = {}) -> FakeSection:
	var s := FakeSection.new()
	s.section_id = id
	s.payload_data = payload
	add_child(s)
	return s


func _host_no_manager() -> FakeSaveSlotHostController:
	var flow := SaveFlow.new()
	add_child(flow)
	flow.manager_path = ^"/root/__sg003_no_such_manager__"
	return FakeSaveSlotHostController.new(flow)


func _slot(name: String) -> String:
	return PREFIX + name


func _slot_file(sid: String) -> String:
	return SaveGameManager.SAVES_DIR + "/" + sid + ".json"


func _write_raw(path: String, text: String) -> void:
	DirAccess.make_dir_recursive_absolute(SaveGameManager.SAVES_DIR)
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_string(text)
	f.close()


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


# --- 시나리오 --------------------------------------------------------

func _test_list_manager_unavailable() -> void:
	print("[A] whole-list manager_unavailable -> list_state + slot count 0")
	var host := _host_no_manager()
	host.refresh_list()
	_check("A.list_state", host.list_state, &"manager_unavailable")
	_check("A.slot_count", host.slot_cards.size(), 0)


func _test_list_per_slot_failure_isolation() -> void:
	print("[B] per-slot parse_error/corrupt 격리 + 정상 slot 비차단")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"v": 1}))
	var host := FakeSaveSlotHostController.new(_new_flow_with_manager(m))
	# 정상 slot 1개.
	var good := _slot("good")
	_check("B.save_good_ok", host.do_save(good, {"display_name": "Good"})["ok"], true)
	# parse_error slot(깨진 JSON).
	var bad_parse := _slot("badparse")
	_write_raw(_slot_file(bad_parse), "{ broken json ")
	# corrupt slot(유효 JSON이지만 Dictionary 아님).
	var bad_corrupt := _slot("badcorrupt")
	_write_raw(_slot_file(bad_corrupt), "[1, 2, 3]")

	host.refresh_list()
	_check("B.list_state", host.list_state, &"ready")
	var gc := host.find_card(good)
	_check("B.good_kind", gc.get("kind", &""), &"normal")
	_check("B.good_display", gc.get("display", {}).get("display_name", ""), "Good")
	var pc := host.find_card(bad_parse)
	_check("B.parse_kind", pc.get("kind", &""), &"failure")
	_check("B.parse_error", pc.get("error", &""), &"parse_error")
	var cc := host.find_card(bad_corrupt)
	_check("B.corrupt_kind", cc.get("kind", &""), &"failure")
	_check("B.corrupt_error", cc.get("error", &""), &"corrupt")


func _test_metadata_fallback() -> void:
	print("[C] metadata fallback: {}/unknown/wrong-type + raw 보존(crash 없음)")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"v": 1}))
	var host := FakeSaveSlotHostController.new(_new_flow_with_manager(m))

	# (1) 빈 metadata -> display_name=slot_id, play_time="—".
	var empty := _slot("meta_empty")
	host.do_save(empty, {})
	# (2) unknown key + 정상 play_time -> display_name fallback, play_time format, unknown 보존.
	var unknown := _slot("meta_unknown")
	host.do_save(unknown, {"weird_key": "hello", "play_time_seconds": 90})
	# (3) wrong display-key types(display_name=int, play_time=String, chapter=int) -> 모두 fallback, raw 보존.
	var wrong := _slot("meta_wrong")
	host.do_save(wrong, {"display_name": 123, "play_time_seconds": "lots", "chapter": 7})

	host.refresh_list()

	var ec := host.find_card(empty)
	_check("C.empty_name_fallback", ec.get("display", {}).get("display_name", ""), empty)
	_check("C.empty_playtime_fallback", ec.get("display", {}).get("play_time", ""), "—")
	_check("C.empty_raw_size", (ec.get("raw_metadata", {}) as Dictionary).size(), 0)

	var uc := host.find_card(unknown)
	_check("C.unknown_name_fallback", uc.get("display", {}).get("display_name", ""), unknown)
	_check("C.unknown_playtime_fmt", uc.get("display", {}).get("play_time", ""), "01:30")
	_check("C.unknown_raw_kept", uc.get("raw_metadata", {}).get("weird_key", ""), "hello")

	var wc := host.find_card(wrong)
	_check("C.wrong_name_fallback", wc.get("display", {}).get("display_name", ""), wrong)
	_check("C.wrong_playtime_fallback", wc.get("display", {}).get("play_time", ""), "—")
	_check("C.wrong_chapter_fallback", wc.get("display", {}).get("chapter", "X"), "")
	# raw metadata는 보존된다(JSON round-trip으로 int는 float로 돌아옴).
	_check("C.wrong_raw_name_kept", int(wc.get("raw_metadata", {}).get("display_name", 0)), 123)
	_check("C.wrong_raw_chapter_kept", int(wc.get("raw_metadata", {}).get("chapter", 0)), 7)


func _test_save_gate_fail_closed() -> void:
	print("[D] save gate deny/unavailable/contract invalid -> fail-closed(save_slot 미호출), 6키 보존")
	# (1) deny.
	var m1 := _new_manager()
	m1.register_section(_make_section(&"world", {"v": 1}))
	var flow1 := _new_flow_with_manager(m1)
	var gate := GateProvider.new()
	gate.allow = false
	gate.reason = &"in_combat"
	flow1.set_save_gate_provider(gate)
	var host1 := FakeSaveSlotHostController.new(flow1)
	host1.select(_slot("deny"))
	_check("D.deny_can_save", host1.can_save_state.get("ok", true), false)
	var rd := host1.do_save(_slot("deny"))
	_check("D.deny_ok", rd["ok"], false)
	_check("D.deny_error", host1.last_action["error"], &"save_not_allowed")
	_check("D.deny_no_save_call", m1.save_calls, 0)
	_check("D.deny_no_file", FileAccess.file_exists(_slot_file(_slot("deny"))), false)
	_check("D.deny_gate_reason", host1.last_action["gate"].get("reason", &""), &"in_combat")
	_check_last_action_save_keys("D.deny", host1)

	# (2) unavailable(메서드 없는 gate).
	var m2 := _new_manager()
	m2.register_section(_make_section(&"world", {"v": 1}))
	var flow2 := _new_flow_with_manager(m2)
	flow2.set_save_gate_provider(NoMethodGate.new())
	var host2 := FakeSaveSlotHostController.new(flow2)
	host2.do_save(_slot("unavail"))
	_check("D.unavail_error", host2.last_action["error"], &"save_gate_unavailable")
	_check("D.unavail_no_save_call", m2.save_calls, 0)
	_check_last_action_save_keys("D.unavail", host2)

	# (3) contract invalid(non-Dictionary 반환).
	var m3 := _new_manager()
	m3.register_section(_make_section(&"world", {"v": 1}))
	var flow3 := _new_flow_with_manager(m3)
	var bad_gate := GateProvider.new()
	bad_gate.return_non_dict = true
	flow3.set_save_gate_provider(bad_gate)
	var host3 := FakeSaveSlotHostController.new(flow3)
	host3.do_save(_slot("contract"))
	_check("D.contract_error", host3.last_action["error"], &"save_gate_contract_invalid")
	_check("D.contract_no_save_call", m3.save_calls, 0)
	_check_last_action_save_keys("D.contract", host3)


func _test_save_report_shape_preserved() -> void:
	print("[E] save_manual 성공/실패 6키 shape를 host state가 보존")
	# 성공.
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"v": 1}))
	var host := FakeSaveSlotHostController.new(_new_flow_with_manager(m))
	host.do_save(_slot("shape_ok"), {"display_name": "OK"})
	_check("E.success_ok", host.last_action["ok"], true)
	_check("E.success_error", host.last_action["error"], &"")
	_check_last_action_save_keys("E.success", host)
	_check_true("E.success_manager_has_path", host.last_action["manager_report"].has("path"))
	# 실패(manager unavailable).
	var host_fail := _host_no_manager()
	host_fail.do_save(_slot("shape_fail"))
	_check("E.fail_ok", host_fail.last_action["ok"], false)
	_check("E.fail_error", host_fail.last_action["error"], &"manager_unavailable")
	_check_last_action_save_keys("E.fail", host_fail)
	_check("E.fail_metadata_empty", host_fail.last_action["metadata"].is_empty(), true)
	_check("E.fail_manager_empty", host_fail.last_action["manager_report"].is_empty(), true)


func _test_load_backup_recovery_preserved() -> void:
	print("[F] load_manual recovered_from_backup/source/restore를 host state가 보존")
	var m := _new_manager()
	var sec := _make_section(&"world", {"v": 1})
	m.register_section(sec)
	var host := FakeSaveSlotHostController.new(_new_flow_with_manager(m))
	var sid := _slot("recover")
	host.do_save(sid)                 # v1
	sec.payload_data = {"v": 2}
	host.do_save(sid)                 # v2, bak=v1
	# primary 손상 -> load는 bak에서 복구.
	_write_raw(_slot_file(sid), "{ broken json ")
	sec.last_restored = null
	var r := host.do_load(sid)
	_check("F.load_ok", r["ok"], true)
	_check("F.recovered", host.last_action["recovered_from_backup"], true)
	_check("F.source", host.last_action["source"], &"backup")
	_check_true("F.restore_preserved", not host.last_action["restore"].is_empty())
	_check("F.restored_v1", int(sec.last_restored["v"]), 1)


func _test_load_failure_reason_preserved() -> void:
	print("[G] load 실패 raw reason(slot_not_found/parse_error)을 host state가 보존")
	var m := _new_manager()
	m.register_section(_make_section(&"world", {"v": 1}))
	var host := FakeSaveSlotHostController.new(_new_flow_with_manager(m))
	# slot_not_found(파일 없음).
	host.do_load(_slot("ghost"))
	_check("G.not_found_ok", host.last_action["ok"], false)
	_check("G.not_found_error", host.last_action["error"], &"slot_not_found")
	# 누락 키는 .get fallback으로 안전(source 기본값 &"").
	_check("G.not_found_source_default", host.last_action["source"], &"")
	# parse_error(깨진 파일, bak 없음).
	var sid := _slot("badload")
	_write_raw(_slot_file(sid), "{ broken json ")
	host.do_load(sid)
	_check("G.parse_error", host.last_action["error"], &"parse_error")
	_check("G.parse_not_recovered", host.last_action["recovered_from_backup"], false)


func _test_delete_then_refresh() -> void:
	print("[H] delete 후 list refresh 흐름")
	var m := _new_manager()
	var sec := _make_section(&"world", {"v": 1})
	m.register_section(sec)
	var host := FakeSaveSlotHostController.new(_new_flow_with_manager(m))
	var sid := _slot("todelete")
	host.do_save(sid)
	sec.payload_data = {"v": 2}
	host.do_save(sid)  # bak 생성
	host.refresh_list()
	_check_true("H.present_before", not host.find_card(sid).is_empty())
	# delete -> primary+bak 제거 + refresh.
	var r := host.do_delete(sid)
	_check("H.delete_ok", r["ok"], true)
	_check("H.last_action_kind", host.last_action["kind"], &"delete")
	_check_true("H.gone_after_refresh", host.find_card(sid).is_empty())
	_check("H.no_file", FileAccess.file_exists(_slot_file(sid)), false)
	# slot_not_found도 refresh가 동작한다.
	var r2 := host.do_delete(sid)
	_check("H.second_delete_ok", r2["ok"], false)
	_check("H.second_delete_error", host.last_action["error"], &"slot_not_found")


# save last_action의 6키 보존 검증.
func _check_last_action_save_keys(prefix: String, host) -> void:
	for key in ["ok", "slot_id", "error", "metadata", "manager_report", "gate"]:
		_check_true("%s.last_action_has_%s" % [prefix, key], host.last_action.has(key))
