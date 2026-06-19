# DialogueTool + WorldState Addon

Godot 4.6.x (Mono) 용 대화 저작/실행 도구와 타입 안전 World State 시스템을 한 폴더로 묶은 addon이다.
`addons/world_core/` 아래 `dialogtool/` 및 `world_state/` 폴더를 복사하면 대화 에디터, 런타임, World State 코어/condition,
StateCondition/StateSet/StateAdd 노드가 함께 따라온다.

## 폴더 구조

```text
addons/world_core/
  dialogtool/                # 대화 에디터/런타임 본체
    dialoguetool.gd
    plugin.cfg
    Editor/ Node/ Resource/ RunTime/ UI/ debugger_plugin/
    examples/                # 대화 예제 리소스
      affinity_ge_10.tres
      sample_dialogues/
  world_state/               # World State (코어 + condition)
    state_definition.gd  state_schema.gd
    world_state_store.gd  world_state_store.tscn  world_state_runtime.gd
    condition/             # ConditionClause/Set/Group/Validator/Evaluator, StateCondition
    examples/
      world_state_schema_example.tres
```

## 설치 (새 프로젝트)

1. **폴더 복사**: 대상 프로젝트에 `addons/world_core/dialogtool/` 전체를 복사한다.
2. **에디터 재시작 후 `--import`**: 최초 실행 시 Godot이 uid/클래스 캐시를 생성한다.
   헤드리스라면 `godot --headless --path <project> --import`를 한 번 돌린다.
3. **플러그인 활성화**: Project Settings → Plugins → "DialogueTool" Enable.
   이때 에디터 유틸 `DialogueToolUtil` autoload는 플러그인이 **자동 등록**해 `project.godot`의 `[autoload]`에
   기록된다(GUI에서 한 번 활성화하면 영구 반영). 수동 추가 금지 — 이중 등록됨.

   > **중요(헤드리스/CI 주의):** 런타임 `dialogue_player.gd`/`dialogue_manager.gd`는 `DialogueToolUtil`
   > autoload **식별자에 parse-time 의존**한다. GUI 활성화를 한 번도 거치지 않고 직접 작성한
   > `project.godot`로 헤드리스 실행하면 `DialogueToolUtil`이 없어 `dialogue_manager.gd`가 parse error를
   > 낸다. 이 경우 아래 autoload 목록에 `DialogueToolUtil="*res://addons/world_core/dialogtool/dialoguetool_util.gd"`를
   > **함께 등록**한다(GUI 활성화로 이미 등록됐다면 중복 추가하지 말 것).

4. **런타임 autoload 수동 등록 (순서 권장)**: Project Settings → Autoload에 아래를 추가한다.

   | 이름 | 경로 |
   | --- | --- |
   | `DialogueManager` | `res://addons/world_core/dialogtool/RunTime/dialogue_manager.gd` |
   | `WorldState` | `res://addons/world_core/world_state/world_state_store.tscn` |
   | `WorldStateRuntime` | `res://addons/world_core/world_state/world_state_runtime.gd` |

   `WorldStateRuntime`는 `_ready()`에서 `/root/WorldState`를 해석하므로 **`WorldState`를 먼저 두는 것을 권장**한다.
   (Godot 4.6.3은 autoload 노드를 모두 root에 추가한 뒤 `_ready()`를 돌려 실제로는 순서가 뒤바뀌어도 store가
   해석되지만, 버전·구성에 무관한 안전을 위해 권장 순서를 따른다.) 플러그인이 런타임 autoload를 자동 등록하지
   않는 이유는 `add_autoload_singleton`이 순서를 보장하지 못하고, 에디터 활성화만으로 런타임 싱글톤이 끼어드는
   것이 호스트에게 예측 불가능하기 때문이다(ADR-011 D4, ADR-007).

   `project.godot`에 직접 적는 형태(헤드리스라면 `DialogueToolUtil`도 함께 — 위 주의 참고):

   ```ini
   [autoload]
   DialogueToolUtil="*res://addons/world_core/dialogtool/dialoguetool_util.gd"
   DialogueManager="*res://addons/world_core/dialogtool/RunTime/dialogue_manager.gd"
   WorldState="*res://addons/world_core/world_state/world_state_store.tscn"
   WorldStateRuntime="*res://addons/world_core/world_state/world_state_runtime.gd"
   ```

설치 직후 `WorldState`는 `world_state/examples/world_state_schema_example.tres`로 부팅돼 바로 동작하고,
`examples/sample_dialogues/sample_world_state_dialogue.tres`를 에디터에서 열어 구조를 확인할 수 있다.

## 게임 Schema로 교체

`world_state/examples/world_state_schema_example.tres`는 **예제**다. 실제 게임 상태 키는 호스트가 소유한다.

1. 자기 프로젝트에 게임 Schema `.tres`(`StateSchema`)를 작성한다. 작성 규칙은 아래 **Schema 작성 규칙** 참고.
2. `WorldState` autoload가 가리키는 store 씬의 `schema` 슬롯을 자기 Schema로 바꾼다. 방법 두 가지:
   - `world_state_store.tscn`의 `schema` ext_resource를 자기 `.tres`로 교체, 또는
   - store 씬을 복제해 자기 Schema에 연결하고 `WorldState` autoload를 그 씬으로 가리킨다(addon 파일 비수정).
3. SaveGame 파일/슬롯은 addon 범위 밖이다. `WorldStateRuntime.capture_world_state()` /
   `restore_world_state(snapshot)` adapter를 호스트의 저장 계층이 소비한다(아래 SaveGame 경계 참고).

### Schema 작성 규칙

`StateSchema`(`world_state/state_schema.gd`)는 `Array[StateDefinition]`을 가진다. 각
`StateDefinition`(`world_state/state_definition.gd`) 필드:

- `key: StringName` — 형식 `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$` (소문자 시작, 최소 두 segment.
  예: `quest.main.stage`, `actor.npc01.affinity`). 중복 금지.
- `value_type` — `StateDefinition.StateValueType` enum: `BOOL`(0)/`INT`(1)/`FLOAT`(2)/`STRING`(3)/
  `STRING_NAME`(4).
- `default_value` — `value_type`와 `typeof()` 정확 일치. 암시적 변환(int↔float, String↔StringName, null) 거부.
- `lifetime` — `StateDefinition.StateLifetime` enum: `SAVE`(0, snapshot 대상)/`SESSION`(1, 새 게임·load에서만
  default, 저장 안 됨).
- `writable: bool` — `false`면 gameplay write 거부(시스템 reset은 허용).

도메인 제약: INT는 JSON-safe `±(2^53-1)`, FLOAT는 finite만. 위반 시 Store가 not-ready/거부한다.
`schema.validate()`가 `{valid, errors, error_codes, key_count}`를 반환하며, 유효할 때만 lookup이 채워진다.
예제는 `world_state/examples/world_state_schema_example.tres`(6-key) 참고.

## 게임 코드에서 대화 실행

```gdscript
# read/mutation provider로 WorldState(/root/WorldState)를 그대로 주입할 수 있다.
DialogueManager.play(dialogue_resource, WorldState, WorldState)
```

- read provider 미주입 시 조건은 fail-closed(false), mutation provider 미주입 시 `state_*` Effect는
  `provider_missing`으로 무시되고 Flow는 계속된다. read 권한은 mutation으로 자동 승격되지 않는다.

### Provider 계약

`DialoguePlayer`는 `/root`를 직접 조회하지 않고 주입된 provider로만 상태를 읽고/바꾼다. 두 provider는
별개이며 `WorldStateStore`(=`WorldState` autoload)가 둘 다 만족한다(duck-type).

- **read provider** (`state_condition` 평가, Branch/Choice 조건): `has_state(key) -> bool`,
  `read_state(key) -> Variant`, `try_read_state(key, fallback) -> Variant`.
- **mutation provider** (`state_set`/`state_add` Effect):
  - `apply_state_batch(changes: Array[Dictionary]) -> Dictionary` — `state_set`이 사용. change는
    `{key, value}`, 타입은 5종(BOOL/INT/FLOAT/STRING/STRING_NAME). atomic(하나라도 실패하면 전체 거부).
  - `add_state(key: StringName, delta) -> Dictionary` — `state_add`가 사용. INT/FLOAT만, strict 타입.
  - 반환 Dictionary는 `{applied, changed, ...}` 보고형. 계약 위반(freed/arity/반환형)은 SCRIPT ERROR 없이
    `provider_contract_invalid`로 처리되고 Flow는 계속된다.

값 변환은 하지 않는다(조용한 0/false 강등 없음). 타입/도메인/read-only 위반은 Store가 strict 거부하고 값은
불변이며, 결과는 `state_mutation_evaluated`/`condition_evaluated` signal로 보고된다.

## 기존 프로젝트 마이그레이션

이전에 World State 코어를 `res://Assets/Script/gds/world_state/`(또는 다른 경로)에 두고 있었다면:

1. 해당 폴더를 `addons/world_core/world_state/`로 **이동**한다(복사 금지 — 같은 `class_name`이 두 곳에
   남으면 프로젝트 open이 실패한다). `.gd.uid` 사이드카를 동반 이동한다.
2. 경로 문자열을 일괄 재작성한다:
   - `project.godot` autoload(`WorldState`/`WorldStateRuntime`).
   - `world_state_store.tscn`과 Schema `.tres`의 `ext_resource path`.
   - 코드 내 path 상수(`SCHEMA_PATH`/`STORE_SCENE`/`RUNTIME_SCRIPT`/`CLAUSE_SCRIPT` 등)와 조건/대화
     `.tres`/`.tscn`의 `ext_resource path`.
3. `.tres`의 `uid="uid://..."`는 그대로 보존한다(신규 부여 불필요). `class_name` 코드 참조는 경로 독립이라
   수정할 필요가 없다.
4. `godot --headless --path <project> --import`를 1~2회 돌려 uid/클래스 캐시를 재생성하고 parse/script
   error 0, `class_name` 중복 0을 확인한다.

## SaveGame 경계

addon은 snapshot adapter에서 끝난다. 파일 IO/슬롯/백업은 호스트 책임이다.

- `WorldStateRuntime.start_new_game()` — SAVE+SESSION을 default로 초기화, 성공 시 session-ready.
- `WorldStateRuntime.capture_world_state() -> Dictionary` — SAVE-only JSON 호환 snapshot 반환.
- `WorldStateRuntime.restore_world_state(snapshot) -> bool` — transactional 복원(호환 안 되면 기존 상태
  보존). SESSION은 복원에서 default로 시작.
- 호스트의 저장 계층이 `capture_world_state()` 결과를 파일에 쓰고, 로드 시 `restore_world_state()`에 넘긴다.

## 참고

- 이 addon의 코드/리소스만으로 자급한다(위 사용법은 자체 포함). 아래는 **원 저장소(AutoCrawler)** 의 설계
  문서이며 addon에는 **동봉되지 않는다** — 복사 대상 프로젝트에는 존재하지 않을 수 있다.
  - 패키징 결정: `LLM_WIKI/40_Decisions/ADR-011-DialogueWorldState-Addon-Packaging.md`
  - World State 사용법/시스템: `LLM_WIKI/20_Systems/World-State-User-Guide.md`,
    `World-State-System.md`, `DialogueTool.md`
