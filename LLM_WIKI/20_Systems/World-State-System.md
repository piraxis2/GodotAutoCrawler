---
type: system
system: WorldState
status: complete
updated: 2026-06-14
---

# World State System

## Agent Brief

- 상태: DT-005(스토어) + DT-006(런타임 통합) 구현·리뷰 완료
- Task: [[DT-005-StateSchema-WorldStateStore]], [[DT-006-WorldState-Runtime-Integration]]
- Decision: [[ADR-006-Typed-World-State]], [[ADR-007-WorldState-Runtime-Lifecycle]]
- Review: [[DT-005-WorldState-Review]], [[DT-006-WorldState-Runtime-Review]]
- 사용법: [[World-State-User-Guide]]
- 책임: 등록된 게임 상태의 타입 안전한 조회, 변경, 초기화, snapshot
- 비책임: 실제 save file, Dialogue-local 변수, ConversationContext, 조건 UI
- 장기 목표: 수백~수천 상태가 선택지, 응답, Effect, 반복 대화를 결정하는 공통 기반

## Implemented (현재 사실)

DT-005 Step 1~6과 DT-006 Step 0~5가 구현·리뷰됐다. schema 선언/검증/lookup, runtime read/write
Store, SAVE/SESSION lifetime + snapshot, atomic mutation batch, Dialogue read provider seam, 실제 autoload
부팅과 new/load lifecycle 통합 회귀가 존재한다.

- `WorldState` autoload(`/root/WorldState`, class `WorldStateStore`)는 유효한 bootstrap Schema로 부팅 시
  store-ready다.
- `WorldStateRuntime` autoload(`/root/WorldStateRuntime`)는 new game/load/SESSION lifecycle을 조정한다.
  snapshot 복원은 envelope를 먼저 비변경 검사하고, 호환될 때만 default 초기화와 SAVE import를 수행한다.
- 외부 SaveGame 계층용 adapter는 `capture_world_state()`/`restore_world_state(snapshot)`이다. coordinator와
  Store 모두 파일 경로와 slot을 모른다.
- 조건 데이터 모델/검증/pure-read 평가기와 실제 `WorldStateStore` 통합(DT-007 Step 1~3)은 존재한다
  (아래 Condition Model). State Read/Set Dialogue 노드, 실제 SaveGame file/slot 시스템은 아직 없다.

- `Assets/Script/gds/world_state/state_definition.gd` — `StateDefinition` Resource.
  - 필드: `key: StringName`, `value_type`, `default_value: Variant`, `lifetime`,
    `writable: bool`, `description`, `tags: Array[StringName]`.
  - enum: `StateValueType { BOOL, INT, FLOAT, STRING, STRING_NAME }`,
    `StateLifetime { SAVE, SESSION }` (Godot에 전역 enum이 없어 클래스 내부에 둔다).
  - static helper: `builtin_type_for(vt)`, `is_known_value_type(vt)`, `is_known_lifetime(lt)`.
- `Assets/Script/gds/world_state/state_schema.gd` — `StateSchema` Resource.
  - 필드: `schema_version: int`, `definitions: Array[StateDefinition]`.
  - `validate() -> { valid, errors[{code,index,key,message}], error_codes[], key_count }`.
  - lookup API: `is_valid()`, `has_key(key)`, `get_definition(key)`, `keys()`, `last_result()`.
  - 검증을 모두 통과한 경우에만 lookup을 채운다. 오류가 하나라도 있으면 lookup은 비어 있다.
  - 무효화 3경로(오래된 lookup 비신뢰): (1) Definition 필드 setter가 Resource `changed`를
    발행하고 `StateSchema`가 구독 → 필드 deep mutation 감지. (2) `definitions`/`schema_version`
    setter → 재할당/버전 변경 감지. (3) `definitions.size()`+`hash()` 구조 지문을 접근 시 비교 →
    setter를 우회하는 in-place 배열 변경(append/erase/remove_at/인덱스 대입) 감지. 어느 경우든
    다음 접근에서 재검증된다.
  - `validate()`/`last_result()`는 deep copy를 반환해 호출자가 결과를 변조해도 내부 상태가 안전하다.
- key 문법: `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$`, 최소 두 segment.
- 허용 타입: bool/int/float/String/StringName. default 타입은 `typeof()` 정확 일치로 검사하며
  암시적 변환(int->float, String<->StringName, null)을 거부한다.
- 검출 오류 code: `schema_version_invalid`, `definition_null`, `value_type_invalid`,
  `lifetime_invalid`, `key_empty`, `key_invalid_format`, `key_duplicate`, `default_type_mismatch`.
- 검증: `Assets/Script/gds/world_state/tests/dt005_step1_schema_test.tscn` 헤드리스 테스트가
  validation 행렬과 `.tres` 저장/재로드 왕복을 확인한다(ALL PASS).
- `Assets/Script/gds/world_state/world_state_store.gd` — `WorldStateStore` (Node).
  - `@export var schema: StateSchema`, `initialize() -> bool`, `is_store_ready() -> bool`.
    유효 schema일 때만 ready가 되고 default로 runtime 값을 채운다.
  - 계약 compile: `initialize()`가 타입/default/writable을 private `_contract` map으로 스냅샷하고,
    이후 read/write/reset은 mutable schema를 다시 조회하지 않는다. 초기화 후 schema를 바꿔도
    Store는 기존 계약을 유지하며, 변경 반영은 `initialize()` 재호출로만 이뤄진다.
  - read: `has_key`, `get_value`(미등록/not-ready면 오류 후 null), `try_get_value(key, fallback)`.
  - write: `set_value(key, value) -> Error` — strict type validation(암시적 변환 금지) + JSON-safe
    도메인(INT ±(2^53-1), FLOAT finite) 강제. `ERR_UNAVAILABLE`/`ERR_BUSY`(알림 중)/
    `ERR_DOES_NOT_EXIST`/`ERR_UNAUTHORIZED`(read-only gameplay)/`ERR_INVALID_DATA`(타입/도메인 위반).
    실패 시 값/시그널 불변.
  - ready 조건: 유효 schema + `schema_version`이 JSON-safe 범위 + 모든 default가 도메인 안.
    위반 시 not-ready → ready Store는 항상 무손실 JSON export 가능.
  - 알림 transaction: value_changed 발행 중 mutation(set/reset/reset_lifetime/import/initialize/
    apply_batch)은 모두 거부(`ERR_BUSY`/`store_busy`/initialize `false`)되어 stale event와 batch
    손상을 막는다.
  - reset: `reset_value(key) -> Error` — default 복원, 시스템 작업이라 read-only key에도 허용.
  - lifetime: `reset_lifetime(lifetime)` — 해당 lifetime을 default로 복원 + `state_reset` 1회.
  - snapshot: `export_snapshot(lifetime=SAVE) -> {schema_version, values}`(JSON 호환, StringName→String,
    SESSION 제외), `import_snapshot(snapshot) -> {applied, ignored, errors}`(SAVE replace-load).
    구조/version 오류는 commit 전 전체 거부, unknown/SESSION/type-mismatch는 개별 무시 후 report,
    read-only key에도 적용. 모든 값을 먼저 반영한 뒤 결정된 순서로 value_changed를 발행한다
    (부분 상태 미노출). version/INT 복원은 finite·정확 정수이며 JSON 안전 범위 `±(2^53-1)` 안인
    값만 허용(손실 입력·범위 초과 거부 — Godot JSON이 숫자를 double로 파싱). report는
    signal/반환에 각각 deep copy를 쓰고 not-ready 포함 모든 경로가 `snapshot_imported`를 발행한다.
  - batch: `apply_batch(changes: Array[Dictionary]) -> {applied, diff:[{key,old,new}], errors:[{index,key,reason}]}`.
    change는 `{key, value}`(key는 StringName/String, 그 외 타입은 malformed). 모든 변경을 먼저 검증하고
    하나라도 실패(타입/도메인/read-only/미등록/중복 key/형식)하면 전체 거부(부분 적용 없음). 중복 검사는
    다른 오류와 독립적으로 앞당겨 수행한다. 성공 시 모든 값 반영 후 **입력 순서**로 value_changed
    발행, diff는 실제 변경만 기록(같은 값은 적용되되 제외).
  - `signal value_changed(key, old, new)` — 값이 실제로 바뀔 때만 발행(같은 값은 무발행).
  - provider 계약 facade(DT-005 Step 5): read(`has_state`/`read_state`/`try_read_state`)와
    mutation(`set_state`/`apply_state_batch`)을 분리해 Store의 좁은 view로 노출(native API에 위임).
  - `world_state_store.tscn`: 외부 `world_state_schema.tres`(유효 6-key bootstrap Schema)를 `schema`로
    참조한다. DT-006 Step 2에서 `WorldState` autoload(`/root/WorldState`)로 등록돼 부팅 시
    `is_store_ready()==true`다. autoload 이름은 class_name `WorldStateStore`와 충돌을 피하려 `WorldState`다.
- 검증: `tests/dt005_step2_store_test.tscn`, `dt005_step3_snapshot_test.tscn`,
  `dt005_step4_batch_test.tscn` 헤드리스(ALL PASS).

## Runtime Lifecycle (DT-006 완료)

- `Assets/Script/gds/world_state/world_state_schema.tres` — schema_version 1의 유효한 6-key bootstrap Schema.
  다섯 value type, SAVE/SESSION lifetime, writable/read-only 계약을 통합 검증하기 위한 최소 집합이며
  제품 quest/actor 계약이 확정되면 확장한다.
- `project.godot` autoload 순서:
  1. `WorldState="*res://Assets/Script/gds/world_state/world_state_store.tscn"`
  2. `WorldStateRuntime="*res://Assets/Script/gds/world_state/world_state_runtime.gd"`
- `Assets/Script/gds/world_state/world_state_runtime.gd` — lifecycle coordinator.
  - `_ready()`에서 이미 등록된 `/root/WorldState`를 한 번 해석한다. 테스트에서는 `set_store()`로
    주입할 수 있으며 주입 Store와 autoload를 섞지 않는다.
  - `is_store_ready()`는 Schema/Store 부팅 준비, `is_session_ready()`는 새 게임 또는 load 완료를 뜻한다.
    Store가 ready여도 session-ready가 아니면 gameplay 시작 조건을 충족하지 않는다.
  - `start_new_game()`은 SAVE와 SESSION을 모두 default로 재초기화하고 성공 시 session-ready로 전환한다.
  - `restore_game()`/`restore_world_state()`는 snapshot envelope를 먼저 비변경 검사한다. malformed/version
    mismatch면 initialize하지 않아 기존 상태와 기존 session-ready를 보존한다. 호환 snapshot만
    initialize(default) -> SAVE import 순서로 적용하므로 SESSION은 default로 시작한다.
  - lifecycle transaction 중 Store 교체와 재진입을 거부한다. 실제 Store 참조는 transaction 동안 고정된다.
  - 성공은 `world_state_ready(mode, report)`, 실패는 `world_state_failed(mode, report)`로 알리며 반환값과
    signal report는 서로 독립된 deep copy다.
  - `capture_world_state()`는 SAVE-only JSON 호환 Dictionary를 반환한다. `restore_world_state()`는
    transactional restore의 외부 SaveGame-facing 별칭이다.
- SESSION은 새 게임과 SAVE load에서만 default로 시작한다. scene 교체나 Dialogue 종료에서는 reset하지 않는다.
- 검증: `tests/dt006_step1~5_*`와 DT-005/DT-004 전체 16개 headless 테스트 및 editor import가 통과했다.

## Dialogue 통합 경계 (Step 5 완료)

- `DialoguePlayer`는 read 상태 provider를 주입받는다(`set_read_state_provider`). `/root`/PlayerData/
  save를 직접 조회하지 않고, `has_state`/`read_state`/`try_read_state`로만 상태를 읽는다(미지정 시
  false/null/fallback). mutation provider는 아직 주입하지 않는다(소비 노드 없음).
- 전달 경로: `DialogueManager.play(resource, provider)` → `DialogueUI.play(resource, provider)` →
  `DialoguePlayer.set_read_state_provider(provider)`. WorldStateStore를 그대로 주입할 수 있다.
- 기존 Variable/Expression/Branch 데이터 평가는 provider와 독립적으로 유지된다.
- 수명주기: UI.play는 resource+provider를 한 쌍으로 묶어 deferred 시작하고 같은 프레임 연속 호출은
  마지막만 시작한다(latest-wins). Manager가 UI를 교체하면 폐기 UI의 대기 시작을 취소해 폐기된
  대화가 시작/평가되지 않는다.
- 검증: `tests/dt005_step5_provider_seam_test.tscn`(ALL PASS) + 기존 DialogueTool dt004 회귀.

## Condition Model (DT-007 Step 1~4 완료, 완료 판정 대기 — [[DT-007-Condition-Review]])

- `Assets/Script/gds/world_state/condition/`에 조건 데이터 모델과 구조 검증기가 있다
  ([[DT-007-ConditionSet-ConditionEvaluator]] Step 1, [[ADR-008-Structured-Condition-Evaluation]]).
  - `ConditionClause`(@abstract base), `StateCondition`(leaf: key/operator/expected_value),
    `ConditionGroup`(ALL/ANY/NOT + recursive `Array[ConditionClause]`), `ConditionSet`(top-level asset).
    모두 순수 데이터 Resource이며 평가/provider/UI를 모른다.
  - `ConditionValidator.validate(condition_set) -> {valid, errors[{code,path,key,message}], error_codes,
    node_count}`: stateless static, iterative(explicit-stack) DFS. null/unknown/empty/NOT arity/
    cycle/alias/depth(64)/node(4096)/key 형식/operator/expected 타입/ordered 숫자 제약을 검사한다.
    provider를 읽지 않으며(2단계 평가의 1단계, structural reject 시 read 0), 결과는 호출별 deep copy다.
  - condition key 형식은 DT-005 `StateSchema.KEY_PATTERN`을 재사용한다(단일 source of truth).
  - `ConditionEvaluator.evaluate(condition_set, read_provider) -> {passed, valid, errors, trace,
    read_count}`(DT-007 Step 2): pure-read 평가기. 2단계 평가 — Validator(read 0)를 먼저 통과해야
    주입 provider의 `has_state`/`read_state`만으로 트리를 재귀 평가한다. strict typeof 비교(암시적 변환
    없음), evaluation-local key cache(miss 포함 key당 1회 read), non-short-circuit 전체 trace,
    fail-closed errored 전파(provider/state/type 오류는 errors에 적재되어 valid·passed=false; NOT/ANY가
    errored child를 pass로 안 바꿈). mutation/signal/save/UI/autoload를 모른다. report/trace는 deep copy.
    runtime 오류 코드: `provider_missing`/`provider_contract_invalid`/`state_missing`/`actual_type_mismatch`.
  - DT-007 Step 3: 실제 `WorldStateStore`를 read provider로 그대로 주입해 통합 검증했다(제품 코드 변경
    없음). set_value/apply_batch/reset_value/reset_lifetime(SESSION)/import_snapshot 뒤 재평가가 새 값을
    반영하고, read-only/SESSION key도 평가에서 정상 read되며, evaluate는 Store를 변경하지 않는다(pure read).
  - 검증: `condition/tests` `dt007_step1`(24)/`step2`(23)/`step3`(11 실제 Store)/`step4`(end-to-end:
    `.tres` 왕복 trace parity·lifecycle·snapshot·성능 sanity)/spike ALL PASS. DT-004/005/006 회귀 +
    editor import 통과. 후속 Dialogue node 입력 계약은 [[DT-007-Condition-Review]]에 문서화.

## Planned Components (미구현)

- State Condition / Read·Set Dialogue 노드와 조건부 Choice/Response Selector: 위 ConditionSet/Evaluator를
  소비한다([[DT-007-Condition-Review]]의 입력 계약). mutation Effect는 별도 mutation provider 주입 필요.
- State Read/Set Dialogue 노드와 조건부 Choice/Response Selector
- SaveGame file/slot, backup, autosave 정책(`capture_world_state`/`restore_world_state` adapter 소비)
- schema migration/key alias와 full int64 snapshot wire

## Public Contract

```text
# WorldStateStore (/root/WorldState)
has_key
get_value / try_get_value
set_value
reset_value / reset_lifetime
export_snapshot / import_snapshot
peek_snapshot_compatibility
apply_batch
value_changed / state_reset / snapshot_imported

# WorldStateRuntime (/root/WorldStateRuntime)
is_store_ready / is_session_ready
start_new_game
restore_game / restore_world_state
capture_world_state
world_state_ready / world_state_failed
```

## Invariants

- 미등록 key는 생성하지 않는다.
- type mismatch는 값을 변경하지 않는다.
- SAVE와 SESSION은 namespace와 독립된 lifetime이다.
- 파일 저장 책임을 갖지 않는다.
- batch는 atomic하다.
- invalid schema는 부분 초기화하지 않는다.
- gameplay 진입 준비는 Store ready가 아니라 coordinator의 session-ready로 판단한다.
- SESSION은 새 게임/load에서만 default로 시작하고 scene/Dialogue 종료로 자동 reset하지 않는다.
- 호환되지 않는 snapshot 복원 실패는 기존 Store 값과 기존 session-ready를 보존한다.
- 조건 평가는 pure read이고 mutation은 명시적 Effect에서만 수행한다.
- snapshot import는 SAVE replace-load이며 version 불일치는 commit 전에 거부한다.
- 상태 변화는 향후 trace/history가 소비할 수 있는 diff로 설명 가능해야 한다.

## Future Consumers

- ConditionEvaluator
- Set/Add State Effect
- Response Selector
- Quest system
- DialogueHistory와 State Inspector

## Usage

Schema 작성, Store 초기화, read/write/reset, batch, snapshot 파일 연동, Dialogue provider 주입,
오류 로그와 수동 점검 절차는 [[World-State-User-Guide]]를 따른다.
