---
id: ADR-011
type: decision
status: accepted
date: 2026-06-16
system: DialogueTool, WorldState
---

# DialogueWorldState Addon Packaging

## Context

DT-008/DT-009 이후 `addons/dialogtool` 제품 코드는 WorldState condition 서브시스템에
직접 의존한다. 코드 대조로 확인한 결합 표면:

- 제품 코드 결합은 condition 하나뿐이고 모두 `class_name` 참조다(경로 독립):
  - `dialogue_player.gd`: `ConditionSet`, `ConditionEvaluator.evaluate()`
  - `world_state_condition_def.gd`: `@export var condition_set: ConditionSet`
  - `world_state_condition_node.gd`, `condition_set_picker.gd`: `ConditionSet`
- condition은 다시 `StateSchema.KEY_PATTERN` + `StateDefinition` enum에 의존한다
  (`condition_validator.gd`).
- mutation은 duck-type(`apply_state_batch`/`add_state`)으로 decoupled,
  read/store/runtime은 provider 주입으로 decoupled다. `WorldStateStore`/`StateSchema`/
  `WorldStateRuntime`는 addon 제품 코드에서 직접 참조되지 않는다(테스트에서만 등장).

WorldState 코어가 `res://Assets/Script/gds/world_state` 아래 있어 `addons/dialogtool`만
복사하면 깨진다. 핵심 기술 사실: GDScript `class_name`은 경로 독립이라 파일 이동 시 코드
참조는 무수정이고, 이동으로 깨지는 것은 `.tres`/`.tscn` `ext_resource path`, 코드 내 문자열
경로(`preload`/`load`/const), `project.godot` autoload 항목뿐이다.

## Decision

### D1. 패키징 형태 — 단일 addon 루트(dialogtool 유지)

`addons/dialogtool/`를 루트로 유지하고 WorldState를 `addons/dialogtool/world_state/`
하위모듈로 이동한다. dialogtool 자체 경로(plugin.cfg / editor_plugins enabled / debugger
plugin / main.tscn preload / icon)는 변경하지 않아 마이그레이션 표면을 최소화한다.
복사 1폴더로 동작한다는 1순위 재사용 목표를 만족한다.

Amendment: [[ADR-013-WorldCore-Umbrella-Packaging]]은 SaveGame처럼 DialogueTool 밖 core 소비자가 추가되는
장기 목표로 `addons/world_core/` umbrella root를 accepted했다. ADR-011 D1은 DT-011 완료 시점의 현재
패키징 사실로 유지하며, `world_core` 이동은 ADR-013의 migration trigger를 만족할 때 별도 Task로 수행한다.

### D2. Addon 필수 폐쇄집합

addon 자급에 필요한 WorldState 파일은 다음을 **모두** 포함한다:
`state_definition.gd`, `state_schema.gd`, `world_state_store.gd`/`.tscn`,
`world_state_runtime.gd`, `condition/*`. `condition_validator`가
`StateSchema`/`StateDefinition`에 의존하므로 코어 2파일 누락은 단독 설치 실패(P0/P1)다.

### D3. 이동(move)이며 복사 금지

같은 `class_name` 2중 등록은 프로젝트 open 실패(파싱 에러)를 일으키므로 원본 삭제를 동반한
이동만 수행한다. `.uid` 사이드카를 동반 이동해 uid 참조를 보존한다. `class_name` 코드
참조는 경로 독립이라 무수정이다. 재작성 대상:

- `project.godot` autoload 3종 path.
- `world_state_store.tscn` ext_resource path.
- **이동하는 world_state/condition 테스트 `.tscn`/`.tres`의 `ext_resource path`** —
  `world_state/tests/dt005_step1~6_*.tscn`, `dt009_step1_*.tscn`,
  `condition/tests/dt007_step1~4_*.tscn`, `dt007_spike_*.tscn` 등 17개 + `store.tscn`이
  `res://Assets/Script/gds/world_state/...`를 직접 가리킨다. 상수만 고치면 씬 로드가 실패한다.
- 코드 내 path 문자열(`SCHEMA_PATH`/`RUNTIME_SCRIPT`/`STORE_SCENE`/`CLAUSE_SCRIPT`,
  addon dt008 테스트의 `SCHEMA_PATH` 포함).
- `examples`로 옮길 `affinity_ge_10.tres`의 ext_resource path.

`world_state_schema.tres`는 이미 `uid="uid://urle8xa2dmc"`를 가지므로 이동 시 uid **보존**만
확인하면 된다(신규 부여 불필요).

### D4. Autoload 소유권·순서 — 호스트 등록

런타임 autoload(`DialogueManager`/`WorldState`/`WorldStateRuntime`)는 호스트
`project.godot`가 직접 등록한다(설치 문서 제공). `add_autoload_singleton`은 등록 순서를
보장하지 못해 ADR-007의 `WorldState`→`WorldStateRuntime` 순서를 강제할 수 없고, 에디터
플러그인 활성화만으로 런타임 autoload가 끼어드는 것은 호스트에게 놀라움이므로 플러그인이
자동 등록하지 않는다. 설정이 불필요한 에디터 유틸 `DialogueToolUtil`만 플러그인이
자동 등록한다(현행 유지). 설치 문서는 수동 `DialogueToolUtil` 항목을 넣지 않는다(이중 등록 방지).

### D5. Schema 소유권 — example만 addon

addon은 `examples/world_state_schema_example.tres`(현 6-key bootstrap을 이동·개명)만
포함한다. 게임 schema와 save slot은 호스트가 소유한다. `world_state_store.tscn`은 example
schema에 연결돼 out-of-box 부팅을 보장하고, 호스트는 autoload schema를 자기 것으로 교체한다.

### D6. SaveGame 경계 불변

addon은 `capture_world_state`/`restore_world_state` adapter에서 끝난다. 파일 IO/slot/백업은
호스트 후속 Task(DT-006 후속)다.

## Consequences

### Positive

- addon 1폴더 복사 + 문서대로 autoload 등록으로 다른 프로젝트에서 재사용 가능하다.
- 게임 schema/save가 addon 코드와 분리된다.
- `class_name` 경로 독립 덕분에 제품 코드 수정 없이 이동만으로 동작한다.
- 결합 표면이 condition 하나로 격리돼 마이그레이션 위험이 낮다.

### Negative

- "dialogtool" 폴더가 world_state를 포함한다(README로 보완).
- 호스트가 autoload 3종을 정확한 순서로 등록해야 한다(문서 의존).
- world_state 단독(대화 없이 quest만) 재사용은 하위폴더라 약간 불편하다.

## Alternatives Rejected

- **새 루트 `dialogue_state_tool/`(후보 A):** umbrella 이름은 정직하나 dialogtool 전체
  경로(plugin.cfg/editor_plugins/debugger/main.tscn/icon/모든 .tres) churn이 커 거부.
- **별도 두 addon(후보 C):** Godot이 addon 간 의존성을 강제하지 못해 "한 폴더만 복사 →
  깨짐" 문제가 잔존, 재사용 목표에 반해 거부.
- **복사 + compatibility shim:** `class_name` 중복으로 프로젝트 open이 실패해 거부(D3).
- **플러그인 전부 자동 등록:** 순서 미보장 + 게임 schema 결합으로 거부(D4).

## Review Gate

[[DT-011-DialogueWorldState-Addon-Packaging]] Step 0 설계 리뷰(2026-06-16)에서 코드 대조로
결합 표면(condition class_name 단일, mutation duck-type decoupled)을 확인하고, 후보 B /
호스트 autoload 등록 / example-only schema를 확정해 **accepted**로 전환했다.
판정: Approved after design fixes(아래 fix는 Step 1 착수 전 확정 — D2 폐쇄집합, D3
move-not-copy + path 인벤토리, D4 autoload 순서, D5 schema 분리).

## Related

- [[DT-011-DialogueWorldState-Addon-Packaging]]
- [[ADR-007-WorldState-Runtime-Lifecycle]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[ADR-010-State-Mutation-Dialogue-Effects]]
- [[ADR-013-WorldCore-Umbrella-Packaging]]
- [[DialogueTool]]
- [[World-State-System]]
