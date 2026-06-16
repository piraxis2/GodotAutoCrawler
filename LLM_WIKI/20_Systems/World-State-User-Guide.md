---
type: guide
system: WorldState
status: current
updated: 2026-06-16
---

# World State User Guide

이 문서는 DT-005/DT-006으로 구현된 `StateDefinition`, `StateSchema`, `WorldStateStore`,
`WorldStateRuntime`을 게임 코드와 DialogueTool에서 사용하는 방법을 설명한다. 타입·snapshot 결정은
[[ADR-006-Typed-World-State]], 런타임 lifecycle은 [[ADR-007-WorldState-Runtime-Lifecycle]], 현재 구현
구조는 [[World-State-System]]을 참고한다.

## 1. 현재 사용할 수 있는 범위

현재 구현된 기능:

- 타입이 지정된 상태 key 선언과 schema 검증
- 단일 상태 조회, 변경, default 복원
- SAVE/SESSION lifetime 분리와 lifetime 전체 초기화
- 여러 상태의 atomic batch 변경
- JSON 호환 snapshot export/import
- `/root/WorldState` Store와 `/root/WorldStateRuntime` lifecycle coordinator autoload
- 새 게임 default 초기화, transactional snapshot 복원, SESSION lifecycle
- 외부 SaveGame 계층용 `capture_world_state`/`restore_world_state` adapter
- `WorldStateStore`를 Dialogue read provider로 주입
- 변경 및 snapshot 관련 signal

구조화 조건 평가도 사용할 수 있다(DT-007, 아래 20절): `ConditionSet`/`ConditionValidator`/
`ConditionEvaluator`로 ALL/ANY/NOT 트리를 작성·검증·평가한다.

아직 구현되지 않은 기능:

- Dialogue 그래프의 State Read Data 노드
- Response Selector
- 실제 save slot 및 파일 관리
- schema version migration과 key rename alias
- full int64 snapshot

현재 `world_state_store.tscn`은 유효한 `examples/world_state_schema_example.tres`(예제 Schema)를 참조하며
`WorldState` autoload로 등록돼 있다. 게임 schema는 호스트가 소유하고 이 example을 교체한다(ADR-011 D5). 게임 부팅 직후 Store는 ready지만, gameplay를 시작하려면 `WorldStateRuntime`에서
`start_new_game()` 또는 `restore_world_state()`가 성공해 session-ready가 되어야 한다.

## 2. 핵심 개념

### StateDefinition

상태 하나의 계약이다.

| 필드 | 의미 |
| --- | --- |
| `key` | 상태를 식별하는 canonical key |
| `value_type` | BOOL, INT, FLOAT, STRING, STRING_NAME 중 하나 |
| `default_value` | 초기값과 reset 대상 값 |
| `lifetime` | SAVE 또는 SESSION |
| `writable` | gameplay의 `set_value`/batch 변경 허용 여부 |
| `description` | 제작자용 설명 |
| `tags` | 검색·분류용 태그. 현재 runtime 정책에는 영향 없음 |

### StateSchema

여러 Definition을 묶고 key 형식, 중복, 타입, lifetime, version을 검증한다. 하나라도 잘못되면
전체 Schema가 invalid이며 lookup을 부분 공개하지 않는다.

### WorldStateStore

검증된 Schema를 runtime 계약으로 compile하고 현재 값을 보관한다. 초기화 후에는 mutable한
Schema를 다시 읽지 않는다. Schema 변경을 Store에 반영하려면 `initialize()`를 다시 호출해야 하며,
이때 기존 runtime 값은 모두 새 default로 초기화된다.

### WorldStateRuntime

새 게임과 load의 초기화 순서를 소유하는 coordinator다. Store ready와 session-ready를 구분하며,
호환되지 않는 snapshot은 Store를 초기화하기 전에 거부해 기존 상태를 보존한다. 파일과 slot은 직접
다루지 않고 외부 SaveGame 계층에 capture/restore adapter만 제공한다.

## 3. Key 작성 규칙

key는 다음 정규식을 만족해야 한다.

```text
^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$
```

규칙:

- 소문자 영문자로 시작한다.
- 각 segment에는 소문자, 숫자, underscore만 사용한다.
- dot으로 구분한 segment가 최소 두 개 있어야 한다.
- runtime에서 동적으로 key를 조합하지 않는다.
- migration 정책이 생기기 전에는 출시된 key 이름을 변경하지 않는다.

권장 namespace:

```text
quest.main.stage
quest.blacksmith.completed
actor.noabel.affinity
actor.noabel.mood
faction.merchant.reputation
player.gold
world.region.name
dialogue.blacksmith.first_met
```

잘못된 예:

```text
stage                 # segment가 하나
Quest.main.stage      # 대문자
quest..stage          # 빈 segment
quest.main-stage      # hyphen
1quest.main.stage     # 숫자로 시작
```

## 4. 타입과 Default 규칙

암시적 변환은 허용하지 않는다. `typeof(default_value)`와 선언 타입이 정확히 같아야 한다.

| State 타입 | 올바른 default | 잘못된 예 |
| --- | --- | --- |
| BOOL | `false` | `0`, `null` |
| INT | `0` | `0.0`, `"0"` |
| FLOAT | `0.0` | `0` |
| STRING | `""` | `&""` |
| STRING_NAME | `&"idle"` | `"idle"` |

추가 숫자 제약:

- INT는 `-9007199254740991`부터 `9007199254740991`까지만 허용한다.
- FLOAT는 `INF`, `-INF`, `NAN`을 허용하지 않는다.
- 이 제약은 default, runtime set, batch, snapshot import 모두에 적용된다.

## 5. 코드로 Schema 만들기

테스트, 프로토타입, 동적 조립 코드에서는 다음처럼 만든다.

```gdscript
func make_definition(
        key: StringName,
        value_type: StateDefinition.StateValueType,
        default_value: Variant,
        lifetime: StateDefinition.StateLifetime = StateDefinition.StateLifetime.SAVE,
        writable: bool = true
    ) -> StateDefinition:
    var definition := StateDefinition.new()
    definition.key = key
    definition.value_type = value_type
    definition.default_value = default_value
    definition.lifetime = lifetime
    definition.writable = writable
    return definition


func make_schema() -> StateSchema:
    var definitions: Array[StateDefinition] = []
    definitions.append(make_definition(
        &"quest.main.stage",
        StateDefinition.StateValueType.INT,
        0
    ))
    definitions.append(make_definition(
        &"actor.noabel.affinity",
        StateDefinition.StateValueType.INT,
        0
    ))
    definitions.append(make_definition(
        &"dialogue.blacksmith.first_met",
        StateDefinition.StateValueType.BOOL,
        false,
        StateDefinition.StateLifetime.SESSION
    ))
    definitions.append(make_definition(
        &"world.locked",
        StateDefinition.StateValueType.BOOL,
        true,
        StateDefinition.StateLifetime.SAVE,
        false
    ))

    var schema := StateSchema.new()
    schema.schema_version = 1
    schema.definitions = definitions
    return schema
```

Schema를 사용하기 전에 구조화된 validation 결과를 확인할 수 있다.

```gdscript
var schema := make_schema()
var result := schema.validate()

if not result["valid"]:
    for error in result["errors"]:
        push_error("[%s] index=%s key=%s: %s" % [
            error["code"],
            error["index"],
            error["key"],
            error["message"],
        ])
    return

print("registered keys: ", schema.keys())
```

대표 validation code:

- `schema_version_invalid`
- `definition_null`
- `value_type_invalid`
- `lifetime_invalid`
- `key_empty`
- `key_invalid_format`
- `key_duplicate`
- `default_type_mismatch`

## 6. Editor에서 Schema Resource 만들기

현재 부팅용 예제 Schema는 다음 경로에 있다(DT-011 Step 3에서 `world_state/`에서 이동·개명, uid 보존).
게임용 Schema는 호스트가 별도로 작성해 이 example을 교체한다(ADR-011 D5).

```text
res://addons/dialogtool/examples/world_state_schema_example.tres
```

bootstrap key는 다음과 같다. 이 목록은 런타임 통합을 증명하는 최소 placeholder이며 제품 상태가
확정되면 같은 규칙으로 확장한다.

| key | type | default | lifetime | writable |
| --- | --- | --- | --- | --- |
| `quest.main.stage` | INT | `0` | SAVE | true |
| `actor.example.affinity` | INT | `0` | SAVE | true |
| `player.health` | FLOAT | `100.0` | SAVE | true |
| `player.display_name` | STRING | `""` | SAVE | true |
| `world.build.channel` | STRING_NAME | `&"dev"` | SAVE | false |
| `session.intro.seen` | BOOL | `false` | SESSION | true |

1. FileSystem에서 사용할 폴더를 선택한다.
2. `New Resource...`에서 `StateSchema`를 생성하고 `.tres`로 저장한다.
3. `schema_version`을 1 이상으로 설정한다.
4. `definitions` 배열에 `StateDefinition` Resource를 추가한다.
5. 각 Definition의 key, type, default, lifetime, writable을 설정한다.
6. 특히 INT/FLOAT와 String/StringName의 default 타입을 정확히 맞춘다.
7. 게임용 Store Node의 `schema` export 필드에 이 `.tres`를 할당한다.

Schema가 invalid면 Store의 `_ready()`에서 다음 오류가 발생한다.

```text
WorldStateStore: schema is invalid; store not ready
```

이때 Store를 수정하기 전에 `schema.validate()["errors"]`를 출력해 어떤 Definition이 실패했는지
확인한다.

## 7. Store 생성과 초기화

### 게임 런타임에서 사용

일반 게임 코드에서는 Store를 새로 만들지 않고 autoload를 사용한다.

```gdscript
var world_state: WorldStateStore = WorldState

if not WorldStateRuntime.is_store_ready():
    push_error("World State Store is not ready")
    return
```

Store ready는 Schema와 default 계약이 준비됐다는 뜻이다. 실제 gameplay 진입 전에는 새 게임 또는
load를 완료해야 한다.

```gdscript
func begin_new_game() -> bool:
    var report: Dictionary = WorldStateRuntime.start_new_game()
    if not report["ok"]:
        push_error("New game World State failed: %s" % report.get("reason", "unknown"))
        return false
    assert(WorldStateRuntime.is_session_ready())
    return true
```

`project.godot`에는 `WorldState`가 먼저, `WorldStateRuntime`이 다음 순서로 등록돼 있다. coordinator는
부팅 중 Store를 다시 초기화하지 않는다.

### 테스트에서 수동 초기화

```gdscript
var world_state := WorldStateStore.new()
world_state.schema = make_schema()

if not world_state.initialize():
    push_error("World State initialization failed")
    return

assert(world_state.is_store_ready())
```

### 테스트 SceneTree에 Node로 추가

`WorldStateStore._ready()`는 자동으로 `initialize()`를 호출한다. 따라서 Node를 tree에 추가하기 전에
Schema를 할당한다.

```gdscript
var world_state := WorldStateStore.new()
world_state.schema = make_schema()
add_child(world_state) # _ready()에서 initialize()

assert(world_state.is_store_ready())
```

테스트에서 이미 tree에 들어간 Store에 Schema를 나중에 할당했다면 명시적으로 `initialize()`를 호출한다.

```gdscript
world_state.schema = replacement_schema
var initialized := world_state.initialize()
```

주의: 재초기화는 기존 값을 보존하는 migration이 아니다. 새 계약을 compile하고 모든 값을 새
default로 다시 채운다.

## 8. 상태 읽기

key 존재 여부를 먼저 확인하거나 fallback read를 사용하는 것이 안전하다.

```gdscript
if world_state.has_key(&"quest.main.stage"):
    var stage: int = world_state.get_value(&"quest.main.stage")

var affinity: int = world_state.try_get_value(&"actor.noabel.affinity", 0)
var unknown = world_state.try_get_value(&"actor.unknown.affinity", -1)
```

- `get_value()`는 not-ready 또는 미등록 key에서 빨간 `push_error` 로그를 남기고 `null`을 반환한다.
- `try_get_value()`는 같은 상황에서 로그 없이 fallback을 반환한다.
- provider API에서는 `has_state`, `read_state`, `try_read_state`가 각각 대응한다.

## 9. 단일 상태 변경

```gdscript
var error := world_state.set_value(&"quest.main.stage", 2)
if error != OK:
    push_error("State change failed: %s" % error_string(error))
```

반환 코드:

| 코드 | 의미 |
| --- | --- |
| `OK` | 적용됐거나 이미 같은 값 |
| `ERR_UNAVAILABLE` | Store가 ready가 아님 |
| `ERR_BUSY` | `value_changed` 알림 중 재진입 변경 |
| `ERR_DOES_NOT_EXIST` | 미등록 key |
| `ERR_UNAUTHORIZED` | `writable=false`인 key를 gameplay에서 변경 |
| `ERR_INVALID_DATA` | 타입 불일치 또는 숫자 도메인 위반 |

같은 값을 다시 설정하면 `OK`지만 `value_changed`는 발생하지 않는다.

다음 호출은 모두 거부된다.

```gdscript
world_state.set_value(&"quest.main.stage", "2") # INT에 String
world_state.set_value(&"player.hp", 10)          # FLOAT에 INT
world_state.set_value(&"world.locked", false)   # read-only
world_state.set_value(&"unknown.key", true)     # 미등록
```

## 10. Reset

단일 key를 compile 당시 default로 되돌린다.

```gdscript
var error := world_state.reset_value(&"quest.main.stage")
```

`reset_value()`는 시스템 작업이므로 read-only key도 reset할 수 있다.

lifetime 전체를 초기화할 수도 있다.

```gdscript
world_state.reset_lifetime(StateDefinition.StateLifetime.SESSION)
```

해당 lifetime의 모든 값이 먼저 default로 반영되고, 변경된 key의 `value_changed`가 발생한 뒤
`state_reset(lifetime)`이 한 번 발생한다.

게임의 SESSION lifecycle은 `WorldStateRuntime`이 소유한다. SESSION은 `start_new_game()`과 성공한
`restore_world_state()`에서만 default로 시작하며, scene 교체나 Dialogue 종료 때 수동 reset하지 않는다.
`reset_lifetime(SESSION)` 직접 호출은 테스트·관리 도구처럼 명시적으로 전체 SESSION 초기화가 필요한
경우에만 사용한다.

## 11. Atomic Batch

한 선택이나 Effect가 여러 상태를 함께 바꿔야 하면 단일 set을 반복하지 말고 batch를 사용한다.

```gdscript
var report := world_state.apply_batch([
    {"key": &"quest.main.stage", "value": 3},
    {"key": &"actor.noabel.affinity", "value": 10},
])

if not report["applied"]:
    for error in report["errors"]:
        push_error("batch[%s] %s: %s" % [
            error["index"],
            error["key"],
            error["reason"],
        ])
    return

for change in report["diff"]:
    print("%s: %s -> %s" % [change["key"], change["old"], change["new"]])
```

batch 규칙:

- change 형식은 `{key, value}`다.
- key는 StringName 또는 String만 가능하다.
- 모든 항목을 먼저 검증한다.
- 오류가 하나라도 있으면 아무 값도 바꾸지 않는다.
- 같은 key를 두 번 넣으면 전체 batch가 실패한다.
- read-only key는 gameplay batch에서 변경할 수 없다.
- 성공하면 모든 값을 먼저 반영한 뒤 입력 순서로 signal을 발행한다.
- 같은 값은 성공하지만 diff와 signal에서 제외된다.

오류 reason:

- `store_not_ready`
- `store_busy`
- `malformed_change`
- `unknown_key`
- `read_only`
- `type_mismatch`
- `out_of_domain`
- `duplicate_key`

## 12. Signal 구독

```gdscript
func connect_world_state_signals(world_state: WorldStateStore) -> void:
    world_state.value_changed.connect(_on_world_state_changed)
    world_state.state_reset.connect(_on_world_state_reset)
    world_state.snapshot_imported.connect(_on_snapshot_imported)


func _on_world_state_changed(key: StringName, old_value: Variant, new_value: Variant) -> void:
    print("state changed: %s, %s -> %s" % [key, old_value, new_value])


func _on_world_state_reset(lifetime: StateDefinition.StateLifetime) -> void:
    print("lifetime reset: ", lifetime)


func _on_snapshot_imported(report: Dictionary) -> void:
    print("snapshot report: ", report)
```

lifecycle 성공/실패는 coordinator signal로 구독한다.

```gdscript
func _ready() -> void:
    WorldStateRuntime.world_state_ready.connect(_on_world_state_ready)
    WorldStateRuntime.world_state_failed.connect(_on_world_state_failed)


func _on_world_state_ready(mode: StringName, report: Dictionary) -> void:
    print("World State session ready: ", mode, " ", report)


func _on_world_state_failed(mode: StringName, report: Dictionary) -> void:
    push_error("World State lifecycle failed: %s %s" % [mode, report])
```

`value_changed` callback 안에서 Store를 다시 변경하지 않는다. 현재 정책은 callback 중
`set_value`, reset, import, batch, initialize를 `ERR_BUSY` 또는 `store_busy`로 거부한다. 후속 변경이
필요하면 callback이 끝난 뒤 별도 transaction으로 실행한다.

## 13. Snapshot 저장

Store와 coordinator는 파일이나 save slot을 관리하지 않는다. 외부 SaveGame 계층은
`WorldStateRuntime.capture_world_state()`로 SAVE-only snapshot을 얻어 JSON으로 기록한다.

```gdscript
func save_world_state(path: String) -> Error:
    var snapshot: Dictionary = WorldStateRuntime.capture_world_state()
    if snapshot.is_empty():
        return ERR_UNAVAILABLE

    var file := FileAccess.open(path, FileAccess.WRITE)
    if file == null:
        return FileAccess.get_open_error()
    file.store_string(JSON.stringify(snapshot))
    return OK
```

`capture_world_state()`는 Store의 기본 `export_snapshot()`을 사용하므로 SAVE 상태만 포함한다.

```gdscript
{
    "schema_version": 1,
    "values": {
        "quest.main.stage": 3,
        "actor.noabel.affinity": 10
    }
}
```

SESSION 상태를 진단 목적으로 별도 export하려면 Store API에 lifetime을 명시할 수 있지만 일반 save에는
넣지 않는다.

```gdscript
var session_dump := WorldState.export_snapshot(StateDefinition.StateLifetime.SESSION)
```

## 14. Snapshot 불러오기

```gdscript
func load_world_state(path: String) -> Dictionary:
    if not FileAccess.file_exists(path):
        return {"ok": false, "reason": "file_not_found"}

    var file := FileAccess.open(path, FileAccess.READ)
    if file == null:
        return {"ok": false, "reason": "file_open_failed"}

    var parsed: Variant = JSON.parse_string(file.get_as_text())
    if typeof(parsed) != TYPE_DICTIONARY:
        return {"ok": false, "reason": "invalid_json"}

    return WorldStateRuntime.restore_world_state(parsed)
```

`restore_world_state()`는 먼저 snapshot envelope와 schema version을 비변경 검사한다.

- malformed/version mismatch면 Store를 초기화하지 않고 기존 값과 기존 session-ready를 보존한다.
- 호환되면 Store를 default로 초기화한 뒤 SAVE replace-load를 수행한다.
- SAVE 값은 snapshot에서 복원되고 SESSION은 default로 시작한다.
- 성공 report의 `ok`는 true이며 `WorldStateRuntime.is_session_ready()`도 true다.
- 실패 시 `world_state_failed(&"load", report)`가 발생하고 성공으로 진행하지 않는다.

내부 Store import 규칙은 다음과 같다.

- snapshot에 있는 유효한 SAVE key는 복원한다.
- snapshot에 없는 SAVE key는 default로 돌아간다.
- SESSION 값은 변경하지 않는다.
- read-only SAVE key도 복원한다.
- schema version 불일치와 잘못된 최상위 구조는 전체 거부한다.
- unknown key와 SESSION key는 `ignored`에 기록한다.
- 타입 불일치는 `errors`에 기록한다.
- 모든 값을 먼저 적용한 뒤 `value_changed`를 발행한다.
- 종료 경로마다 `snapshot_imported(report)`가 발생한다.

반환 report 예:

```gdscript
{
    "mode": &"load",
    "ok": true,
    "store_ready": true,
    "import": {
        "applied": ["quest.main.stage"],
        "ignored": [],
        "errors": []
    }
}
```

게임 load에서는 `WorldState.import_snapshot()`을 직접 호출하지 않는다. 직접 import는 SESSION 초기화와
session-ready 전환을 수행하지 않으므로 Store 단위 테스트나 별도 진단 도구에만 사용한다.

## 15. DialogueTool에 주입

Dialogue에는 State Condition 노드(DT-008)와 StateSet/StateAdd Effect 노드(DT-009)가 있다. `WorldState`(또는 임의 read provider)를
`DialogueManager.play`에 넘기면 `state_condition` Data 노드가 그 provider로 `ConditionSet`을 평가해
Branch와 조건부 Choice를 제어한다.

```gdscript
var dialogue := load("res://Dialogue/example.tres") as DialogueGraphResource
DialogueManager.play(dialogue, WorldState)
```

`state_condition` 노드에 `ConditionSet` `.tres`를 지정하고 boolean output을 Branch 조건 입력이나 Choice
항목별 Data 입력에 연결한다. 작성 방법은 [[DialogueTool-User-Guide]] State Condition / 조건부 선택지 절,
조건 평가 계약은 위 Condition 절을 참고한다.

StateSet/StateAdd Effect까지 실행하려면 mutation provider도 세 번째 인자로 넘긴다. 같은 `WorldState`를
read/mutation provider로 함께 넘길 수 있지만, read provider가 mutation provider로 자동 승격되지는 않는다.

```gdscript
DialogueManager.play(dialogue, WorldState, WorldState)
```

전달 경로:

```text
DialogueManager.play(resource, WorldState, WorldState)
  -> DialogueUI.play(resource, WorldState, WorldState)
  -> DialoguePlayer.set_read_state_provider(WorldState)
  -> DialoguePlayer.set_mutation_state_provider(WorldState)
```

`WorldStateStore`는 다음 read provider API를 구현한다.

```gdscript
WorldState.has_state(&"quest.main.stage")
WorldState.read_state(&"quest.main.stage")
WorldState.try_read_state(&"unknown.key", -1)
```

mutation provider facade도 존재한다.

```gdscript
WorldState.set_state(&"quest.main.stage", 2)
WorldState.apply_state_batch([
    {"key": &"quest.main.stage", "value": 3},
])
WorldState.add_state(&"actor.example.affinity", 1)
```

DialogueTool의 StateSet은 `apply_state_batch`를, StateAdd는 `add_state`를 사용한다. StateAdd는 INT/FLOAT key만
지원하고, int↔float 암시 변환은 없다. provider 누락, read-only, type mismatch, out-of-domain 실패는
`state_mutation_evaluated` report로 관찰되며 Flow는 계속된다.

## 16. Read-only 사용 기준

`writable=false`는 gameplay 코드의 직접 set과 batch를 막는다. 다음 시스템 작업은 허용된다.

- `reset_value`
- `reset_lifetime`
- `import_snapshot`

파생값, 시스템 소유 flag, 외부 계산 결과처럼 일반 gameplay가 임의 변경하면 안 되는 key에 사용한다.
보안 경계는 아니며, 모든 코드 실행을 막는 접근 제어 기능으로 해석하지 않는다.

## 17. 오류 로그 해석

Godot의 `push_error()`는 Output에 빨간 오류로 표시된다. 테스트는 실패 경로가 올바르게 거부되는지
확인하기 위해 이 로그를 의도적으로 발생시킨다.

예:

```text
WorldStateStore: set_value type mismatch on key 'quest.main.stage'
WorldStateStore: set_value denied on read-only key 'world.locked'
```

`dt005_step6_integration_test.tscn` 실행 중 위 로그가 나타나더라도 마지막에 다음이 있으면 정상이다.

```text
PASS D.set_type -> 30
PASS D.set_readonly -> 4
[DT-005 Step6] ALL PASS
```

반대로 일반 게임 실행에서 `schema is invalid; store not ready`가 나오면 정상 로그가 아니다. 할당된
Schema의 validation 오류를 확인해야 한다.

## 18. 수동 점검 순서

1. 부팅 후 `/root/WorldState`와 `/root/WorldStateRuntime`이 각각 하나만 존재하는지 확인한다.
2. `WorldStateRuntime.is_store_ready()`가 true이고 `is_session_ready()`는 아직 별도 상태인지 확인한다.
3. `start_new_game()` 성공 후 session-ready와 bootstrap default를 확인한다.
4. 정상 set/reset과 거부되는 타입/read-only 변경을 확인한다.
5. 여러 key batch의 성공과 오류 batch 전체 거부를 확인한다.
6. `capture_world_state()` 결과에 SAVE key만 있고 SESSION key가 없는지 확인한다.
7. JSON stringify/parse 후 `restore_world_state()`로 SAVE 타입·값이 복원되고 SESSION은 default인지 확인한다.
8. malformed/version mismatch 복원이 실패하며 기존 값이 보존되는지 확인한다.
9. transient scene 교체 전후에 같은 autoload 인스턴스와 값이 유지되는지 확인한다.
10. `DialogueManager.play(dialogue, WorldState)` 후 Player read provider가 Store 값을 읽는지 확인한다.
11. 자동 테스트의 최종 `ALL PASS`와 headless editor import 성공을 확인한다.

통합 기준 테스트:

```text
res://addons/dialogtool/world_state/tests/dt005_step6_integration_test.tscn
res://addons/dialogtool/world_state/tests/dt006_step5_integration_test.tscn
```

세부 테스트:

- Step 1: Schema validation과 `.tres` 왕복
- Step 2: Store read/write/reset과 계약 compile
- Step 3: lifetime, snapshot, 숫자 도메인, 재진입
- Step 4: atomic batch
- Step 5: Dialogue provider seam
- Step 6: end-to-end 통합

DT-006 런타임 통합 테스트:

- Step 1: bootstrap Schema와 Store scene
- Step 2: `WorldState` autoload와 boot readiness
- Step 3: new/load lifecycle, transactional restore, 재진입·Store 교체
- Step 4: capture/restore adapter와 JSON 왕복
- Step 5: 실제 autoload + scene churn + Dialogue provider end-to-end

## 19. 운영 규칙

- 상태를 추가할 때 먼저 Schema에 Definition을 등록한다.
- key 문자열을 여러 코드에 직접 흩뿌리지 말고 도메인별 상수 또는 중앙 참조를 고려한다.
- 여러 상태가 하나의 논리적 결과라면 batch를 사용한다.
- gameplay 시작은 `WorldStateRuntime.is_session_ready()`가 true일 때만 허용한다.
- 새 게임은 `start_new_game()`, load는 `restore_world_state()` 단일 진입점을 사용한다.
- save file은 Store/coordinator가 아니라 SaveGame 계층이 관리하고, capture/restore adapter만 호출한다.
- SESSION은 save에 넣지 않는다.
- SESSION은 새 게임/load에서만 default로 시작하며 scene/Dialogue 종료 때 reset하지 않는다.
- 조건 평가는 read-only로 유지하고 mutation은 명시적 Effect 단계에서 수행한다.
- schema version을 올리기 전에 migration 정책을 먼저 설계한다.
- 출시 이후 key rename은 alias/migration 없이 수행하지 않는다.

## 20. 구조화 조건 평가 (DT-007)

World State 위에서 ALL/ANY/NOT 조건을 데이터로 작성하고, 주입한 read provider로 결정론적으로
평가한다. 설계는 [[ADR-008-Structured-Condition-Evaluation]], 검증·계약은 [[DT-007-Condition-Review]].

### Resource 모델

- `StateCondition`(leaf): `key`(등록된 state key), `operator`(EQUAL/NOT_EQUAL/LESS/LESS_EQUAL/GREATER/
  GREATER_EQUAL), `expected_value`(bool/int/float/String/StringName literal).
- `ConditionGroup`: `logic`(ALL/ANY/NOT) + `children: Array[ConditionClause]`. NOT은 child 정확히 1개.
- `ConditionSet`: `root`(leaf 또는 group) + `description`/`tags`. `.tres`로 저장·재로드된다.
- `ConditionClause`는 `@abstract` base다. Inspector에서 직접 생성하지 않고 StateCondition/ConditionGroup만 쓴다.

### 코드로 작성하고 평가하기

```gdscript
func make_gate() -> ConditionSet:
    var stage := StateCondition.new()
    stage.key = &"quest.main.stage"
    stage.operator = StateCondition.Operator.GREATER_EQUAL
    stage.expected_value = 3

    var seen := StateCondition.new()
    seen.key = &"session.intro.seen"
    seen.operator = StateCondition.Operator.EQUAL
    seen.expected_value = true

    var not_seen := ConditionGroup.new()
    not_seen.logic = ConditionGroup.Logic.NOT
    not_seen.children = [seen] as Array[ConditionClause]

    var root := ConditionGroup.new()
    root.logic = ConditionGroup.Logic.ALL
    root.children = [stage, not_seen] as Array[ConditionClause]

    var set := ConditionSet.new()
    set.root = root
    return set


func check_gate() -> void:
    var report := ConditionEvaluator.evaluate(make_gate(), WorldState)
    if not report["valid"]:
        for e in report["errors"]:
            push_error("[%s] %s %s: %s" % [e["code"], e["path"], e["key"], e["message"]])
        return
    if report["passed"]:
        print("gate open")
```

`read_provider`는 `has_state(key: StringName) -> bool`와 `read_state(key: StringName) -> Variant`를
제공해야 한다. `WorldState`(=`/root/WorldState`, `WorldStateStore`)가 그대로 만족한다.

### 결과 구조

```gdscript
{
    "passed": bool,                # valid && 논리 결과
    "valid": bool,                 # errors.is_empty()
    "errors": [{code, path, key, message}],
    "trace": { ... },              # 노드 트리(아래)
    "read_count": int,             # 읽은 unique key 수(miss 포함)
}
```

- `valid==false`면 `passed`는 항상 false.
- trace leaf: `{kind:"state", path, key, operator, expected, actual, passed}`(에러 leaf는 `actual:null`,
  `error:<code>`). group: `{kind:"group", logic, path, passed, children}`. root path=`[]`.
- operator 문자열 `equal|not_equal|less|less_equal|greater|greater_equal`, logic `all|any|not`은 안정 계약.

### 규칙과 fail-closed

- strict 비교: 양쪽 `typeof()` 정확 일치. int↔float, String↔StringName 암시적 변환 없음. ordered(LESS 등)는
  int/float만.
- 2단계 평가: 구조 검증(provider read 0) 통과 후에만 값을 읽는다. 같은 key는 호출 내 1회 read.
- 다음은 모두 `valid=false`, `passed=false`로 fail-closed: 빈 group, NOT arity 위반, cycle/alias, depth>64,
  node>4096, 빈/잘못된 key, 잘못된 operator/logic, 미지원 expected, 미등록 key(`state_missing`), 타입
  불일치(`actual_type_mismatch`), provider null/계약 위반(`provider_missing`/`provider_contract_invalid`).
- ANY가 논리적으로 true여도 형제에 오류가 있으면 통과하지 않는다. NOT/ANY는 errored child를 pass로 바꾸지 않는다.
- evaluator는 pure read다. Store를 변경하지 않고 mutation/signal에 의존하지 않는다.

### 검증 테스트

```text
res://addons/dialogtool/world_state/condition/tests/dt007_step1_validation_test.tscn   # 구조 검증
res://addons/dialogtool/world_state/condition/tests/dt007_step2_evaluator_test.tscn    # fake provider 평가
res://addons/dialogtool/world_state/condition/tests/dt007_step3_store_integration_test.tscn # 실제 Store
res://addons/dialogtool/world_state/condition/tests/dt007_step4_e2e_test.tscn          # .tres+trace e2e
```

## Related

- [[World-State-System]]
- [[ADR-006-Typed-World-State]]
- [[ADR-007-WorldState-Runtime-Lifecycle]]
- [[ADR-008-Structured-Condition-Evaluation]]
- [[DT-005-StateSchema-WorldStateStore]]
- [[DT-005-WorldState-Review]]
- [[DT-006-WorldState-Runtime-Integration]]
- [[DT-006-WorldState-Runtime-Review]]
- [[DT-007-ConditionSet-ConditionEvaluator]]
- [[DT-007-Condition-Review]]
- [[DialogueTool-User-Guide]]
