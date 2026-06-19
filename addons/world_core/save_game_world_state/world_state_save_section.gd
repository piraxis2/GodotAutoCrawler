class_name WorldStateSaveSection
extends SaveSection
## SaveGame ↔ WorldState 통합 adapter(SG-001 Step 3).
##
## SaveGame core(`SaveSection`)와 WorldState lifecycle(`WorldStateRuntime`)을 양쪽 다 아는 integration
## layer다(ADR-013). SaveGame core는 WorldState를 모르고, WorldStateRuntime은 SaveGame을 모른다 —
## 결합은 이 adapter 한 곳에 격리된다.
##
## 결합 표면:
## - `SaveSection`: class_name 참조(경로 독립).
## - `WorldStateRuntime`: class_name이 없으므로(ADR-007 D2) NodePath/주입으로 해석하고 duck-type 호출만
##   한다(preload/load·class_name 미참조 → parse-safe). 필요한 계약 메서드는 is_store_ready/
##   is_session_ready/capture_world_state/peek_world_state_compatibility/restore_world_state.
##
## JSON int/float 정규화는 이 adapter가 따로 하지 않는다. WorldStateStore.import_snapshot이 wire 값
## (JSON round-trip의 정수형 float, String↔StringName)을 schema 타입으로 복원하므로, restore 경로가
## 자체적으로 round-trip을 견딘다.

## WorldStateRuntime autoload 경로(호스트가 다르게 두면 NodePath로 교체). 테스트는 set_runtime()으로 주입.
@export var world_state_runtime_path: NodePath = ^"/root/WorldStateRuntime"

var _runtime_override: Node = null


func _init() -> void:
	section_id = &"world_state"
	section_version = 1
	# 다른 gameplay section보다 이른 순서로 복원한다(설계 §6).
	restore_order = -100
	required = true


## 테스트/통합 주입. 주입하면 NodePath 해석을 건너뛴다.
func set_runtime(runtime: Node) -> void:
	_runtime_override = runtime


# 계약 메서드를 모두 가진 runtime을 해석한다. 없거나 계약 불충족이면 null(fail-closed, parse-safe).
func _resolve_runtime() -> Node:
	var rt: Node = _runtime_override
	if rt == null and not world_state_runtime_path.is_empty():
		rt = get_node_or_null(world_state_runtime_path)
	if rt == null or not is_instance_valid(rt):
		return null
	if not (rt.has_method("is_store_ready") and rt.has_method("is_session_ready") \
			and rt.has_method("capture_world_state") \
			and rt.has_method("peek_world_state_compatibility") \
			and rt.has_method("restore_world_state")):
		return null
	return rt


## Store ready + session ready를 먼저 확인한다. 준비되지 않았으면 실패 report를 반환하고 빈 payload를
## 만들지 않는다(manager는 section capture 실패 시 save 전체를 쓰지 않는다 — 설계 §6).
## duck-type 경계이므로 반환 shape도 방어한다: ready는 `== true`로만 통과시키고, capture 반환이
## Dictionary가 아니면 `runtime_contract_invalid`로 닫는다(typed 대입 SCRIPT ERROR 방지).
func capture_save() -> Dictionary:
	var rt := _resolve_runtime()
	if rt == null:
		return {"ok": false, "reason": &"runtime_unavailable"}
	if rt.is_store_ready() != true:
		return {"ok": false, "reason": &"store_not_ready"}
	if rt.is_session_ready() != true:
		return {"ok": false, "reason": &"session_not_ready"}
	var snapshot: Variant = rt.capture_world_state()
	if not (snapshot is Dictionary):
		return {"ok": false, "reason": &"runtime_contract_invalid"}
	return {"ok": true, "payload": snapshot, "reason": &""}


## restore 전에 snapshot envelope/schema 호환성을 비파괴로 점검한다(read-only).
func validate_save(payload: Dictionary) -> Dictionary:
	var rt := _resolve_runtime()
	if rt == null:
		return {"ok": false, "reason": &"runtime_unavailable"}
	var compat: Variant = rt.peek_world_state_compatibility(payload)
	if not (compat is Dictionary):
		return {"ok": false, "reason": &"runtime_contract_invalid"}
	var ok: bool = compat.get("ok", false) == true
	return {"ok": ok, "reason": &"" if ok else _to_reason(compat.get("reason", &"incompatible"))}


## WorldStateRuntime의 transactional restore로 복원한다(SAVE import + SESSION default, 실패 시 보존).
func restore_save(payload: Dictionary) -> Dictionary:
	var rt := _resolve_runtime()
	if rt == null:
		return {"ok": false, "reason": &"runtime_unavailable"}
	var report: Variant = rt.restore_world_state(payload)
	if not (report is Dictionary):
		return {"ok": false, "reason": &"runtime_contract_invalid"}
	var ok: bool = report.get("ok", false) == true
	return {
		"ok": ok,
		"reason": &"" if ok else _to_reason(report.get("reason", &"restore_failed")),
		"runtime_report": report,
	}


# 임의 reason 값을 StringName으로 안전하게 변환한다(잘못된 타입이어도 SCRIPT ERROR 없이).
func _to_reason(value: Variant) -> StringName:
	return StringName(str(value))
