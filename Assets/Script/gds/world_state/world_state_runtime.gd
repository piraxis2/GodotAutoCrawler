extends Node
## World State 런타임 lifecycle coordinator (DT-006 Step 3).
##
## autoload 이름: WorldStateRuntime (`/root/WorldStateRuntime`). class_name은 두지 않는다
## (autoload 이름과 같은 class_name은 "hides an autoload singleton" 오류를 내므로, ADR-007 D2).
##
## 책임(ADR-007 D1): 상태 보관(WorldStateStore)과 분리된 세션 orchestration. 새 게임/load/SESSION
## 초기화 순서를 단일 진입점으로 고정한다. Store는 파일/슬롯을 모르고, 이 coordinator는 파일을 쓰지
## 않는다(외부 SaveGame이 capture/restore 결과를 소비).
##
## 두 ready의 구분:
## - is_store_ready(): Store가 유효 schema로 ready인가(부팅 autoload 상태).
## - is_session_ready(): 새 게임/load가 완료돼 gameplay 가능한가.

## 새 게임/load가 성공해 session-ready가 됐을 때 발행. report는 deep copy.
signal world_state_ready(mode: StringName, report: Dictionary)
## 새 게임/load가 실패했을 때 발행(busy/store_missing/malformed/version_mismatch/store_not_ready 등).
signal world_state_failed(mode: StringName, report: Dictionary)

const STORE_AUTOLOAD := "/root/WorldState"

var _store: WorldStateStore = null
var _session_ready: bool = false
var _busy: bool = false


func _ready() -> void:
	# 주입된 Store가 없으면 autoload Store를 1회 해석한다. 부팅 중 Store를 재초기화하지 않는다
	# (Store autoload가 _ready에서 이미 default init 했다 — 중복 초기화 방지, ADR-007).
	if _store == null:
		_store = get_node_or_null(STORE_AUTOLOAD) as WorldStateStore


## 테스트/통합 주입. 주입하면 autoload 조회를 건너뛴다(autoload와 주입 Store가 섞이지 않음).
## lifecycle op 진행 중(`_busy`)에는 교체를 거부한다 — 진행 중 transaction이 다른/없는 Store로
## 뒤바뀌어 import 대상과 ready/report 기준이 불일치하는 것을 막는다(예: restore의 value_changed
## callback에서 호출). 실제로 Store가 바뀌면 새 Store는 아직 새 게임/load를 거치지 않았으므로
## session-ready를 해제한다.
func set_store(store: WorldStateStore) -> void:
	if _busy:
		push_error("WorldStateRuntime: set_store() during a lifecycle op; ignored.")
		return
	if store == _store:
		return
	_store = store
	_session_ready = false


func get_store() -> WorldStateStore:
	return _store


## Store가 유효 schema로 ready인가(세션 완료와 별개).
func is_store_ready() -> bool:
	return _store != null and _store.is_store_ready()


## 새 게임/load가 완료돼 gameplay 가능한가.
func is_session_ready() -> bool:
	return _session_ready


## 새 게임: Store를 유효 schema default로 재초기화한다(SAVE+SESSION 모두 default).
## destructive 의도이므로 성공 시 session-ready, 실패 시 not-ready.
func start_new_game() -> Dictionary:
	var report := {"mode": &"new_game", "ok": false, "store_ready": false}
	if _busy:
		return _fail(report, "busy")
	if _store == null:
		return _fail(report, "store_missing")

	var store := _store  # 트랜잭션 동안 참조 고정(callback의 교체로부터 보호)
	_busy = true
	var init_ok := store.initialize()
	_busy = false

	report["store_ready"] = store.is_store_ready()
	report["ok"] = init_ok and report["store_ready"]
	_session_ready = report["ok"]
	if report["ok"]:
		world_state_ready.emit(&"new_game", report.duplicate(true))
		return report.duplicate(true)
	return _fail(report, "store_not_ready")


## load: transactional restore. mutation 전에 snapshot envelope를 비변경 점검하고, 통과한 경우에만
## default 재초기화 후 SAVE snapshot을 import한다(SESSION은 default로 시작). envelope 실패 시
## 기존 상태/세션을 보존하고 session-ready로 전환하지 않는다(ADR-007 D4a).
func restore_game(world_state_snapshot: Dictionary) -> Dictionary:
	var report := {"mode": &"load", "ok": false, "store_ready": false}
	if _busy:
		return _fail(report, "busy")
	if _store == null:
		return _fail(report, "store_missing")

	var store := _store  # 트랜잭션 동안 참조 고정(import callback의 교체/null로부터 보호)

	# 1) envelope 비변경 점검 — 실패면 기존 상태/세션 보존(reset 없음).
	var compat: Dictionary = store.peek_snapshot_compatibility(world_state_snapshot)
	if not compat["ok"]:
		report["store_ready"] = store.is_store_ready()
		report["preserved"] = true
		return _fail(report, compat["reason"])

	# 2) 통과 시에만 commit: default 재초기화 -> SAVE import.
	_busy = true
	var init_ok := store.initialize()
	var import_report: Dictionary = {}
	if init_ok:
		import_report = store.import_snapshot(world_state_snapshot)
	_busy = false

	report["store_ready"] = store.is_store_ready()
	report["import"] = import_report
	report["ok"] = init_ok and report["store_ready"]
	_session_ready = report["ok"]
	if report["ok"]:
		world_state_ready.emit(&"load", report.duplicate(true))
		return report.duplicate(true)
	return _fail(report, "store_not_ready")


## --- 외부 SaveGame adapter 경계 (DT-006 Step 4) ---
## 외부 저장 계층(file/slot 시스템)은 아래 capture/restore_world_state 쌍만 사용한다. coordinator는
## Store의 public snapshot API(export_snapshot/peek_snapshot_compatibility/import_snapshot)만 호출하고
## Store 내부(_values/_contract)에 접근하지 않는다. 파일 경로·슬롯·직렬화는 외부 책임(별도 SaveGame Task).

## capture: Store의 SAVE snapshot(JSON 호환 Dictionary)을 반환할 뿐 파일을 쓰지 않는다.
func capture_world_state() -> Dictionary:
	if not is_store_ready():
		push_warning("WorldStateRuntime: capture_world_state on not-ready store; returning empty.")
		return {}
	return _store.export_snapshot()


## restore: 외부 저장 계층이 역직렬화한 snapshot으로 복원하는 adapter 진입점.
## Step 3 transactional restore lifecycle(restore_game)을 그대로 경유한다 — envelope 점검 통과 시에만
## default 재초기화 후 SAVE import, 실패 시 기존 상태 보존·미성공 보고. capture_world_state와 짝을 이루는
## SaveGame-facing 이름이다.
func restore_world_state(snapshot: Dictionary) -> Dictionary:
	return restore_game(snapshot)


# 실패 report를 마무리한다(reason 기록 + world_state_failed 발행 + deep copy 반환). _session_ready는
# 여기서 바꾸지 않는다(envelope-fail 보존 경로가 기존 세션을 유지하도록).
func _fail(report: Dictionary, reason: String) -> Dictionary:
	report["reason"] = reason
	world_state_failed.emit(report["mode"], report.duplicate(true))
	return report.duplicate(true)
