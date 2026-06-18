class_name DialogueDebugPreviewProvider extends RefCounted
## DialogueTool 에디터 Play/Debug 서브프로세스 전용 preview WorldStateStore 구성 helper
## (DT-010 Step 1, ADR-012 D1/D2/D5/D6).
##
## 에디터 Play로 띄운 별도 Godot 프로세스에서 DialoguePlayer가 WorldStateCondition / state_set /
## state_add를 provider_missing 없이 실행할 수 있도록, addon 동봉 example schema로 preview 전용
## WorldStateStore를 구성한다. store facade가 read(has_state/read_state/try_read_state)와
## mutation(apply_state_batch/add_state) 계약을 모두 만족하므로, 인스턴스 하나를 read·mutation
## provider 양쪽으로 주입한다(ADR-012 D1).
##
## parse-safety(ADR-012 D2): /root/WorldState autoload를 bare 전역 식별자로 참조하지 않는다. store는
## class_name `WorldStateStore`(addon 내부 포함이라 항상 parse 가능)로 생성하고, schema는 string path를
## `load()`한다. 따라서 addon만 복사한 fresh 프로젝트(WorldState autoload 미등록)에서도 이 스크립트가
## parse·boot된다.
##
## 격리/lifecycle(ADR-012 D1/D4): 실제 게임 /root/WorldState save state를 건드리지 않는 별도 인스턴스다.
## Play마다 새 프로세스가 뜨므로 매번 example schema default에서 시작한다(결정론적). 반환 store는 Node로
## tree에 추가하지 않으며(read/mutation은 순수 메서드 호출이라 tree 불필요), 프로세스 teardown까지 살아
## 있는 preview 1회용 상태다(기존 헤드리스 테스트의 store 보유 패턴과 동일).

## addon 동봉 example schema(6-key, actor.example.affinity INT 포함). 호스트 게임 schema가 아니라
## preview 전용 고정 schema다(ADR-011 D5 / ADR-012 D1 한계).
const SCHEMA_PATH := "res://addons/dialogtool/examples/world_state_schema_example.tres"


## example schema로 preview 전용 WorldStateStore를 구성해 ready 상태로 반환한다.
## schema_path는 기본적으로 동봉 example을 가리키며, 테스트가 실패 경로를 검증할 때만 다른 경로를 넘긴다.
##
## 실패 정책(ADR-012 D6, fail-closed): schema load 실패 / StateSchema 아님 / initialize 실패 시
## 구체 사유를 push_error로 남기고 null을 반환한다. 호출자(DialoguePlayer)는 null이면 provider를
## 주입하지 않아 기존 fail-closed(condition false / mutation provider_missing)를 유지한다. 자동 true나
## 자동 mutation 성공 처리는 없다.
static func make_preview_store(schema_path: String = SCHEMA_PATH) -> WorldStateStore:
	var schema: Variant = load(schema_path)
	if schema == null:
		push_error("DialogueDebugPreviewProvider: failed to load example schema at '%s'; preview provider not created." % schema_path)
		return null
	if not (schema is StateSchema):
		push_error("DialogueDebugPreviewProvider: resource at '%s' is not a StateSchema; preview provider not created." % schema_path)
		return null

	var store := WorldStateStore.new()
	store.schema = schema
	if not store.initialize():
		# initialize()가 이미 구체 사유(schema invalid / version / default domain)를 push_error로 남겼다.
		push_error("DialogueDebugPreviewProvider: preview WorldStateStore failed to initialize from '%s'; preview provider not created." % schema_path)
		store.free()
		return null

	return store
