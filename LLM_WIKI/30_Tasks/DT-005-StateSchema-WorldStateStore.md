---
id: DT-005
type: task
status: done
system: WorldState
created: 2026-06-12
updated: 2026-06-12
tags: [task, dialogue-tool, world-state]
---

# StateSchema + WorldStateStore

## Goal

대규모 반응형 대화를 위한 타입 안전한 게임 상태 기반을 만든다.

DialogueTool과 게임 시스템은 문자열 Dictionary를 직접 읽고 쓰지 않고, 등록된 key와 타입 계약을 통해 상태를 조회·변경한다. 이후 ConditionSet, Set Variable, Response Selector, DialogueHistory가 이 기반을 공유한다.

최종 목표는 수백~수천 개 상태와 복합 조건이 대사, 선택지 노출, 응답 우선순위, 후속 Effect를 통제하는 대화 시스템이다. DT-005는 그 전체 규칙 엔진을 구현하지 않고 다음 확장을 감당할 상태 계약과 실행 경계를 만든다.

```text
StateSchema / WorldStateStore
  -> ConditionSet / ConditionEvaluator
  -> conditional Choice / Response Selector
  -> State mutation Effect
  -> DialogueHistory / evaluation trace / inspector
```

대화 그래프가 전역 Dictionary 구조나 save format을 직접 알게 하지 않는다. 조건과 Effect는 이후 별도 데이터 모델로 추가하되, 모두 동일한 state provider 계약을 사용한다.

## Context

현재 DialogueTool의 `VariableDef`는 그래프 내부 상수에 가깝고 게임 전체의 지속 상태를 표현하지 못한다. `PlayerData` 오토로드는 비어 있으며 별도의 저장 시스템도 아직 확정되지 않았다.

```text
quest.missing_child.stage
actor.noabel.affinity
faction.guard.reputation
player.stat.charisma
dialogue.blacksmith.first_met
```

## Core Distinction

### Namespace

key의 도메인을 표현한다: `quest`, `actor`, `faction`, `player`, `world`, `dialogue`.

### Lifetime

값이 언제 초기화되고 저장되는지 표현한다.

- `SAVE`: 플레이스루 저장 데이터에 포함
- `SESSION`: 실행 중에만 유지하며 새 게임/로드 시 초기화

Namespace와 lifetime은 별개다. `actor.noabel.affinity`는 actor namespace지만 SAVE lifetime을 가질 수 있다.

## Proposed Resources

### StateDefinition

```gdscript
class_name StateDefinition extends Resource

@export var key: StringName
@export var value_type: StateValueType
@export var default_value: Variant
@export var lifetime: StateLifetime = StateLifetime.SAVE
@export var writable: bool = true
@export_multiline var description: String
@export var tags: Array[StringName] = []
```

초기 지원 타입은 bool, int, float, String, StringName으로 제한한다. Vector, Color, Object, Resource, Callable은 초기 범위에서 제외한다.

### StateSchema

```gdscript
class_name StateSchema extends Resource

@export var schema_version: int = 1
@export var definitions: Array[StateDefinition]
```

책임:

- key 중복과 형식 검사
- default value 타입 검사
- key -> StateDefinition lookup 생성
- 구조화된 validation 결과 제공

초기 구현은 하나의 schema resource를 사용한다. public API와 key 계약은 향후 여러 schema fragment를 합쳐 하나의 read-only registry를 만드는 구성을 막지 않아야 한다. DT-005에서는 fragment merge, mod load order, override를 구현하지 않는다.

### WorldStateStore

schema가 지정된 `.tscn`을 autoload하는 방식을 우선한다.

```gdscript
signal value_changed(key: StringName, old_value: Variant, new_value: Variant)
signal state_reset(lifetime: StateLifetime)
signal snapshot_imported(report: Dictionary)

func has_key(key: StringName) -> bool
func get_value(key: StringName) -> Variant
func try_get_value(key: StringName, fallback: Variant = null) -> Variant
func set_value(key: StringName, value: Variant) -> Error
func reset_value(key: StringName) -> Error
func reset_lifetime(lifetime: StateLifetime) -> void
func export_snapshot(lifetime: StateLifetime = StateLifetime.SAVE) -> Dictionary
func import_snapshot(snapshot: Dictionary) -> Dictionary
```

Store는 유효한 schema로 완전히 초기화된 경우에만 사용 가능하다. null Definition, 중복 key, 잘못된 enum/default가 하나라도 있으면 부분 registry를 만들지 않고 초기화 전체를 실패시킨다.

Dialogue와 조건 평가 계층에 노출할 provider 계약은 Store 구현보다 작게 유지한다.

```gdscript
func has_state(key: StringName) -> bool
func read_state(key: StringName) -> Variant
func try_read_state(key: StringName, fallback: Variant = null) -> Variant
```

mutation은 별도 계약으로 둔다. 읽기만 필요한 ConditionEvaluator가 쓰기 API에 의존하지 않게 하기 위함이다.

```gdscript
func set_state(key: StringName, value: Variant) -> Error
func apply_state_batch(changes: Array[Dictionary]) -> Dictionary
```

## Behavioral Rules

### Keys

- lower snake case와 dot namespace를 사용한다.
- 빈 segment, 공백, 대문자, 연속 dot은 validation 오류다.
- runtime에 key를 즉석 생성하지 않는다.
- canonical 문법은 `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$`다.
- 최소 두 segment를 요구하며 첫 segment를 namespace로 취급한다.

### Types

- `set_value()`는 strict type validation을 수행한다.
- int -> float 같은 암시적 변환도 초기 구현에서는 금지한다.
- 잘못된 값은 저장하지 않고 오류를 반환한다.

### Missing Keys

- `get_value()`는 미등록 key에 오류를 기록하고 null을 반환한다.
- 실패 허용 경로는 `try_get_value()`를 사용한다.

### Defaults and Reset

- Store 초기화 시 schema default로 값을 생성한다.
- reset은 Definition의 default로 되돌린다.
- 같은 값으로 set/reset하면 성공하지만 `value_changed`를 발행하지 않는다.

### Persistence Boundary

- WorldStateStore는 파일 경로와 저장 슬롯을 알지 않는다.
- snapshot export/import만 제공한다.
- 실제 파일 저장은 향후 SaveGame 시스템 또는 PlayerData가 담당한다.
- snapshot은 JSON 호환 wire data만 포함한다. `StringName` 값은 `String`으로 export하고 schema 타입에 따라 import 시 복원한다.
- import는 현재 SAVE 값을 먼저 default로 되돌린 뒤 snapshot의 유효한 SAVE 값만 적용하는 replace-load 동작이다.
- schema version이 정확히 일치하지 않거나 최상위 구조가 잘못되면 아무 값도 변경하지 않고 전체 import를 거부한다.
- 개별 unknown key, SESSION key, 타입 불일치는 해당 항목만 무시하고 report에 남긴다.
- `writable`은 gameplay mutation 제한이다. reset과 snapshot import는 시스템 작업이므로 read-only key에도 적용할 수 있다.

```gdscript
{
    "schema_version": 1,
    "values": {
        "quest.main.stage": 2,
        "actor.noabel.affinity": 35
    }
}
```

## Steps

### Step 1: StateDefinition과 StateSchema

목표:
- 타입, default, lifetime을 선언하는 Resource와 validation/lookup을 구현한다.

완료 조건:
- 정상 schema가 오류 없이 lookup을 생성한다.
- 중복 key, 잘못된 key, 타입 불일치를 검출한다.
- null Definition, 잘못된 enum, 1 미만 schema version을 검출한다.
- validation 실패 시 부분 lookup을 공개하지 않는다.
- 저장 후 재로드해 Definition 순서와 값이 보존된다.

### Step 2: WorldStateStore Read/Write

목표:
- schema default로 초기화되고 타입 안전하게 값을 읽고 쓴다.

작업:
- schema를 export한 Store scene/script
- read/write/reset API와 `value_changed`
- unknown/read-only/type mismatch 오류 처리

완료 조건:
- 유효한 schema로만 Store가 ready 상태가 된다.
- 등록 key만 변경할 수 있다.
- 실패한 set이 기존 값을 바꾸지 않는다.
- reset이 default를 복원한다.
- 같은 값 재설정은 성공하고 signal을 발행하지 않는다.
- read-only는 gameplay set만 거부하고 reset은 허용한다.

### Step 3: Lifetime과 Snapshot

목표:
- SAVE와 SESSION을 구분하고 외부 저장 시스템용 snapshot API를 제공한다.

권장 import 정책:
- 결과를 `applied`, `ignored`, `errors`로 보고한다.
- 타입 오류 key만 거부하고 나머지는 적용한다.
- import 시작 시 SAVE 값을 default로 reset하고 snapshot에 없는 key는 default가 된다.
- unknown key는 무시하되 report에 남긴다.
- schema version/최상위 구조 오류는 commit 전 전체 거부한다.

완료 조건:
- SAVE만 export되고 SESSION은 포함되지 않는다.
- snapshot round-trip에서 값이 보존된다.
- 잘못된 snapshot이 Store를 손상시키지 않는다.
- export 결과를 JSON으로 직렬화/역직렬화한 뒤 bool/int/float/String/StringName 값이 schema 타입으로 복원된다.

### Step 4: Atomic Mutation Batch

목표:
- 한 선택의 여러 Effect를 검증 후 한 묶음으로 적용한다.

```gdscript
func apply_batch(changes: Array[Dictionary]) -> Dictionary
```

초기 change 형식은 `{ "key": StringName, "value": Variant }`만 지원한다. 같은 key가 두 번 이상 나오면 의도를 추측하지 않고 batch 전체를 거부한다. 모든 값을 먼저 검증하고 한 번에 commit한 뒤 입력 순서대로 `value_changed`를 발행한다. signal callback에서 발생한 후속 mutation은 이미 완료된 batch와 별도 transaction으로 처리한다.

완료 조건:
- 모든 변경을 먼저 검증한다.
- 중간 실패 시 부분 적용이 없다.
- 결과 diff에 key, old, new가 기록된다.
- 같은 key가 batch에 여러 번 등장하면 전체 거부한다.
- 실패한 batch는 값과 signal을 모두 변경하지 않는다.

### Step 5: Dialogue Runtime Integration Seam

목표:
- 새 Dialogue 노드를 만들지 않고 Player가 상태 공급자를 받을 경계를 만든다.

작업:
- read provider와 mutation provider 계약 분리
- `DialogueManager -> DialogueUI -> DialoguePlayer` 명시적 provider 전달 경로
- 테스트에서 fake store를 주입할 수 있게 구성
- 기존 Variable/Expression 동작 유지

완료 조건:
- Dialogue runtime이 save file이나 PlayerData를 알지 않는다.
- DialoguePlayer가 `/root`를 직접 조회하지 않는다.
- provider 미지정과 fake read provider 주입을 테스트할 수 있다.
- 기존 Dialogue 회귀 테스트가 통과한다.

이 Step에서는 State Read/Set Dialogue 노드를 만들지 않는다. provider를 실제로 소비하는 노드와 ConditionEvaluator는 후속 Task에서 추가한다. 사용되지 않는 mutation provider를 DialoguePlayer에 미리 주입하지 않는다.

### Step 6: Integration Regression and Review

검증:
- default 초기화
- 정상/비정상 set과 reset
- SAVE/SESSION 분리
- snapshot export/import 오류 행렬
- atomic batch 성공/실패
- fake provider Dialogue integration
- 기존 DialogueTool 전체 회귀

완료 조건:
- P0/P1 없음
- Godot headless editor load 성공
- 테스트 결과와 accepted debt를 Review 문서에 기록

## Implementation Log

### Step 1: StateDefinition과 StateSchema — 구현 완료 (2026-06-12, 코드 리뷰 대기)

설계 pseudocode는 `value_type: StateValueType` / `lifetime: StateLifetime`를 전역 타입처럼
표기했지만 Godot 4에는 전역 enum이 없다. 따라서 두 enum을 `StateDefinition` 안의 named enum으로
두고 다른 스크립트에서는 `StateDefinition.StateValueType` / `StateDefinition.StateLifetime`로
참조한다. enum 멤버와 허용 값(BOOL/INT/FLOAT/STRING/STRING_NAME, SAVE/SESSION)은 설계와 동일하다.
이는 계약 변경이 아닌 엔진 제약에 따른 namespacing 차이다.

**변경 파일**
- `Assets/Script/gds/world_state/state_definition.gd`: `@tool class_name StateDefinition extends Resource`.
  `key`, `value_type`, `default_value`, `lifetime`, `writable`, `description`, `tags` 필드와
  `StateValueType`/`StateLifetime` enum, enum -> 내장 Variant 타입 매핑 static helper
  (`builtin_type_for`, `is_known_value_type`, `is_known_lifetime`).
- `Assets/Script/gds/world_state/state_schema.gd`: `@tool class_name StateSchema extends Resource`.
  `schema_version`, `definitions` 필드. `validate()`가 구조화된 결과
  `{valid, errors[{code,index,key,message}], error_codes[], key_count}`를 반환한다.
  검증을 모두 통과한 경우에만 key -> StateDefinition lookup(`has_key`/`get_definition`/`keys`)을
  채운다. 오류가 하나라도 있으면 lookup은 빈 채로 유지된다.
- `Assets/Script/gds/world_state/tests/dt005_step1_schema_test.gd` (+ `.tscn`): Step 1 전용
  헤드리스 테스트.

**검증 규칙 (구현된 사실)**
- key 문법: `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$` (RegEx). 최소 두 segment를 강제한다.
- 허용 타입은 bool, int, float, String, StringName 뿐이다.
- default 타입은 `typeof()` 정확 일치로 검사한다. int->float, String<->StringName, null 등
  암시적 변환을 허용하지 않는다.
- 검출하는 오류 code: `schema_version_invalid`, `definition_null`, `value_type_invalid`,
  `lifetime_invalid`, `key_empty`, `key_invalid_format`, `key_duplicate`, `default_type_mismatch`.

**검증 (Godot 4.6.3 mono headless)**
- 실행 바이너리: `D:\SteamLibrary\steamapps\common\Godot Engine\Godot_v4.6.3-stable_mono_win64_console.exe`
- `--headless --import` 로 class cache 재생성(신규 `class_name` 등록) → editor load 성공 (exit 0).
  새 `class_name`을 직접 scene 부팅으로만 로드하면 global class cache 미등록으로 parse error가 난다.
- `--headless <test.tscn>` 실행 → `[DT-005 Step1] ALL PASS`, exit 0. 케이스 A~M 전부 통과.
  - A: 정상 schema validation 통과 + key lookup 동작 (6 keys).
  - B~K: 빈 key, 단일 segment, 형식 위반 7종, 중복 key, default 불일치,
    암시적 변환 3종(int->float / String<->StringName / null), null Definition,
    잘못된 value_type/lifetime enum, schema_version 0 → 해당 error code 검출.
  - L: 정상 1 + 잘못된 1 → `key_count == 0`, 정상 key도 lookup 비공개(부분 공개 안 함).
  - M: `.tres` 저장 → `CACHE_MODE_IGNORE` 재로드 → Definition 순서/key/value_type/default
    (+typeof)/lifetime/writable/description/tags 보존, StringName default가 재로드 후에도
    `TYPE_STRING_NAME`(21)로 유지. 임시 리소스(`user://dt005_tmp_schema.tres`)는 테스트 종료 시 삭제.
  - N: 검증 후 mutation 시 lookup 무효화(아래 리뷰 수정 P1) — deep mutation(def.key/value_type),
    schema_version setter, definitions 재할당 4종.
  - O: validate()/last_result() 반환 dict를 변조해도 내부 is_valid/error가 유지됨(리뷰 수정 P2).
  - P: in-place 배열 변경(append invalid/valid, erase, remove_at, 인덱스 대입 5종)이 setter를
    우회해도 재검증으로 감지됨(2차 리뷰 수정 P1).
- 종료 시 "ObjectDB instances leaked" / "resources still in use" 경고는 import pass에서도 나타나는
  Godot 종료 시점 양성 경고이며 테스트 실패가 아니다.

**Step 1 코드 리뷰 수정 (2026-06-12)**
- [P1] 검증 후 Resource 변경 시 오래된 lookup 신뢰 → 시그널 기반 무효화로 수정.
  `StateDefinition`의 모든 필드 setter가 Resource `changed`를 발행하고, `StateSchema`는 검증 시
  현재 definitions의 `changed`를 구독한다(`_rewatch`). Definition deep mutation, `definitions`
  재할당, `schema_version` 변경이 모두 `invalidate()`를 거쳐 다음 접근에서 재검증된다.
  회귀 테스트 케이스 N 추가.
- [P2] validation 결과 외부 변조 → `validate()`와 `last_result()`가 `_last_result.duplicate(true)`
  deep copy를 반환하도록 수정. 내부 상태는 분리. 회귀 테스트 케이스 O 추가.
- [P2] 캐시 무효화 회귀 테스트 누락 → 케이스 N(무효화)·O(불변성) 추가로 해소.
- 수정 후 재검증: 헤드리스 테스트 A~O 전부 통과(exit 0), `--import` editor load 성공(exit 0).

**Step 1 코드 리뷰 2차 수정 (2026-06-12)**
- [P1] in-place 배열 변경(`append`/`erase`/`remove_at`/인덱스 대입)이 `definitions` setter를
  우회해 stale lookup이 남던 문제 → 구조 지문 기반 감지로 수정. `validate()`가
  `definitions.size()`와 `definitions.hash()`를 기록하고, 접근 시 `_ensure_validated()`가 이를
  비교해 변화가 있으면 재검증한다. size 비교(O(1))가 append/erase/remove_at의 흔한 경로를 단락
  처리하고, hash 비교가 인덱스 대입(크기 동일, 인스턴스 교체)까지 감지한다. setter 무효화 +
  changed 시그널(필드 deep mutation) + 구조 지문(배열 구조 변경)으로 세 경로를 모두 닫았다.
  회귀 테스트 케이스 P(append invalid/valid, erase, remove_at, 인덱스 대입 5종) 추가.
- 수정 후 재검증: 헤드리스 테스트 A~P 전부 통과(exit 0).

**남은 위험 / 다음 Step으로 이월**
- 구조 지문의 `hash()`는 O(n)이라, 매우 큰 schema에서 accessor를 반복 호출하면 비용이 누적될 수
  있다. Store(Step 2)는 유효 schema를 한 번 검증 후 자체 map으로 소비하는 흐름이라 hot path가
  아니다. 대규모 authoring 시 compiled read-only registry는 측정 후 후속 설계(Scale Policy 참고).
- WorldStateStore, autoload/project.godot, runtime 값, snapshot, batch, Dialogue provider 연동은
  이번 Step 범위 밖(Step 2 이후).
- 자기 승인하지 않는다.

### Step 2: WorldStateStore Read/Write — 구현 완료 (2026-06-12, 코드 리뷰 대기)

**변경 파일**
- `Assets/Script/gds/world_state/world_state_store.gd`: `class_name WorldStateStore extends Node`.
  `@export var schema: StateSchema`. `initialize()`가 유효 schema일 때만 ready가 되고 default로
  `_values`를 채운다. `_ready()`가 export된 schema로 자동 초기화한다.
- `Assets/Script/gds/world_state/world_state_store.tscn`: autoload용 Store scene(스크립트만 부착,
  schema는 미할당 — 실제 autoload 연결은 후속 통합 작업). Step 2에서는 project.godot를 바꾸지 않는다.
- `Assets/Script/gds/world_state/tests/dt005_step2_store_test.gd` (+ `.tscn`): Step 2 헤드리스 테스트.

**구현된 API와 동작**
- `signal value_changed(key, old_value, new_value)` — 값이 실제로 바뀔 때만 발행.
- `initialize() -> bool`, `is_store_ready() -> bool`.
- `has_key(key)`, `get_value(key)`(미등록/not-ready면 오류 기록 후 null),
  `try_get_value(key, fallback=null)`(실패 허용, 무오류).
- `set_value(key, value) -> Error`: strict type validation(`typeof` 정확 일치, 암시적 변환 금지).
  - `ERR_UNAVAILABLE`(not-ready), `ERR_DOES_NOT_EXIST`(미등록), `ERR_UNAUTHORIZED`(read-only gameplay set),
    `ERR_INVALID_DATA`(타입 불일치). 실패 시 값/시그널 불변.
- `reset_value(key) -> Error`: Definition default로 복원. 시스템 작업이라 read-only key에도 허용.
- 같은 값 set/reset은 `OK`를 반환하되 `value_changed`를 발행하지 않는다.

**범위 메모**
- lifetime reset(`reset_lifetime`/`state_reset`), snapshot export/import(Step 3), atomic batch(Step 4),
  Dialogue provider seam(Step 5)은 구현하지 않았다. autoload 등록도 하지 않았다.

**검증 (Godot 4.6.3 mono headless)**
- `--headless --import`로 `WorldStateStore` class 등록 + editor load 성공(exit 0).
- `--headless dt005_step2_store_test.tscn` → `[DT-005 Step2] ALL PASS`, exit 0. 케이스 A~L:
  - A: null schema → not-ready, get null, set `ERR_UNAVAILABLE`.
  - B: invalid schema → not-ready.
  - C: default 초기화(int/String/float/StringName/bool), StringName이 `TYPE_STRING_NAME`(21).
  - D: 정상 set → `OK`, value_changed(old=100,new=250) 1회.
  - E: 미등록 key → `ERR_DOES_NOT_EXIST`, 무시그널.
  - F: read-only gameplay set → `ERR_UNAUTHORIZED`, 값 불변, 무시그널.
  - G: 타입 불일치 4종(int<-str, float<-int, str<-sn, sn<-str) → `ERR_INVALID_DATA`, 값/시그널 불변.
  - H: 같은 값 set → `OK`, 무시그널.
  - I: reset → default 복원, 변경 시 시그널.
  - J: read-only key도 reset 허용(`OK`).
  - K: 이미 default면 reset 성공하되 무시그널.
  - L: try_get_value/has_key(미등록 fallback/null).
  - M: 초기화 후 schema의 default/writable/타입 변경·key 추가/삭제가 Store 계약에 섞이지 않음(리뷰 수정 P1).
  - N: schema 교체 후 `initialize()` 재호출로만 새 계약/default 반영, invalid 재초기화는 not-ready.
- Step 1 회귀(`dt005_step1_schema_test`) ALL PASS, exit 0.
- 음성 경로 테스트의 `ERROR:` 로그는 의도된 `push_error`(오류 기록 검증)이며 테스트 실패가 아니다.

**Step 2 코드 리뷰 수정 (2026-06-12)**
- [P1] 초기화 후 schema 변경 시 Store가 두 계약을 혼합하던 문제 → 계약 compile로 수정.
  `_values`는 init 시점 schema로 만들지만 `set_value`/`reset_value`가 live `schema`를 다시
  조회해, 초기화 후 schema 교체·key 삭제/추가·타입/default/writable 변경이 동작에 섞였다.
  수정: `initialize()`가 `_contract`(StringName -> `{builtin_type, default, writable}`) private map을
  compile하고, 이후 read/write/reset은 mutable schema/Definition을 다시 조회하지 않는다. schema
  변경은 `initialize()` 재호출(명시적 재초기화)로만 반영된다. 부수 효과로 Step 1의 `definitions.hash()`
  runtime hot-path 비용도 제거된다. 회귀 테스트 M(계약 격리)·N(재초기화) 추가.
- 수정 후 재검증: 헤드리스 테스트 A~N 전부 통과(exit 0), Step 1 회귀 통과, editor load 성공.

**남은 위험 / 다음 Step으로 이월**
- 계약은 init 스냅샷이므로 schema를 바꾼 뒤 `initialize()`를 호출하지 않으면 옛 계약이 유지된다
  (의도된 동작). 변경 반영은 호출자가 명시적으로 재초기화해야 한다.
- Store를 autoload로 등록하지 않았다. 실제 게임 통합 시 schema 리소스를 할당한 `.tscn`을 autoload로
  연결하는 작업이 남는다(테스트는 `new()` + schema 주입 경로 사용).
- lifetime 구분과 snapshot은 Step 3, batch는 Step 4에서 다룬다.
- 자기 승인하지 않는다.

### Step 3: Lifetime과 Snapshot — 구현 완료 (2026-06-12, 코드 리뷰 대기)

**변경 파일**
- `Assets/Script/gds/world_state/world_state_store.gd`: lifetime/snapshot API 추가.
  - 계약 compile에 `lifetime`을 포함하고 `_schema_version`을 스냅샷한다.
  - `signal state_reset(lifetime)`, `signal snapshot_imported(report)` 추가.
  - `reset_lifetime(lifetime)`: 해당 lifetime의 모든 key를 default로 복원(read-only 포함),
    값이 바뀐 key마다 value_changed, 끝에 state_reset 1회.
  - `export_snapshot(lifetime=SAVE) -> { schema_version, values }`: JSON 호환 wire.
    StringName 값은 String으로 정규화. 기본 SAVE만(SESSION 제외).
  - `import_snapshot(snapshot) -> report`: replace-load.
  - JSON 왕복 복원용 `_to_wire`, `_coerce_wire_value` 헬퍼.
- `Assets/Script/gds/world_state/tests/dt005_step3_snapshot_test.gd` (+ `.tscn`): Step 3 테스트.

**import 정책 (구현된 사실)**
- 최상위 구조(Dictionary + number `schema_version` + Dictionary `values`) 또는 `schema_version`
  불일치는 commit 전 전체 거부하고 아무 값도 바꾸지 않는다. `schema_version`은 JSON 왕복을 고려해
  int/float 모두 허용하고 `int()` 비교한다.
- 통과 시 SAVE key들의 최종 값을 먼저 계산(snapshot 유효 값 또는 default)한 뒤 key마다 1회만
  commit한다 — reset→set 중간 신호가 없는 replace-load. snapshot에 없는 SAVE key는 default가 된다.
- unknown key→`ignored(unknown_key)`, SESSION key→`ignored(session_key)`, 타입 불일치→
  `errors(type_mismatch)`로 개별 처리하고 나머지를 적용한다. 적용된 key는 `applied`.
- read-only key에도 적용된다(시스템 작업). SESSION key는 import에서 reset되지 않는다.

**검증 (Godot 4.6.3 mono headless)**
- `--headless dt005_step3_snapshot_test.tscn` → `[DT-005 Step3] ALL PASS`, exit 0. 케이스 A~K:
  - A: export 형식(`schema_version`/`values`), SAVE만·SESSION 제외, StringName→String wire.
  - B: `export_snapshot(SESSION)`은 SESSION만.
  - C: `reset_lifetime` — 대상 lifetime만 default, 타 lifetime 미변경, state_reset 1회 발행.
  - D: 메모리 snapshot 왕복(값/StringName 타입 보존).
  - E: JSON stringify→parse_string 후 bool/int/float/String/StringName이 schema 타입으로 복원
    (typeof INT/FLOAT/STRING/STRING_NAME/BOOL 확인).
  - F: replace-load — snapshot에 없는 SAVE key는 default, SESSION 미변경.
  - G: 잘못된 구조 5종(빈/values 없음/version 없음/version String/values 비-Dict) 전체 거부·무변경.
  - H: `schema_version` 불일치 전체 거부·무변경.
  - I: unknown/SESSION/type-mismatch 개별 분류 + 유효 항목 적용, 무효 SAVE 값은 default로.
  - J: read-only key에도 import 적용.
  - K: snapshot_imported 발행 + 거부 import 뒤 Store 정상 동작.
  - L: import 중 value_changed가 부분 상태를 노출하지 않음(첫 callback에서 전체 최종 상태)(리뷰 P1).
  - M: 손실 입력 coercion 거부 — version 1.5/INT 1.000001/inf/nan은 거부, version 1.0·INT 5.0은 허용(리뷰 P1).
  - N: report deep-copy 격리(반환 변조가 signal에 무영향) + not-ready 포함 모든 경로 발행(리뷰 P2).
  - O: 큰 INT JSON-safe 범위 강제 — 경계(±(2^53-1)) 허용·JSON 왕복 보존, 2^53+1/±2^63/INT64
    한계는 거부(2차 리뷰 P1). FLOAT key에 int wire를 넣을 때도 같은 범위 강제(2^53+1/INT64는 거부·
    default 유지, ±(2^53-1)는 허용·정확 보존)(5차 리뷰 P1).
  - P: 쓰기 경계 JSON-safe 강제 — set_value 2^53+1/INF/NAN 거부, 경계값 set+export 무손실,
    unsafe schema_version/INT default/INF FLOAT default는 not-ready(3차 리뷰 P1).
  - Q: 알림 transaction — value_changed 발행 중 set/reset/initialize/reset_lifetime/import 재진입은
    모두 거부(`ERR_BUSY`/`false`/`store_busy`). batch 값·성공 report가 실제 상태와 일치하고 이벤트
    new_value가 실제 값과 일치(stale 없음), 알림 종료 후 명시적 initialize는 정상(3·4차 리뷰 P2/P1).
- Step 1·2 회귀 ALL PASS, editor load(`--import`) exit 0.
- 테스트의 `_list_has_key` 헬퍼에서 Dictionary==String 비교로 단언이 중단되던 결함을 발견·수정
  (제품 코드 아님). 음성 경로 `ERROR:` 로그는 의도된 `push_error`.

**Step 3 코드 리뷰 수정 (2026-06-12)**
- [P1] import 중 value_changed가 부분 적용 상태를 노출 → 모든 SAVE 값을 먼저 `_values`에 반영한 뒤
  결정된 순서(contract 순서)로 신호를 발행하도록 수정. signal 없이 값만 반영하는 `_stage` +
  모아 발행하는 `_emit_changes` 헬퍼를 도입하고 `_commit`/`reset_lifetime`/`import_snapshot`을
  같은 패턴으로 통일. 회귀 테스트 L(첫 callback에서 전체 최종 상태) 추가.
- [P1] 숫자 coercion이 손실 입력을 승인 → `_as_exact_int`로 finite하고 정확히 정수이며 int64 범위
  안인 값만 허용(`is_equal_approx`/맹목적 `int()` 제거). `schema_version`과 INT 복원 모두 이 검사를
  사용한다. FLOAT는 finite만 허용(inf/nan 거부). 회귀 테스트 M 추가.
- [P2] report signal/반환 계약 + 발행 정책 불일치 → `_finish_import`로 통일. 모든 종료 경로
  (not-ready 포함)가 `snapshot_imported`를 발행하고, signal과 반환값에 각각 독립 `duplicate(true)`
  deep copy를 사용한다. 회귀 테스트 N 추가.
- 수정 후 재검증: 헤드리스 테스트 A~N 전부 통과(exit 0), Step 1·2 회귀 통과, editor load 성공.

**Step 3 코드 리뷰 2차 수정 (2026-06-12)**
- [P1] 큰 INT가 JSON 왕복에서 조용히 변형(2^53+1 -> 2^53, 양의 2^63 float -> INT64_MIN) →
  snapshot INT를 JSON 안전 정수 범위 `±(2^53-1)`로 제한. `_as_exact_int`가 int/float 모두 이
  범위를 강제하고, 초과 값(2^53+1, ±2^63, INT64_MIN/MAX)은 조용히 반올림/wrap하지 않고 거부한다.
  Godot JSON이 숫자를 double로 파싱하는 사실에 맞춘 결정(스냅샷은 JSON 호환 wire 계약).
  회귀 테스트 O(경계 허용 + JSON 왕복 보존, 2^53+1 int/JSON 거부, ±2^63·INT64 한계 거부) 추가.
- 수정 후 재검증: 헤드리스 테스트 A~O 전부 통과(exit 0), Step 1·2 회귀 통과, editor load 성공.

**Step 3 코드 리뷰 3차 수정 (2026-06-12)**
- [P1] import만 제한하고 export/쓰기 경계는 손실 값을 허용하던 문제 → JSON-safe 정책을 모든 쓰기
  경계로 확장. `_value_in_domain`(INT ±(2^53-1), FLOAT finite)을 도입하고:
  - `set_value`가 도메인 위반(2^53 초과 INT, INF/NAN FLOAT)을 `ERR_INVALID_DATA`로 거부.
  - `initialize()`가 `schema_version` JSON-safe 범위와 모든 default 도메인을 검사해, 위반 시
    ready를 거부(not-ready). 따라서 ready Store의 모든 값은 항상 JSON snapshot으로 무손실 export
    가능 → 범위 밖 값이 든 Store가 snapshot을 만들 수 없음을 보장.
  - 회귀 테스트 P(set/default/schema_version unsafe 거부 + 경계값 export 왕복) 추가.
- [P2] value_changed 콜백 재진입 시 staged batch event가 변형된 값과 함께 뒤늦게 발행되던 문제 →
  알림 transaction 정책 도입. `_emit_changes` 발행 중 `_in_notification`을 세우고, 그동안의
  mutation(set/reset/reset_lifetime/import)을 `ERR_BUSY`(import는 report `store_busy`)로 거부한다.
  모든 value_changed 발행을 `_emit_changes` 단일 경로로 통일(`_commit`도 경유). 회귀 테스트 Q 추가.
- 수정 후 재검증: 헤드리스 테스트 A~Q 전부 통과(exit 0), Step 1·2 회귀 통과, editor load 성공.

**Step 3 코드 리뷰 4차 수정 (2026-06-12)**
- [P1] `initialize()`가 알림 transaction guard를 우회 → 알림 중 호출되면 상태를 비워 in-flight batch가
  손상되고 성공 report와 실제 값이 불일치하던 문제 수정. `initialize()` 최상단(상태 비우기 전)에
  `_in_notification` 검사를 추가해 알림 중 재초기화를 거부(false)하고 기존 상태를 보존한다.
- 테스트 Q 확장: 콜백 중 `initialize()`가 false이고 기존/import 값이 유지됨, 성공 report와 실제 값
  일치, 이벤트 new_value와 실제 값 일치, 알림 종료 후 명시적 initialize 정상, `reset_lifetime`/
  `import_snapshot` 재진입 거부까지 회귀 검증.
- 수정 후 재검증: 헤드리스 A~Q 전부 통과(exit 0), Step 1·2 회귀 통과, editor load 성공.

**Step 3 코드 리뷰 5차 수정 (2026-06-12)**
- [P1] FLOAT key import에서 큰 int wire가 `float(wire)`로 조용히 반올림(9007199254740993 ->
  9007199254740992.0)되며 `applied`로 보고되던 문제 수정. `_coerce_wire_value`의 FLOAT 분기에서
  int wire를 JSON-safe 범위(±(2^53-1)) 안일 때만 float로 변환하고, 범위 밖은 거부한다(그 범위
  안의 정수는 double로 정확 표현). 회귀 테스트 O에 FLOAT←2^53+1/INT64 거부·default 유지,
  FLOAT←±(2^53-1) 허용·정확 보존 추가.
- 수정 후 재검증: 헤드리스 A~Q 전부 통과(exit 0), Step 1·2 회귀 통과, editor load 성공.

**남은 위험 / 다음 Step으로 이월**
- runtime INT/FLOAT 도메인이 JSON-safe로 제한된다(INT ±(2^53-1), FLOAT finite). 게임 상태 범위에는
  충분하나, full int64가 필요하면 후속에서 INT를 canonical decimal String으로 export/import하는
  방식을 검토한다.
- 알림 중 재진입 mutation은 거부(`ERR_BUSY`)한다. Step 4 atomic batch에서 "콜백 후속 mutation을
  별도 transaction으로 처리"하려면 거부 대신 deferral queue로 정책을 확장할지 그때 결정한다.
- import는 SAVE replace-load만 다룬다. SESSION 영속은 설계상 제외(런타임 한정).
- 실제 파일 직렬화/슬롯은 외부 SaveGame/PlayerData 책임(Store는 path-agnostic).
- atomic batch는 Step 4, Dialogue provider seam은 Step 5.
- 자기 승인하지 않는다.

### Step 4: Atomic Mutation Batch — 구현 완료 (2026-06-12, 코드 리뷰 대기)

**변경 파일**
- `Assets/Script/gds/world_state/world_state_store.gd`: `apply_batch(changes: Array[Dictionary]) -> Dictionary` 추가.
- `Assets/Script/gds/world_state/tests/dt005_step4_batch_test.gd` (+ `.tscn`): Step 4 테스트.

**구현된 동작**
- change 형식 `{ "key": StringName, "value": Variant }`. 모든 변경을 먼저 검증하고 하나라도 실패하면
  batch 전체를 거부한다(부분 적용 없음, 값/시그널 불변).
- 검증: store ready, 알림 중 아님, 형식, 등록 key, writable(read-only 거부), 타입 일치,
  JSON-safe 도메인, 같은 key 중복 금지. 모든 오류를 모아 report에 담는다.
- 성공 시 모든 값을 먼저 `_values`에 반영한 뒤 **입력 순서**로 value_changed를 발행한다(부분 상태 노출 없음).
- 반환 report: `{ applied, diff:[{key,old,new}...], errors:[{index,key,reason}...] }`.
  diff는 실제로 바뀐 항목만 입력 순서로 기록(value_changed와 1:1). 같은 값은 적용되되 diff/signal 제외.
- errors reason: store_not_ready / store_busy / malformed_change / unknown_key / read_only /
  type_mismatch / out_of_domain / duplicate_key.

**알림 transaction 정책**
- batch도 기존 정책을 따른다 — value_changed 발행 중 재진입 mutation(set/reset/import/apply_batch)은
  거부(`ERR_BUSY` / report `store_busy`). Task의 "콜백 후속 mutation은 별도 transaction"은, 동기
  반환값이 오도되지 않도록 deferral 대신 명시적 거부로 구현했다(호출자가 알림 종료 후 별도 호출).
  deferral queue는 측정 후 후속 과제로 남긴다.

**검증 (Godot 4.6.3 mono headless)**
- `--headless dt005_step4_batch_test.tscn` → `[DT-005 Step4] ALL PASS`, exit 0. 케이스 A~M:
  - A: 정상 batch 전체 적용 + diff(key/old/new) + signal 3회.
  - B: value_changed가 입력 순서로 발행(contract 순서 아님) — name→gold.
  - C: 중간 타입 오류 시 전체 거부, 값/signal 불변, diff 빈.
  - D: 같은 key 중복 전체 거부. E: 미등록 key 거부. F: read-only 거부(전체).
  - G: JSON-safe 도메인 위반(2^53+1 INT, INF FLOAT) 거부. H: 형식 오류(value 없음) 거부.
  - I: 여러 오류 모두 수집(unknown+type 2건). J: 같은 값은 적용되되 diff/signal 제외.
  - K: 빈 batch 성공(무변경). L: not-ready 거부.
  - M: 첫 callback에서 전체 batch 값 반영(부분 상태 없음) + 알림 중 set/apply_batch 재진입 거부.
  - N: 잘못된 key 타입(null/int) → 런타임 오류 대신 `malformed_change`, 값/signal 불변(리뷰 P1).
  - O: String key 허용.
  - P: 중복 검사 독립성 — 첫 항목 type_mismatch + 같은 key 재등장 → 두 오류 모두 기록,
    unknown key 반복도 unknown+duplicate 기록(리뷰 P2).
- Step 1·2·3 회귀 ALL PASS, editor load(`--import`) exit 0.
- 음성 경로 `ERROR:` 로그는 의도된 `push_error`.

**Step 4 코드 리뷰 수정 (2026-06-12)**
- [P1] 잘못된 key 타입이 `StringName(...)` 런타임 오류를 내고 빈 Dictionary를 반환하던 문제 →
  key 타입을 먼저 검사(StringName/String만 허용, 그 외는 `malformed_change`)한 뒤 StringName으로
  변환. null/int key도 구조화된 report로 처리. 회귀 테스트 N·O 추가.
- [P2] 중복 검사가 type/domain 검사 뒤에 있어 첫 항목이 다른 오류면 `duplicate_key`가 누락되던 문제 →
  중복 검사를 다른 per-item 검사보다 앞당기고, 첫 등장이 오류여도 key를 `seen`에 기록해 재등장을
  항상 잡는다. 회귀 테스트 P(type+duplicate, unknown+duplicate) 추가.
- 수정 후 재검증: 헤드리스 A~P 전부 통과(exit 0), Step 1·2·3 회귀 통과, editor load 성공.

**남은 위험 / 다음 Step으로 이월**
- 알림 중 재진입 mutation은 거부한다. deferral queue가 필요하면 후속에서 검토(동기 반환 계약 유지 위해 현재는 거부).
- change 형식은 `{key, value}`만 지원한다(add/multiply 등 연산 Effect는 후속 ConditionSet/Effect Task).
- Dialogue provider seam은 Step 5.
- 자기 승인하지 않는다.

### Step 5: Dialogue Runtime Integration Seam — 구현 완료 (2026-06-12, 코드 리뷰 대기)

**선행 상태 메모(실제 코드 기준):** Step 5 제품 코드와 테스트 스크립트는 이번 세션 시작 시점에 이미
작업 트리에 존재했다(`dialogue_player.gd`/`dialogue_manager.gd`/`dialogue_ui.gd`는 git에 modified,
`world_state_store.gd` provider facade와 `dt005_step5_provider_seam_test.gd`도 존재). 기존 변경을
덮어쓰지 않고 그대로 두었다. 이번 작업은 (1) 누락된 테스트 scene(`.tscn`) 생성, (2) 전체 실행·검증,
(3) 문서화다.

**관련 파일(기존 구현)**
- `addons/dialogtool/RunTime/dialogue_player.gd`: read 상태 provider seam.
  `set_read_state_provider`/`get_read_state_provider`/`has_read_state_provider`, 그리고 조건 계층이
  쓸 `has_state`/`read_state`/`try_read_state`(provider 미지정 시 false/null/fallback로 안전 동작).
  mutation provider는 주입하지 않는다(소비 노드 없음). `/root`/PlayerData/save를 직접 조회하지 않는다.
- `addons/dialogtool/RunTime/dialogue_manager.gd`: `play(resource, read_state_provider=null)`가 provider를
  UI까지 전달.
- `addons/dialogtool/UI/dialogue_ui.gd`: `play(resource, read_state_provider=null)`가 deferred start 전에
  `dialogue_player.set_read_state_provider(provider)`로 주입.
- `Assets/Script/gds/world_state/world_state_store.gd`: provider 계약 facade —
  read(`has_state`/`read_state`/`try_read_state`)와 mutation(`set_state`/`apply_state_batch`)을 분리해
  Store의 좁은 view로 노출(기존 `has_key`/`get_value`/`set_value`/`apply_batch`에 위임).

**이번 세션 추가/검증**
- `Assets/Script/gds/world_state/tests/dt005_step5_provider_seam_test.tscn` 생성(기존 `.gd` 실행용).

**검증 (Godot 4.6.3 mono headless)**
- `--headless dt005_step5_provider_seam_test.tscn` → `[DT-005 Step5] ALL PASS`, exit 0. 케이스 A~G:
  - A: provider 미지정 — has_state false, read_state null, try_read_state fallback.
  - B: fake read provider 주입 — has/read/try가 fake로 라우팅.
  - C: WorldStateStore를 read provider로 주입 — read 동작 + Store가 mutation 계약(set_state)도 구현.
  - D: Variable→Branch 데이터 평가 유지(provider 미지정, true/false 분기).
  - E: Expression→Branch 데이터 평가 유지(`x > 0`).
  - F: DialogueUI.play가 provider를 Player까지 전달.
  - G: DialogueManager→UI→Player provider 전달(autoload 경로).
  - H: 같은 프레임 UI 연속 play → latest-wins(A 미시작, B만 providerB로 시작)(리뷰 P1).
  - I: Manager 연속 교체 → 폐기된 A 미실행(`dialogue_started` 1회, 외부 Say ["B"],
    providerA 접근 0회), 최종 활성 UI가 providerB 유지(리뷰 P1·2차 P1).
- 기존 DialogueTool 회귀(`dt004_step1~4`, integration 포함) 전부 ALL PASS.
- dt005 Step 1~4 회귀 ALL PASS. headless editor load 성공.

**완료 조건 대응**
- Dialogue runtime이 save/PlayerData를 알지 않음(코드에 state용 `/root` 조회 없음; 주석/예시만 존재).
- DialoguePlayer가 `/root`를 직접 조회하지 않음(상태는 주입 provider로만).
- provider 미지정(A)과 fake read provider 주입(B) 테스트 가능.
- 기존 Variable/Expression/Branch 동작 유지(D/E + dt004 회귀).

**Step 5 코드 리뷰 수정 (2026-06-12)**
- [P1] DialogueUI.play가 provider를 즉시 공유 필드에 저장하고 resource만 deferred에 캡처해, 같은
  프레임 연속 play 시 먼저 큐된 시작이 나중 provider로 평가되던 결합 깨짐 수정. resource+provider를
  한 쌍(`_pending_start`)으로 묶고 단일 deferred dispatcher(`_deferred_start`)로 마지막 요청만
  시작한다(latest-wins). 회귀 테스트 H(UI)·I(Manager) 추가.
- [P2] System 문서 "Implemented" 도입부가 "Step 2까지"로 남아 Step 3~5 내용과 모순되던 것 수정
  (현재 Step 1~5 사실로 갱신).
- 수정 후 재검증: Step 5 A~I 전부 통과(exit 0), dt004 5종·dt005 Step 1~4 회귀 통과, editor load 성공.

**Step 5 코드 리뷰 2차 수정 (2026-06-12)**
- [P1] Manager가 같은 프레임에 UI를 교체하면 폐기된 UI의 deferred 시작이 뒤늦게 실행돼 폐기된
  그래프가 시작/평가되던 문제(외부 출력은 source guard로 숨겨지지만 그래프 자체는 실행) 수정.
  `DialogueUI.cancel_pending_start()`를 추가하고 `DialogueManager._dismiss()`가 폐기 UI의 pending
  시작을 취소한다. 이제 폐기된 A는 시작/평가되지 않는다(`dialogue_started` 1회, providerA 접근 0회).
  테스트 I에 started 횟수·외부 Say·providerA 호출 0 검증 추가(FakeReadProvider에 calls 카운터).
- 수정 후 재검증: Step 5 A~I 전부 통과(exit 0), dt004 5종·dt005 Step 1~4 회귀 통과, editor load 성공.

**범위 메모 / 이월**
- State Read/Set Dialogue 노드와 ConditionEvaluator는 만들지 않았다(후속 Task).
- mutation provider는 DialoguePlayer에 주입하지 않았다(소비자 없음).
- 자기 승인하지 않는다.

### Step 6: Integration Regression and Review — 완료 (2026-06-12)

**변경 파일**
- `Assets/Script/gds/world_state/tests/dt005_step6_integration_test.gd` (+ `.tscn`): end-to-end 통합 회귀.
- `LLM_WIKI/50_Reviews/DT-005-WorldState-Review.md`: 통합 리뷰·판정 문서.

**통합 시나리오 검증**
- `dt005_step6_integration_test` → `[DT-005 Step6] ALL PASS`, exit 0. 한 store를
  default 초기화(A) → atomic batch 성공(B, 입력 순서·diff) → batch 실패 거부(C) →
  단일 set/reset 정상·타입 오류·read-only·시스템 reset(D) → export SAVE-only(E) →
  JSON snapshot 왕복 replace-load(F, SAVE 복원·SESSION 미변경) → reset_lifetime SESSION(G) →
  실제 store를 DialoguePlayer read provider로 주입(H, mutation 즉시 반영 + Say 실행) →
  snapshot_imported 발행(I)으로 묶어 검증.

**전체 회귀**
- dt005 Step 1~6 전부 ALL PASS, DialogueTool dt004 5종(integration 포함) ALL PASS, headless editor load 성공.

**판정**
- [[DT-005-WorldState-Review]]: **완료**. P0/P1 없음, accepted debt 명시.

## Out of Scope

- ConditionSet과 조건 UI
- Set/Add/Tag Dialogue 노드
- Response Selector
- DialogueHistory
- 실제 save slot/file serialization
- wildcard 또는 parameterized key
- computed state와 getter callback
- network replication
- 대규모 State Inspector

## Risks

- Variant 타입을 넓게 허용하면 snapshot 직렬화가 불안정해진다.
- key 문자열을 자유 생성하면 schema의 의미가 사라진다.
- Store가 파일 저장까지 맡으면 SaveGame 시스템과 결합된다.
- DialoguePlayer가 전역 singleton에 고정되면 테스트가 어려워진다.
- key rename 정책 없이 출시 후 key를 변경하면 세이브 호환이 깨진다.
- 수천 개 Definition을 한 inspector에서 편집하면 authoring UX와 로드 시간이 나빠질 수 있다. schema fragment와 전용 inspector는 측정 후 후속 설계한다.
- 조건식을 임의 Godot `Expression` 문자열로 확장하면 타입 검사, trace, 안전한 마이그레이션이 어려워진다. 후속 ConditionSet은 구조화된 조건 데이터와 결정론적 evaluator를 우선한다.

## Scale and Dialogue Invariants

- 조건 평가는 상태를 변경하지 않는 pure read다.
- 선택지 노출과 Response Selector는 동일 snapshot에서 결정론적으로 같은 결과를 내야 한다.
- mutation은 명시적 Effect 단계에서만 수행하고 batch 단위 diff를 남길 수 있어야 한다.
- 조건 실패는 `false`만 반환하지 않고 key, expected, actual, operator를 trace할 수 있어야 한다.
- 그래프는 상태 key를 참조하지만 default, lifetime, save format을 복제하지 않는다.
- 상태 key rename은 alias/migration 정책 전에는 금지한다.
- Dialogue-local temporary 값과 전역 World State를 섞지 않는다.

## Follow-ups

- ConditionSet + ConditionEvaluator
- State Read Data 노드
- Set/Add State Effect 노드
- State Inspector와 조건 trace
- schema migration 및 key alias
- DialogueHistory

## Related

- [[ADR-006-Typed-World-State]]
- [[World-State-System]]
- [[DialogueTool]]
