class_name SaveFlow
extends Node
## SaveGame core 위의 얇은 호출 계층(facade) — SG-002 Step 1.
##
## 목적(ADR-014):
## - 게임별 UI/메뉴/디버그 도구/이벤트 레이어가 `SaveGameManager`의 envelope/backup 내부 정책을 직접
##   알지 않아도 의도 중심 API(`save_manual`/`load_manual`/...)로 호출할 수 있게 한다.
## - slot metadata는 host가 주입한 provider(base)와 caller가 넘긴 metadata(override)로 만든다.
## - 저장 가능 여부(컷신/전투/로딩 중 금지 등)는 UI와 실제 save 호출이 같은 `can_save()` 결과를
##   공유하도록 optional save gate provider로 분리한다.
##
## 설계 경계:
## - `SaveFlow`는 `SaveGameManager`를 소유하지 않는다. 호출마다 lazy resolve하고 매번
##   `is_instance_valid`/`is SaveGameManager`를 재확인해 freed manager나 autoload 재생성에 안전하다.
## - manager report는 숨기지 않고 passthrough한다(facade는 편의층이지 오류 의미를 재해석하지 않는다).
## - SaveGame core와 마찬가지로 WorldState/DialogueTool 등 domain-specific system을 직접 참조하지 않는다
##   (정적 가드 테스트로 보존). gate/metadata provider는 `Object` duck-type 계약이다.
##
## 모든 public API는 보고형 Dictionary(또는 Dictionary 배열)를 반환한다(코드베이스 공통 패턴).

## 기본 manager 위치. host가 `SaveGameManager`를 autoload(권장 이름 `SaveGame`)로 등록하면 여기로 해석된다.
## 테스트/수동 구성에서는 `set_manager(manager)`로 주입해 우선 사용한다.
@export var manager_path: NodePath = ^"/root/SaveGame"

## 명시적으로 주입된 manager(1순위). 없으면 `manager_path`로 해석한다.
var _manager: SaveGameManager = null
## optional metadata provider. duck-type `make_save_metadata(slot_id) -> Dictionary`.
## Variant 경계로 둔다 — non-Object 입력도 타입 오류 없이 unavailable로 fail-closed되게 한다.
var _metadata_provider = null
## optional save gate provider. duck-type `query_save_gate(slot_id) -> Dictionary { ok, reason }`.
## Variant 경계로 둔다(metadata provider와 동일 이유).
var _save_gate_provider = null


# --- 주입 ------------------------------------------------------------

## manager를 명시적으로 주입한다(1순위). 주입된 manager는 `manager_path`보다 우선하되, 호출 시점에
## freed/invalid면 무시되고 `manager_path` 해석으로 폴백한다(stale reference 비신뢰).
func set_manager(manager: SaveGameManager) -> void:
	_manager = manager


## metadata provider를 주입한다. null이면 base metadata는 `{}`. provider는 `Object` duck-type을
## 기대하지만 인자는 Variant로 받아 non-Object 입력도 호출 시점에 unavailable로 fail-closed한다.
func set_metadata_provider(provider) -> void:
	_metadata_provider = provider


## save gate provider를 주입한다. null이면 `can_save()`는 항상 allow. (metadata provider와 동일하게
## non-Object 입력도 unavailable로 fail-closed되도록 Variant 경계로 받는다.)
func set_save_gate_provider(provider) -> void:
	_save_gate_provider = provider


# --- save gate -------------------------------------------------------

## 저장 가능 여부를 질의한다. manager 가용성은 보지 않고 gate provider만 본다(따라서 ok:true여도
## `save_manual()`은 manager 해석 실패로 `manager_unavailable`이 될 수 있다).
##
## - provider 없음: `{ ok:true, reason:&"" }`.
## - provider freed/non-Object/메서드 없음: fail-closed `{ ok:false, reason:&"save_gate_unavailable" }`.
## - 반환이 Dictionary가 아니거나 `ok`가 bool이 아님: fail-closed
##   `{ ok:false, reason:&"save_gate_contract_invalid" }`.
## - provider가 `{ ok:false, reason }`을 반환하면 reason을 보존해 deny로 정규화한다.
func can_save(slot_id = &"") -> Dictionary:
	if _save_gate_provider == null:
		return {"ok": true, "reason": &""}
	if not _provider_usable(_save_gate_provider, "query_save_gate"):
		return {"ok": false, "reason": &"save_gate_unavailable"}
	var result = _save_gate_provider.query_save_gate(slot_id)
	if not (result is Dictionary) or typeof(result.get("ok")) != TYPE_BOOL:
		return {"ok": false, "reason": &"save_gate_contract_invalid"}
	return {"ok": result["ok"], "reason": result.get("reason", &"")}


# --- save / load / delete / list / has -------------------------------

## 수동 저장. 흐름: manager 해석 -> save gate -> metadata 빌드 -> manager.save_slot -> report 래핑.
## 성공/실패 모두 `ok, slot_id, error, metadata, manager_report, gate` 6키를 유지한다(호출되지 않은
## 단계는 `{}`). manager report는 숨기지 않고 `manager_report`에 원본을 보존한다.
func save_manual(slot_id, metadata: Dictionary = {}) -> Dictionary:
	var manager := _resolve_manager()
	if manager == null:
		return _save_report(false, slot_id, &"manager_unavailable", {}, {}, {})

	# 저장 정책 게이트. ok:false면 manager.save_slot을 호출하지 않는다.
	var gate := can_save(slot_id)
	if not gate["ok"]:
		var greason = gate.get("reason", &"")
		var gate_error: StringName = &"save_not_allowed"
		# 정책상 금지(save_not_allowed)와 gate 설치/계약 오류를 UI가 구분하도록 분기한다.
		if greason == &"save_gate_unavailable" or greason == &"save_gate_contract_invalid":
			gate_error = greason
		return _save_report(false, slot_id, gate_error, {}, {}, gate)

	# metadata: provider base + caller override. provider 오류면 fail-closed(save_slot 미호출).
	var built := _build_metadata(slot_id, metadata)
	if not built["ok"]:
		return _save_report(false, slot_id, built["error"], {}, {}, gate)
	var final_metadata: Dictionary = built["metadata"]

	# 최종 metadata JSON 호환 검증은 manager.save_slot()이 담당한다(non-JSON이면 manager의
	# metadata_not_json_compatible report가 그대로 passthrough된다).
	var manager_report := manager.save_slot(slot_id, final_metadata)
	var ok: bool = manager_report.get("ok", false)
	return _save_report(
		ok,
		slot_id,
		manager_report.get("error", &"" if ok else &"save_failed"),
		final_metadata,
		manager_report,
		gate)


## 수동 로드. gate를 확인하지 않는다. manager.load_slot report를 그대로 감싸 `recovered_from_backup`,
## `source`, `restore` 등 정보를 손실 없이 전달한다.
func load_manual(slot_id) -> Dictionary:
	var manager := _resolve_manager()
	if manager == null:
		return {"ok": false, "error": &"manager_unavailable"}
	return manager.load_slot(slot_id)


## slot 삭제. manager.delete_slot report를 그대로 감싼다.
func delete_slot(slot_id) -> Dictionary:
	var manager := _resolve_manager()
	if manager == null:
		return {"ok": false, "error": &"manager_unavailable"}
	return manager.delete_slot(slot_id)


## slot 목록. manager.list_slots()를 display formatting 없이 그대로 반환한다. manager 미해석 시
## 빈 배열 대신 단일 실패 entry를 반환해 UI가 설치/초기화 문제를 알 수 있게 한다(per-slot corrupt
## entry와 shape를 맞추기 위해 `slot_id:&""` 포함).
func list_slots() -> Array[Dictionary]:
	var manager := _resolve_manager()
	if manager == null:
		var fail: Array[Dictionary] = [{"ok": false, "slot_id": &"", "error": &"manager_unavailable"}]
		return fail
	return manager.list_slots()


## slot 존재 여부. manager.has_slot 위임. manager 미해석 시 false.
func has_slot(slot_id) -> bool:
	var manager := _resolve_manager()
	if manager == null:
		return false
	return manager.has_slot(slot_id)


# --- 내부 helpers ----------------------------------------------------

## manager를 호출마다 lazy resolve한다. 1순위 주입 manager(valid일 때만), 그다음 `manager_path`.
## 매번 `is_instance_valid`/`is SaveGameManager`를 확인해 freed/wrong type/미설치면 null을 반환한다.
func _resolve_manager() -> SaveGameManager:
	if _manager != null and is_instance_valid(_manager) and _manager is SaveGameManager:
		return _manager
	if is_inside_tree():
		var node := get_node_or_null(manager_path)
		if node != null and is_instance_valid(node) and node is SaveGameManager:
			return node as SaveGameManager
	return null


## provider가 호출 가능한가. 검사 순서가 중요하다: null → non-Object → freed → method 존재.
## `is_instance_valid()`는 Object를 기대하므로 non-Object를 먼저 걸러 타입 오류를 막는다(non-Object
## 입력도 SCRIPT ERROR 없이 false → 호출자가 unavailable로 fail-closed).
func _provider_usable(provider, method_name: String) -> bool:
	if provider == null:
		return false
	if typeof(provider) != TYPE_OBJECT:
		return false
	if not is_instance_valid(provider):
		return false
	return provider.has_method(method_name)


## metadata = provider base + caller override(shallow merge, provider base 먼저 -> caller 우선).
## 반환: `{ ok:true, metadata }` 또는 `{ ok:false, error }`.
## provider 없음이면 base는 `{}`. provider freed/non-Object/메서드 없음은 fail-closed
## `metadata_provider_unavailable`, 반환이 Dictionary가 아니면 `metadata_provider_contract_invalid`.
func _build_metadata(slot_id, caller_metadata: Dictionary) -> Dictionary:
	var base: Dictionary = {}
	if _metadata_provider != null:
		if not _provider_usable(_metadata_provider, "make_save_metadata"):
			return {"ok": false, "error": &"metadata_provider_unavailable"}
		var result = _metadata_provider.make_save_metadata(slot_id)
		if not (result is Dictionary):
			return {"ok": false, "error": &"metadata_provider_contract_invalid"}
		base = result

	var final_metadata: Dictionary = {}
	for k in base:
		final_metadata[k] = base[k]
	for k in caller_metadata:
		final_metadata[k] = caller_metadata[k]
	return {"ok": true, "metadata": final_metadata}


## save_manual report를 균일한 6키 shape로 만든다.
func _save_report(ok: bool, slot_id, error, metadata: Dictionary,
		manager_report: Dictionary, gate: Dictionary) -> Dictionary:
	return {
		"ok": ok,
		"slot_id": slot_id,
		"error": error,
		"metadata": metadata,
		"manager_report": manager_report,
		"gate": gate,
	}
