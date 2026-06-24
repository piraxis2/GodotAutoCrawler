class_name SaveSection
extends Node
## 저장 가능한 최소 단위(SG-001 Step 1).
##
## SaveGame core가 저장/복원하는 도메인 데이터의 base contract다. 게임은 이 노드를 상속해 자기
## gameplay system(예: WorldState, inventory, party)을 저장 대상으로 노출한다.
##
## 설계 경계(ADR-013): SaveGame core(`SaveGameManager`/`SaveSection`)는 어떤 domain-specific
## system도 직접 알지 않는다. WorldState/DialogueTool 같은 시스템 결합은 이 base를 상속한 adapter
## (예: 후속 `WorldStateSaveSection`)가 소유한다. 이 파일에는 그런 결합이 없어야 한다.
##
## 보고형 계약(코드베이스 공통 report 패턴):
## - capture_save() -> { ok: bool, payload: Dictionary, reason: StringName }
##     ok==true일 때만 payload를 envelope에 넣는다. ok==false면 manager는 save 전체를 만들지 않는다.
##     payload는 JSON 호환 Dictionary여야 한다(core는 내부를 해석하지 않는다).
## - validate_save(payload) -> { ok: bool, reason: StringName }
##     restore 전에 payload가 복원 가능한지 비파괴로 점검한다. read만 한다.
## - restore_save(payload) -> { ok: bool, reason: StringName }
##     실제 도메인 상태를 복원한다. 실패 시 manager가 즉시 중단한다.
##
## 기본 구현은 안전한 no-op이며, 상속 노드가 override 한다.

## 저장 식별자. 빈 값/중복 금지(manager가 검증). 등록·envelope key로 쓰인다.
@export var section_id: StringName = &""
## 이 SaveSection adapter contract version. >= 1. payload 내부 schema_version과 별개다(ADR-013).
@export var section_version: int = 1
## 복원 정렬 키. 작은 값 먼저. 같은 값은 section_id lexical order로 tie-break.
@export var restore_order: int = 0
## true면 save에 이 section이 없을 때 load 실패. false면 없어도 무시.
@export var required: bool = true


## 도메인 상태를 캡처한다. ok==true면 payload(JSON 호환)를 반환한다.
func capture_save() -> Dictionary:
	return {"ok": true, "payload": {}, "reason": &""}


## payload가 복원 가능한지 비파괴로 점검한다(read-only). restore 전에 호출된다.
func validate_save(_payload: Dictionary) -> Dictionary:
	return {"ok": true, "reason": &""}


## payload로 실제 도메인 상태를 복원한다.
func restore_save(_payload: Dictionary) -> Dictionary:
	return {"ok": true, "reason": &""}
