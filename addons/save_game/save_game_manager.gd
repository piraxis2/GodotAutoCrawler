class_name SaveGameManager
extends Node
## SaveGame core의 in-memory orchestration + 파일 slot store(SG-001 Step 1~2).
##
## 책임:
## - `SaveSection` 명시적 등록(1순위) + 보조 subtree/group discovery helper.
## - 중복/빈 id/invalid version 검증.
## - deterministic section ordering(restore_order, 그다음 section_id lexical).
## - in-memory capture_all() / validate_envelope() / restore_all(envelope).
## - save/load(=capture/restore) 재진입 busy guard.
## - (Step 2) `user://saves/<slot>.json` slot file save/load/list/delete + atomic write.
##
## 설계 경계(ADR-013): core는 WorldState/DialogueTool 등 domain-specific system을 직접 참조하지
## 않는다. payload 내부는 해석하지 않고 JSON 호환 Dictionary로만 다룬다. backup/recovery(.bak)는
## 후속 Step(SG-001 Step 4) 범위다.
##
## 모든 public API는 보고형 Dictionary를 반환한다(코드베이스 공통 패턴). 파일 slot API는 설계 §4의
## `{ ok, slot_id, error, ... }` 형태를 쓰고, in-memory capture/validate/restore는 `reason` 키를 쓴다.

## SaveGame core envelope version. section_version, payload schema_version과 별개로 manager가 해석한다.
const SAVE_VERSION := 1

## 보조 group discovery에서 사용하는 SceneTree group 이름.
const SAVE_SECTION_GROUP := &"save_section"

## JSON-safe 정수 도메인(±(2^53-1)). 이 범위를 벗어난 int는 JSON parse 왕복에서 precision을 잃으므로
## payload로 거부한다(WorldState snapshot과 동일 규칙).
const JSON_SAFE_INT_MAX := 9007199254740991

## slot 파일 루트. 파일명은 `<slot_id>.json`(임시 write는 `.json.tmp`).
const SAVES_DIR := "user://saves"

## 허용 slot_id 패턴(설계 §7). 파일명 안전 문자만, 1~64자.
const SLOT_ID_PATTERN := "^[a-zA-Z0-9_-]{1,64}$"

## 등록된 section: section_id(StringName) -> SaveSection.
var _sections: Dictionary = {}
## capture/restore 진행 중 재진입 차단.
var _busy: bool = false
## slot_id 검증용 RegEx(지연 컴파일).
var _slot_id_regex: RegEx = null


# --- 등록 / 발견 -------------------------------------------------------

## section을 명시적으로 등록한다(1순위 경로). 검증 실패 시 보고형 실패를 반환한다.
func register_section(section: SaveSection) -> Dictionary:
	if section == null:
		return _err(&"section_null", "section is null")
	if String(section.section_id) == "":
		return _err(&"section_id_empty", "section_id is empty")
	if section.section_version < 1:
		return _err(&"section_version_invalid",
			"section_version must be >= 1 (got %d)" % section.section_version)
	var id: StringName = section.section_id
	if _sections.has(id):
		return _err(&"section_id_duplicate", "duplicate section_id '%s'" % id)
	_sections[id] = section
	return {"ok": true, "section_id": id, "reason": &""}


## section 또는 section_id로 등록을 해제한다.
func unregister_section(section_or_id) -> Dictionary:
	var id: StringName
	if section_or_id is SaveSection:
		id = (section_or_id as SaveSection).section_id
	elif section_or_id is StringName or section_or_id is String:
		id = StringName(section_or_id)
	else:
		return _err(&"invalid_argument", "expected SaveSection or section_id")
	if not _sections.has(id):
		return _err(&"section_not_found", "no section registered for '%s'" % id)
	_sections.erase(id)
	return {"ok": true, "section_id": id, "reason": &""}


## 보조 helper: root 하위 트리(+선택적 group)에서 SaveSection을 찾아 등록한다.
## 명시적 register_section()이 1순위이며, 이 helper는 host가 원할 때만 호출한다(전체 SceneTree
## 자동 검색을 기본 동작으로 삼지 않는다 — ADR-013 / SG-001 Design Direction §2).
func discover_sections(root: Node = self, include_groups: bool = false) -> Dictionary:
	var discovered: Array[StringName] = []
	var errors: Array = []
	var seen: Dictionary = {}  # 같은 인스턴스 중복 방문 방지

	var candidates: Array[SaveSection] = []
	if root != null:
		_collect_subtree(root, candidates, seen)
	if include_groups and is_inside_tree():
		for node in get_tree().get_nodes_in_group(SAVE_SECTION_GROUP):
			if node is SaveSection and not seen.has(node.get_instance_id()):
				seen[node.get_instance_id()] = true
				candidates.append(node)

	for section in candidates:
		var r := register_section(section)
		if r["ok"]:
			discovered.append(r["section_id"])
		else:
			errors.append({"reason": r["reason"], "section_id": section.section_id})

	return {"ok": errors.is_empty(), "discovered": discovered, "errors": errors}


func _collect_subtree(node: Node, out: Array[SaveSection], seen: Dictionary) -> void:
	for child in node.get_children():
		if child is SaveSection and not seen.has(child.get_instance_id()):
			seen[child.get_instance_id()] = true
			out.append(child)
		_collect_subtree(child, out, seen)


## 등록된 section을 deterministic 순서(restore_order, 그다음 section_id lexical)로 반환한다.
func get_ordered_sections() -> Array[SaveSection]:
	var arr: Array[SaveSection] = []
	for id in _sections:
		arr.append(_sections[id])
	arr.sort_custom(_compare_sections)
	return arr


func _compare_sections(a: SaveSection, b: SaveSection) -> bool:
	if a.restore_order != b.restore_order:
		return a.restore_order < b.restore_order
	return String(a.section_id) < String(b.section_id)


func get_section_ids() -> Array[StringName]:
	var ids: Array[StringName] = []
	for s in get_ordered_sections():
		ids.append(s.section_id)
	return ids


func has_section(id: StringName) -> bool:
	return _sections.has(id)


func clear_sections() -> void:
	_sections.clear()


# --- capture ----------------------------------------------------------

## 모든 등록 section을 deterministic 순서로 캡처해 in-memory envelope를 만든다.
## 한 section이라도 capture 실패하면 envelope를 만들지 않고 실패 report를 반환한다.
func capture_all() -> Dictionary:
	if _busy:
		return _err(&"busy", "capture_all() while busy")

	# 등록 후 export 필드가 바뀌어 live section 집합이 빈/중복 id 등으로 깨졌는지 재검증한다.
	var rv := _revalidate_sections()
	if not rv["ok"]:
		return {"ok": false, "reason": &"sections_invalid", "errors": rv["errors"]}

	_busy = true

	var sections := get_ordered_sections()
	var section_blocks: Dictionary = {}
	for section in sections:
		var cap: Dictionary = section.capture_save()
		if not (cap is Dictionary) or not cap.get("ok", false):
			# capture 실패: envelope 미생성.
			_busy = false
			var reason: Variant = cap.get("reason", &"capture_failed") if cap is Dictionary else &"capture_failed"
			return {
				"ok": false,
				"reason": &"capture_failed",
				"section_id": section.section_id,
				"section_reason": reason,
			}
		var payload: Variant = cap.get("payload", {})
		if not (payload is Dictionary):
			_busy = false
			return {
				"ok": false,
				"reason": &"capture_payload_invalid",
				"section_id": section.section_id,
			}
		# payload가 JSON 호환인지 재귀 검증한다(StringName/Vector2/Object/non-String key 등 거부).
		# 호환 보장을 core가 강제해 Step 2 파일 저장에서 손실/변형이 생기지 않게 한다.
		if not _is_json_compatible(payload):
			_busy = false
			return {
				"ok": false,
				"reason": &"payload_not_json_compatible",
				"section_id": section.section_id,
			}
		section_blocks[String(section.section_id)] = {
			"section_version": section.section_version,
			"payload": (payload as Dictionary).duplicate(true),
		}

	var envelope := {
		"save_version": SAVE_VERSION,
		"sections": section_blocks,
	}
	_busy = false
	return {"ok": true, "envelope": envelope, "reason": &""}


# --- validate ---------------------------------------------------------

## envelope를 현재 등록된 section 기준으로 비파괴 검증한다(restore 없음, read-only).
## 반환:
## {
##   ok, reason,
##   errors: [{section_id, reason}],
##   ignored_sections: [StringName],   # envelope에 있으나 등록 안 된 section
##   missing_required: [StringName],
##   skipped_sections: [{section_id, reason}],  # 비-required인데 못 쓰는 항목(version mismatch 등)
##   plan: [{section_id, payload}],     # ok==true일 때 restore_all이 쓸 순서대로의 계획
## }
func validate_envelope(envelope: Dictionary) -> Dictionary:
	var errors: Array = []
	var ignored: Array[StringName] = []
	var missing_required: Array[StringName] = []
	var skipped: Array = []
	var plan: Array = []

	# 0) 등록 후 export 필드가 바뀌어 live section 집합이 깨졌는지 재검증한다.
	var rv := _revalidate_sections()
	if not rv["ok"]:
		return _validate_fail(&"sections_invalid", rv["errors"], ignored, missing_required, skipped)

	# 1) envelope 구조.
	if not (envelope is Dictionary) or not envelope.has("save_version") \
			or not envelope.has("sections"):
		return _validate_fail(&"malformed_envelope", errors, ignored, missing_required, skipped)
	# save_version은 정수형 number여야 한다. JSON 파서는 정수도 float로 읽으므로(`1`→`1.0`) INT/정수형
	# FLOAT를 모두 허용하고, 비정수 float/string/null은 거부한다(in-memory envelope=int, 파일 round-trip=float
	# 모두 통과).
	var sv: Variant = envelope["save_version"]
	if not _is_integral_number(sv):
		return _validate_fail(&"malformed_envelope", errors, ignored, missing_required, skipped)
	if not (envelope["sections"] is Dictionary):
		return _validate_fail(&"malformed_envelope", errors, ignored, missing_required, skipped)

	# 2) save_version (manager-level).
	if int(sv) != SAVE_VERSION:
		return _validate_fail(&"save_version_mismatch", errors, ignored, missing_required, skipped)

	var saved_sections: Dictionary = envelope["sections"]

	# 3) 등록 section을 deterministic 순서로 검증.
	for section in get_ordered_sections():
		var id_str := String(section.section_id)
		if not saved_sections.has(id_str):
			if section.required:
				missing_required.append(section.section_id)
				errors.append({"section_id": section.section_id, "reason": &"required_missing"})
			continue

		var block: Variant = saved_sections[id_str]
		# section_version은 정수형 number여야 한다(save_version과 동일 규칙). JSON round-trip의 정수형
		# float(`1.0`)는 허용하되 비정수 float(`1.5`)/string/null은 malformed_section으로 막아 version
		# contract 검증이 int() 강제 변환으로 우회되지 않게 한다.
		if not (block is Dictionary) or not block.has("section_version") \
				or not block.has("payload") or not (block["payload"] is Dictionary) \
				or not _is_integral_number(block["section_version"]):
			errors.append({"section_id": section.section_id, "reason": &"malformed_section"})
			continue

		# section_version mismatch: required면 실패, 아니면 skip+report(MVP, migration 없음).
		if int(block["section_version"]) != section.section_version:
			if section.required:
				errors.append({"section_id": section.section_id, "reason": &"section_version_mismatch"})
			else:
				skipped.append({"section_id": section.section_id, "reason": &"section_version_mismatch"})
			continue

		var payload: Dictionary = block["payload"]

		# payload가 JSON 호환인지 재귀 검증한다(StringName/Vector2/Object/non-String key 등 거부).
		# section validate_save에 넘기기 전에 core가 호환성을 강제한다.
		if not _is_json_compatible(payload):
			if section.required:
				errors.append({"section_id": section.section_id, "reason": &"payload_not_json_compatible"})
			else:
				skipped.append({"section_id": section.section_id, "reason": &"payload_not_json_compatible"})
			continue

		var v: Dictionary = section.validate_save(payload)
		if not (v is Dictionary) or not v.get("ok", false):
			var reason: Variant = v.get("reason", &"validate_failed") if v is Dictionary else &"validate_failed"
			if section.required:
				errors.append({"section_id": section.section_id, "reason": reason})
			else:
				skipped.append({"section_id": section.section_id, "reason": reason})
			continue

		plan.append({"section_id": section.section_id, "payload": payload.duplicate(true)})

	# 4) unknown saved section -> ignored(실패 아님).
	for saved_id in saved_sections:
		if not _sections.has(StringName(saved_id)):
			ignored.append(StringName(saved_id))

	var ok: bool = errors.is_empty()
	return {
		"ok": ok,
		"reason": &"" if ok else &"validation_failed",
		"errors": errors,
		"ignored_sections": ignored,
		"missing_required": missing_required,
		"skipped_sections": skipped,
		"plan": plan,
	}


# --- restore ----------------------------------------------------------

## envelope를 검증한 뒤 모든 section을 deterministic 순서로 복원한다.
## validate 실패 시 restore를 단 한 번도 시작하지 않는다(restore 0회).
## restore 중간 실패 시 즉시 중단하고 partial_restore report를 남긴다(이미 복원된 section은
## 되돌리지 않는다 — ADR-013 / SG-001 Design Direction §5).
func restore_all(envelope: Dictionary) -> Dictionary:
	if _busy:
		return _err(&"busy", "restore_all() while busy")
	_busy = true

	var validation := validate_envelope(envelope)
	if not validation["ok"]:
		_busy = false
		return {
			"ok": false,
			"reason": &"validation_failed",
			"validation": validation,
			"restored_sections": [],
		}

	var restored: Array[StringName] = []
	for item in validation["plan"]:
		var id: StringName = item["section_id"]
		var section: SaveSection = _sections[id]
		var r: Dictionary = section.restore_save(item["payload"])
		if not (r is Dictionary) or not r.get("ok", false):
			var reason: Variant = r.get("reason", &"restore_failed") if r is Dictionary else &"restore_failed"
			_busy = false
			return {
				"ok": false,
				"reason": &"partial_restore",
				"restored_sections": restored,
				"failed_section": id,
				"failed_reason": reason,
				"ignored_sections": validation["ignored_sections"],
			}
		restored.append(id)

	_busy = false
	return {
		"ok": true,
		"reason": &"",
		"restored_sections": restored,
		"ignored_sections": validation["ignored_sections"],
		"skipped_sections": validation["skipped_sections"],
	}


# --- 파일 slot store (Step 2) -----------------------------------------
## `user://saves/<slot>.json`에 envelope를 저장/로드/list/delete한다. backup(.bak)/recovery는 Step 4.
## slot 파일 envelope는 in-memory envelope(save_version+sections)에 slot 메타(slot_id/created/updated/
## metadata)를 더한 형태다. metadata 자체도 JSON 호환이어야 한다.

## slot에 현재 등록 section을 캡처해 atomic write 한다. capture 실패 시 파일을 쓰지 않는다.
## metadata는 caller가 주는 JSON 호환 Dictionary(display_name/play_time 등)다.
func save_slot(slot_id, metadata: Dictionary = {}) -> Dictionary:
	var id_err := _check_slot_id(slot_id)
	if not id_err.is_empty():
		return {"ok": false, "slot_id": StringName(String(slot_id)), "error": id_err}
	var sid := String(slot_id)

	if not _is_json_compatible(metadata):
		return {"ok": false, "slot_id": StringName(sid), "error": &"metadata_not_json_compatible"}

	# capture(검증·busy guard·JSON 호환 강제 포함). 실패 시 파일 미작성.
	var cap := capture_all()
	if not cap["ok"]:
		return {"ok": false, "slot_id": StringName(sid), "error": &"capture_failed", "capture": cap}

	var envelope: Dictionary = cap["envelope"]
	var now := int(Time.get_unix_time_from_system())
	var created := now
	# 기존 slot이 있으면 created_at_unix를 보존한다(읽기 실패/손상이면 now로 새로 시작).
	if FileAccess.file_exists(_slot_path(sid)):
		var prev := _read_envelope(_slot_path(sid))
		if prev["ok"]:
			var pc: Variant = prev["envelope"].get("created_at_unix", null)
			if typeof(pc) == TYPE_INT or typeof(pc) == TYPE_FLOAT:
				created = int(pc)

	envelope["slot_id"] = sid
	envelope["created_at_unix"] = created
	envelope["updated_at_unix"] = now
	envelope["metadata"] = metadata.duplicate(true)

	var write := _atomic_write_json(sid, envelope)
	if not write["ok"]:
		return {"ok": false, "slot_id": StringName(sid), "error": write["error"]}

	return {
		"ok": true,
		"slot_id": StringName(sid),
		"error": &"",
		"path": _slot_path(sid),
		"sections": envelope["sections"],
	}


## slot 파일을 읽어 envelope로 복원한다. validate-all -> restore-all(restore_all 경유).
## 누락/파싱 실패/손상은 보고형 실패이고 게임 상태를 건드리지 않는다.
##
## 복구 정책(Step 4): primary(`<slot>.json`)가 없거나 손상이면 `<slot>.json.bak`을 시도한다. bak이
## 유효하면 거기서 복원하고 `recovered_from_backup=true`로 보고한다. 둘 다 없으면 `slot_not_found`,
## 둘 다 손상이면 `corrupt`(restore 0회, 게임 상태 불변).
func load_slot(slot_id) -> Dictionary:
	var id_err := _check_slot_id(slot_id)
	if not id_err.is_empty():
		return {"ok": false, "slot_id": StringName(String(slot_id)), "error": id_err}
	var sid := String(slot_id)

	var primary_exists := FileAccess.file_exists(_slot_path(sid))
	var bak_exists := FileAccess.file_exists(_bak_path(sid))
	if not primary_exists and not bak_exists:
		return {"ok": false, "slot_id": StringName(sid), "error": &"slot_not_found"}

	var recovered := false
	var read: Dictionary = {"ok": false, "error": &"read_failed"}
	if primary_exists:
		read = _read_envelope(_slot_path(sid))
	if not read["ok"] and bak_exists:
		# primary 없음/손상 -> .bak 복구 시도.
		var bread := _read_envelope(_bak_path(sid))
		if bread["ok"]:
			read = bread
			recovered = true
		else:
			# bak도 손상: 마지막 실패 원인(parse_error/corrupt)을 보존해 read_failed 기본값을 노출하지 않는다(P2).
			read = bread
	if not read["ok"]:
		# primary 손상 + bak 없음/손상.
		return {
			"ok": false,
			"slot_id": StringName(sid),
			"error": StringName(read["error"]),
			"recovered_from_backup": false,
		}

	var restore := restore_all(read["envelope"])
	var ok: bool = restore["ok"]
	return {
		"ok": ok,
		"slot_id": StringName(sid),
		"error": &"" if ok else StringName(restore.get("reason", &"restore_failed")),
		"recovered_from_backup": recovered,
		"source": &"backup" if recovered else &"primary",
		"restore": restore,
	}


## saves 디렉터리의 slot 메타를 읽어 정렬된 목록을 반환한다. 손상 slot은 ok:false로 보고하되
## 다른 slot 나열을 막지 않는다(per-slot corrupt isolation).
func list_slots() -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	if not DirAccess.dir_exists_absolute(SAVES_DIR):
		return out
	var d := DirAccess.open(SAVES_DIR)
	if d == null:
		return out

	var slot_files: Array[String] = []
	for fname in d.get_files():
		# `.json`만 — `.json.tmp`(끝 `.tmp`)/`.bak`은 제외된다.
		if fname.ends_with(".json"):
			slot_files.append(fname)
	slot_files.sort()  # deterministic

	for fname in slot_files:
		var sid := fname.trim_suffix(".json")
		var read := _read_envelope(SAVES_DIR + "/" + fname)
		if not read["ok"]:
			out.append({"ok": false, "slot_id": StringName(sid), "error": read["error"]})
			continue
		# 파싱은 됐지만 메타 구조가 손상된 slot도 ok:false로 격리한다(캐스팅/필드 접근 위험 방지).
		out.append(_extract_slot_meta(sid, read["envelope"]))
	return out


## list_slots용 메타 추출. save_version=정수형 number, timestamp=number, metadata=Dictionary가 아니면
## ok:false corrupt로 격리한다. 손상 slot이 다른 slot 나열을 막지 않게 한다.
func _extract_slot_meta(sid: String, env: Dictionary) -> Dictionary:
	if not _is_integral_number(env.get("save_version", null)):
		return {"ok": false, "slot_id": StringName(sid), "error": &"corrupt"}
	var created: Variant = env.get("created_at_unix", 0)
	var updated: Variant = env.get("updated_at_unix", 0)
	if not _is_number(created) or not _is_number(updated):
		return {"ok": false, "slot_id": StringName(sid), "error": &"corrupt"}
	var meta: Variant = env.get("metadata", {})
	if not (meta is Dictionary):
		return {"ok": false, "slot_id": StringName(sid), "error": &"corrupt"}
	return {
		"ok": true,
		"slot_id": StringName(sid),
		"save_version": int(env["save_version"]),
		"created_at_unix": int(created),
		"updated_at_unix": int(updated),
		"metadata": (meta as Dictionary).duplicate(true),
	}


## slot 파일을 삭제한다. primary와 `.bak`을 모두 제거한다(백업이 남아 slot이 되살아나지 않게).
func delete_slot(slot_id) -> Dictionary:
	var id_err := _check_slot_id(slot_id)
	if not id_err.is_empty():
		return {"ok": false, "slot_id": StringName(String(slot_id)), "error": id_err}
	var sid := String(slot_id)

	var primary_exists := FileAccess.file_exists(_slot_path(sid))
	var bak_exists := FileAccess.file_exists(_bak_path(sid))
	if not primary_exists and not bak_exists:
		return {"ok": false, "slot_id": StringName(sid), "error": &"slot_not_found"}
	var d := DirAccess.open(SAVES_DIR)
	if d == null:
		return {"ok": false, "slot_id": StringName(sid), "error": &"saves_dir_unavailable"}
	if primary_exists and d.remove(sid + ".json") != OK:
		return {"ok": false, "slot_id": StringName(sid), "error": &"delete_failed"}
	if bak_exists and d.remove(sid + ".json.bak") != OK:
		return {"ok": false, "slot_id": StringName(sid), "error": &"delete_failed"}
	return {"ok": true, "slot_id": StringName(sid), "error": &""}


## slot 파일 존재 여부. 잘못된 slot_id는 false.
func has_slot(slot_id) -> bool:
	if not _check_slot_id(slot_id).is_empty():
		return false
	return FileAccess.file_exists(_slot_path(String(slot_id)))


# --- 파일 helpers -----------------------------------------------------

func _slot_path(sid: String) -> String:
	return SAVES_DIR + "/" + sid + ".json"


func _tmp_path(sid: String) -> String:
	return SAVES_DIR + "/" + sid + ".json.tmp"


func _bak_path(sid: String) -> String:
	return SAVES_DIR + "/" + sid + ".json.bak"


## slot_id가 패턴에 맞으면 빈 StringName, 아니면 `invalid_slot_id`를 반환한다.
func _check_slot_id(slot_id) -> StringName:
	if _slot_id_regex == null:
		_slot_id_regex = RegEx.new()
		_slot_id_regex.compile(SLOT_ID_PATTERN)
	if _slot_id_regex.search(String(slot_id)) == null:
		return &"invalid_slot_id"
	return &""


func _ensure_saves_dir() -> Error:
	if DirAccess.dir_exists_absolute(SAVES_DIR):
		return OK
	return DirAccess.make_dir_recursive_absolute(SAVES_DIR)


## tmp에 쓴 뒤 rename으로 교체한다(Godot DirAccess.rename은 기존 대상이 있으면 제거 후 교체 —
## Windows 포함). partial write가 실제 slot 파일을 덮어쓰지 않게 한다.
##
## 백업 정책(Step 4): 기존 primary가 있으면 교체 전에 `<slot>.json.bak`으로 옮긴다(한 세대 백업).
## 순서는 (1) tmp write → (2) primary→bak rename → (3) tmp→primary rename. (2)와 (3) 사이에서 크래시가
## 나도 bak에 직전 good 상태가 남아 load_slot이 복구할 수 있다. 기존 bak은 교체된다(한 세대만 유지).
## 백업 rename 실패는 안전망 없는 덮어쓰기를 막기 위해 save를 실패시킨다(primary는 그대로 보존).
func _atomic_write_json(sid: String, envelope: Dictionary) -> Dictionary:
	var dir_err := _ensure_saves_dir()
	if dir_err != OK:
		return {"ok": false, "error": &"saves_dir_unavailable"}

	var text := JSON.stringify(envelope, "\t")
	var f := FileAccess.open(_tmp_path(sid), FileAccess.WRITE)
	if f == null:
		return {"ok": false, "error": &"tmp_open_failed"}
	f.store_string(text)
	f.close()

	var d := DirAccess.open(SAVES_DIR)
	if d == null:
		return {"ok": false, "error": &"saves_dir_unavailable"}

	# 기존 primary를 .bak으로 회전(한 세대 백업). 단, primary가 **유효할 때만** 회전한다 — 손상된 primary가
	# 마지막 good `.bak`을 덮어쓰는 데이터 손실을 막는다(리뷰 P1). primary가 손상이면 기존 `.bak`(good일 수
	# 있음)을 보존하고, 아래 `tmp → primary` rename이 손상 primary를 새 내용으로 교체한다.
	if d.file_exists(sid + ".json") and _read_envelope(_slot_path(sid))["ok"]:
		var berr := d.rename(sid + ".json", sid + ".json.bak")
		if berr != OK:
			if d.file_exists(sid + ".json.tmp"):
				d.remove(sid + ".json.tmp")
			return {"ok": false, "error": &"backup_failed"}

	var rerr := d.rename(sid + ".json.tmp", sid + ".json")
	if rerr != OK:
		# tmp 잔여물 정리(가능하면). primary는 직전에 bak으로 옮겨졌으면 bak에 보존돼 있다.
		if d.file_exists(sid + ".json.tmp"):
			d.remove(sid + ".json.tmp")
		return {"ok": false, "error": &"rename_failed"}
	return {"ok": true}


## slot 파일을 읽어 envelope Dictionary를 반환한다. 읽기/파싱/구조 실패는 보고형 실패.
## 손상 slot은 예상 가능한 실패이므로 `JSON.parse_string`(실패 시 엔진 ERROR 로그) 대신 인스턴스
## `JSON.parse()`로 error code를 받아 조용히 처리한다(로그 오염 방지).
func _read_envelope(path: String) -> Dictionary:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return {"ok": false, "error": &"read_failed"}
	var text := f.get_as_text()
	f.close()
	var json := JSON.new()
	if json.parse(text) != OK:
		return {"ok": false, "error": &"parse_error"}
	if not (json.data is Dictionary):
		return {"ok": false, "error": &"corrupt"}
	return {"ok": true, "envelope": json.data}


# --- helpers ----------------------------------------------------------

func is_busy() -> bool:
	return _busy


## 등록된 live section들의 현재 export 필드를 재검증한다(등록 후 변경 대비). freed 인스턴스, 빈 id,
## 등록 key와 갈라진 live id(`section_id_changed`), invalid version을 검출한다. capture_all/
## validate_envelope가 op 전에 호출한다.
##
## `_sections`는 등록 당시 id로 keyed인데 plan/restore는 live id로 `_sections[id]`를 조회한다. 따라서
## 등록 후 id가 (빈/중복뿐 아니라) 다른 고유 값으로 바뀌어도 lookup miss/null 접근이 나므로 거부한다.
## 등록 key는 항상 고유하므로 live 중복은 곧 id 변경의 결과이고, `section_id_changed` 한 검사가 이를 포함한다.
func _revalidate_sections() -> Dictionary:
	var errors: Array = []
	for id_key in _sections:
		var section: SaveSection = _sections[id_key]
		if section == null or not is_instance_valid(section):
			errors.append({"section_id": id_key, "reason": &"section_freed"})
			continue
		var live_id: StringName = section.section_id
		if String(live_id) == "":
			errors.append({"section_id": id_key, "reason": &"section_id_empty"})
			continue
		if StringName(id_key) != live_id:
			errors.append({"section_id": id_key, "reason": &"section_id_changed", "live_id": live_id})
			continue
		if section.section_version < 1:
			errors.append({"section_id": live_id, "reason": &"section_version_invalid"})
	return {"ok": errors.is_empty(), "errors": errors}


## number(INT/FLOAT)인가.
func _is_number(value: Variant) -> bool:
	return typeof(value) == TYPE_INT or typeof(value) == TYPE_FLOAT


## 정수형 number인가. INT 또는 정수값 FLOAT(JSON round-trip의 `1.0`)만 true. 비정수 float(`1.5`)/
## string/null/기타는 false. version 필드는 int() 강제 변환 전에 이 검사로 막는다.
func _is_integral_number(value: Variant) -> bool:
	match typeof(value):
		TYPE_INT:
			return true
		TYPE_FLOAT:
			return value == floor(value)
		_:
			return false


## value가 JSON 호환인지 재귀 검증한다. 허용: null/bool/int(JSON-safe 범위)/float/String,
## Array(원소 호환), Dictionary(String key + 값 호환). StringName/Vector*/Object/Resource/Node와
## non-String Dictionary key는 거부한다(JSON 왕복에서 손실/변형되므로).
func _is_json_compatible(value: Variant) -> bool:
	match typeof(value):
		TYPE_NIL, TYPE_BOOL, TYPE_FLOAT, TYPE_STRING:
			return true
		TYPE_INT:
			return value >= -JSON_SAFE_INT_MAX and value <= JSON_SAFE_INT_MAX
		TYPE_ARRAY:
			for e in value:
				if not _is_json_compatible(e):
					return false
			return true
		TYPE_DICTIONARY:
			for k in value:
				if typeof(k) != TYPE_STRING:
					return false
				if not _is_json_compatible(value[k]):
					return false
			return true
		_:
			return false


func _err(reason: StringName, message: String) -> Dictionary:
	return {"ok": false, "reason": reason, "message": message}


func _validate_fail(reason: StringName, errors: Array, ignored: Array[StringName],
		missing_required: Array[StringName], skipped: Array) -> Dictionary:
	return {
		"ok": false,
		"reason": reason,
		"errors": errors,
		"ignored_sections": ignored,
		"missing_required": missing_required,
		"skipped_sections": skipped,
		"plan": [],
	}
