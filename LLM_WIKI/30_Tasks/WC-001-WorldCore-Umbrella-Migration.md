---
id: WC-001
type: task
status: complete
system: WorldCore
created: 2026-06-19
updated: 2026-06-19
tags: [task, world-core, packaging, migration, dialoguetool, world-state, save-game]
---

# WC-001 WorldCore Umbrella Migration

## Goal

`DialogueTool`, `WorldState`, `SaveGame`, `SaveGame ↔ WorldState adapter`를 장기 패키징 목표인
`addons/world_core/` umbrella root 아래 sibling 모듈로 이동한다([[ADR-013-WorldCore-Umbrella-Packaging]]).

목표 구조:

```text
addons/world_core/
  dialogtool/
  world_state/
  save_game/
  save_game_world_state/
```

현재 interim 구조:

```text
addons/
  dialogtool/
    world_state/
  save_game/
  save_game_world_state/
```

이 작업은 기능 추가가 아니라 **path migration + packaging boundary 정리**다. 런타임 동작과 public contract는
그대로 유지해야 한다. 설계 리뷰 판정은 **Approved after design fixes**였고, 2026-06-19 본 문서에 필수
수정 사항을 반영해 **Approved** 상태로 전환했다.

## Context

[[ADR-011-DialogueWorldState-Addon-Packaging]]은 DT-011 완료 시점에 `addons/dialogtool/` 단일 addon root와
`dialogtool/world_state/` 하위모듈 배치를 current packaging으로 확정했다. 이후 SaveGame core가 추가되면서
[[ADR-013-WorldCore-Umbrella-Packaging]]이 더 넓은 umbrella root를 장기 목표로 accepted했다.

SG-001~SG-003에서는 SaveGame core/API 구현과 대규모 path migration을 섞지 않기 위해 `world_core` 이동을
제외했다. 현재 SaveGame 문서는 이 상태를 interim 위치로 기록하고 있으며, [[Open-Tasks]] Later에
`addons/world_core/` umbrella 패키징 이동이 남아 있다.

이번 작업은 사용자가 WorldCore 패키징 목표를 지금 우선순위로 올리기로 했으므로 별도 Task로 수행한다.

## Non-Goals

- DialogueTool, WorldState, SaveGame의 public API 변경.
- Dialogue graph/resource 포맷 변경.
- WorldState schema/key/migration 정책 변경.
- SaveGame envelope/slot/backup/report shape 변경.
- production save menu UI, autosave/quicksave, thumbnail, migration registry, Dialogue SaveEffect 구현.
- BehaviorTree editor 작업.
- `addons/behaviortree`, `addons/devconsole`, `addons/godot-vim` 이동.
- compatibility shim 또는 기존 경로에 복사본 유지. 같은 `class_name` 이중 등록 위험이 있으므로 이동은
  `git mv` 기반으로 수행한다.

## Design Decisions

### D1. Source of Truth

`addons/world_core/`가 새 설치/복사 단위다. 이동 후 현재 사실 문서는 `world_core` 아래 sibling 구조를
source of truth로 기록한다.

### D2. Module Boundaries

의존 방향은 ADR-013을 따른다.

```text
save_game
  -> domain-specific system을 참조하지 않음

world_state
  -> save_game을 참조하지 않음

dialogtool
  -> world_state condition/mutation contract 소비 가능
  -> save_game을 참조하지 않음

save_game_world_state
  -> save_game + world_state를 참조하는 integration adapter
```

### D3. WorldState 위치

`addons/dialogtool/world_state/`는 `addons/world_core/world_state/`로 이동한다. DialogueTool은 sibling
`world_state`를 소비한다. GDScript `class_name` 참조는 경로 독립이지만, `.tscn`/`.tres` `ext_resource path`,
autoload path, `load`/`preload` 문자열, README/문서/테스트 경로는 모두 갱신해야 한다.

### D3a. Schema Example Ownership

`world_state_schema_example.tres`는 WorldState bootstrap schema이므로 `dialogtool/examples`에 남기지 않고
WorldState와 함께 `addons/world_core/world_state/examples/world_state_schema_example.tres`로 이동한다.

근거: 현재 `addons/dialogtool/world_state/world_state_store.tscn`은 path-only ext_resource로
`res://addons/dialogtool/examples/world_state_schema_example.tres`를 참조한다. WorldState만 sibling으로 이동하고
schema example을 DialogueTool 아래에 남기면 `world_core/world_state/world_state_store.tscn`이
`world_core/dialogtool/examples/...`를 참조하게 되어 `world_state -> dialogtool` 역의존이 생긴다. 이는
[[ADR-013-WorldCore-Umbrella-Packaging]]의 의존 방향과 WorldState 독립 재사용 목표를 위반한다.

따라서 examples는 통째 이동하지 않고 분할한다.

- `world_state_schema_example.tres` -> `addons/world_core/world_state/examples/`
- `affinity_ge_10.tres`와 `sample_dialogues/` -> `addons/world_core/dialogtool/examples/`

Step 2는 schema example 동반 이동, `world_state_store.tscn` ext_resource 갱신,
`dialogue_debug_preview_provider.gd` `SCHEMA_PATH`, DT-006/DT-007/DT-008/DT-013 테스트 `SCHEMA_PATH` 갱신을 포함한다.

### D4. Move, Not Copy

중복 `class_name` 등록은 Godot parse/open 실패를 만들 수 있으므로 원본 유지 복사는 금지한다. `.uid` sidecar는
대상 파일과 함께 이동해 uid 기반 참조를 최대한 보존한다.

### D4a. Deterministic Path Rewrite

각 Step은 `git mv` 이후 이동한 모든 `.gd`/`.tscn`/`.tres` 안의 `res://addons/...` 문자열을 결정적으로
재작성한다. Godot은 uid가 없는 path-only `ext_resource`를 자동으로 고쳐주지 않으므로, 경로 문자열을
남겨두면 scene/resource load가 즉시 실패할 수 있다.

절차:

1. `git mv`로 파일과 `.uid` sidecar를 함께 이동한다.
2. 이동한 `.gd`/`.tscn`/`.tres` 및 관련 product resource의 `res://addons/...` 문자열을 새 경로로 치환한다.
3. `.godot/` import cache는 hand-edit하지 않고 `--import` 또는 editor 기동으로 재생성한다.
4. `--import` 후 이동한 모든 `.tscn`/`.tres`를 개별 load해 error 0을 확인한다.

### D5. Autoload Ownership

기존 정책처럼 runtime autoload는 host `project.godot`가 소유한다. migration은 `project.godot`의 경로만 새
위치로 갱신한다.

예상 autoload:

```text
DialogueManager="*res://addons/world_core/dialogtool/RunTime/dialogue_manager.gd"
WorldState="*res://addons/world_core/world_state/world_state_store.tscn"
WorldStateRuntime="*res://addons/world_core/world_state/world_state_runtime.gd"
SaveGame="*res://addons/world_core/save_game/save_game_manager.gd"       # 등록되어 있다면 경로 갱신
SaveFlow="*res://addons/world_core/save_game/save_flow.gd"               # 등록되어 있다면 경로 갱신
```

실제 등록 여부와 이름은 Step 1 inventory에서 `project.godot`와 코드로 확정한다. 현재 확인된
`DialogueToolUtil="*uid://bg2wpsw3ggue7"` autoload는 uid 기반 등록이므로 경로 문자열을 재작성하지 않는다.
대신 `dialoguetool_util.gd`와 `.uid` sidecar가 DialogueTool 이동 시 함께 이동되는지만 확인한다.

### D6. Plugin Ownership

DialogueTool editor plugin은 `addons/world_core/dialogtool/` 아래로 이동한다. `plugin.cfg`, plugin script path,
enabled plugin setting, editor-only autoload/helper path, debug Play path 문자열을 모두 갱신한다.
`project.godot`의 `[editor_plugins] enabled` 값은 `res://addons/.../plugin.cfg` path 문자열로 저장되므로 Step 3에서
새 `res://addons/world_core/dialogtool/plugin.cfg`로 재작성한다. `DialogueToolUtil` autoload는 D5처럼 uid 등록을
유지한다.

SaveGame과 WorldState는 runtime/core 모듈이며 별도 editor plugin을 만들지 않는다.

### D7. Step Order

Step 순서는 SaveGame -> WorldState -> DialogueTool 3-Step으로 유지한다. 단일 `git mv` Step으로 합치지 않는다.
각 모듈을 한 번씩만 이동하고, Step별 검증 경계를 작게 유지하기 위해서다.

### D8. Naming

디렉터리명은 ADR-013대로 lowercase `addons/world_core/`를 사용한다. 사용자-facing 문서의 표시명은
"WorldCore"로 쓴다.

## Migration Inventory

Step 1에서 아래 표를 실제 `rg` 결과로 확정한다. 이 표는 설계 기준 목록이다.

| Area | Current | Target |
| --- | --- | --- |
| DialogueTool | `addons/dialogtool/` | `addons/world_core/dialogtool/` |
| WorldState | `addons/dialogtool/world_state/` | `addons/world_core/world_state/` |
| SaveGame core | `addons/save_game/` | `addons/world_core/save_game/` |
| WorldState adapter | `addons/save_game_world_state/` | `addons/world_core/save_game_world_state/` |
| Dialogue examples | `addons/dialogtool/examples/affinity_ge_10.tres`, `addons/dialogtool/examples/sample_dialogues/` | `addons/world_core/dialogtool/examples/` |
| WorldState example schema | `addons/dialogtool/examples/world_state_schema_example.tres` | `addons/world_core/world_state/examples/world_state_schema_example.tres` |
| SaveGame tests | `addons/save_game/tests/` | `addons/world_core/save_game/tests/` |
| Adapter tests | `addons/save_game_world_state/tests/` | `addons/world_core/save_game_world_state/tests/` |
| WorldState tests | `addons/dialogtool/world_state/tests/` | `addons/world_core/world_state/tests/` |

경로 갱신 대상:

- `project.godot` autoload/plugin paths.
- `.tscn`/`.tres` `ext_resource path`.
- GDScript `load()`/`preload()`/문자열 path constants.
- C# `GD.Load` 문자열 path.
- 리포지토리 루트 product `.tres` dialogue graphs: `1.tres`~`6.tres`, `pride_and_prejudice.tres`.
  이 파일들은 `res://addons/dialogtool/Resource/...`를 ext_resource로 참조한다. uid가 함께 있더라도 stale path가
  남고 잔여 검색에 걸리므로 Step 1 inventory에서 실제 product 데이터인지 폐기 샘플인지 확인한 뒤 처리한다.
- README/User Guide 설치 경로.
- headless test scene 경로와 shell/runbook 문서.
- `LLM_WIKI` system/task/review/current-state/open-tasks 링크와 현재 사실.

## Risk Areas

- Godot `.uid`와 `ext_resource path`가 섞여 있어 단순 텍스트 치환으로는 부족할 수 있다.
- uid 없는 path-only `ext_resource`가 다수 있으므로 `git mv`만으로는 scene/resource load가 보존되지 않는다.
- DialogueTool plugin path가 바뀌면 editor plugin 활성화 설정이 깨질 수 있다.
- `world_state_store.tscn` example schema 참조가 `world_core/world_state/examples/`를 가리켜야 autoload boot가
  통과하고 WorldState가 DialogueTool에 역의존하지 않는다.
- 테스트 `.tscn`이 이동 후 오래된 `ext_resource path`를 유지하면 headless scene load가 실패한다.
- SaveGame 정적 가드 테스트가 경로 이동 후에도 domain-free 경계를 계속 검사해야 한다.
- 문서와 코드 경로가 어긋나면 이후 agent handoff가 혼란스러워진다.

## Steps

### Step 0 - Design and Inventory

목표:
- migration 범위, path map, 검증 matrix를 확정한다.

작업 범위:
- 이 Task 문서 작성.
- 실제 코드/리소스 경로 inventory 작성(`rg` 기반).
- 필요하면 설계 리뷰 후 이 문서 수정.

제외 범위:
- 제품 코드, `.tscn`, `.tres`, `project.godot` path migration.

완료 조건:
- 설계 리뷰 필수 수정 사항(P1/P2/P3)이 이 문서에 반영되어 Task status가 `approved`다.
- migration 대상/제외 대상/검증 matrix가 명확하다.

검증 방법:
- 문서/코드 정적 대조.
- `git status`로 기존 변경 분리 확인.

### Step 1 - Move SaveGame Core and WorldState Adapter

목표:
- SaveGame 관련 두 sibling을 먼저 `addons/world_core/` 아래로 이동한다.

작업 범위:
- `addons/save_game/` -> `addons/world_core/save_game/`
- `addons/save_game_world_state/` -> `addons/world_core/save_game_world_state/`
- 관련 GDScript path constants, tests, README/User Guide 경로 갱신.
- SaveGame 관련 autoload가 `project.godot`에 있으면 경로 갱신.
- 이동한 SaveGame/adapter `.gd`/`.tscn`/`.tres` 안의 `res://addons/save_game*` 문자열 결정적 치환.

제외 범위:
- DialogueTool 이동.
- WorldState를 `dialogtool/world_state`에서 분리.
- SaveGame API/report shape 변경.

완료 조건:
- SaveGame core와 adapter가 새 경로에서 parse/load된다.
- SaveGame 정적 가드가 계속 통과한다.
- WorldState adapter가 새 경로에서 기존 WorldState 위치를 참조해 통합 테스트를 통과한다.
- `--import` 후 Step 1에서 이동한 모든 `.tscn`/`.tres` 개별 load가 error 0이다.

검증 방법:
- Godot headless editor import.
- 이동한 SaveGame/adapter scene/resource 개별 load 0 error.
- SG-001 step1~4, SG-002 step1~2, SG-003 step2 관련 테스트.
- `rg "res://addons/save_game|res://addons/save_game_world_state|addons/save_game|addons/save_game_world_state"` 잔여 검사
  (문서의 historical reference는 예외로 명시).

### Step 2 - Extract WorldState to WorldCore Sibling

목표:
- WorldState를 DialogueTool 하위모듈에서 `addons/world_core/world_state/` sibling으로 분리한다.

작업 범위:
- `addons/dialogtool/world_state/` -> `addons/world_core/world_state/`
- `addons/dialogtool/examples/world_state_schema_example.tres` -> `addons/world_core/world_state/examples/world_state_schema_example.tres`
- WorldState `.tscn`/`.tres` ext_resource path, tests, autoload path, schema example 참조 갱신.
- `world_state_store.tscn`의 schema ext_resource를 새 world_state examples 경로로 갱신.
- `dialogue_debug_preview_provider.gd` `SCHEMA_PATH`와 DT-006/DT-007/DT-008/DT-013 테스트 `SCHEMA_PATH`를
  `res://addons/world_core/world_state/examples/world_state_schema_example.tres`로 갱신.
- DialogueTool의 state_condition/state_read/state_mutation 관련 path 문자열 갱신.
- SaveGame WorldState adapter 경로 갱신(새 위치의 `save_game_world_state/tests` 포함).
- 이동한 WorldState `.gd`/`.tscn`/`.tres` 안의 `res://addons/dialogtool/world_state` 문자열 결정적 치환.

제외 범위:
- DialogueTool root 자체 이동.
- WorldState API/schema 변경.
- SaveGame API 변경.

완료 조건:
- `/root/WorldState`와 `/root/WorldStateRuntime` autoload가 새 경로로 boot된다.
- DialogueTool이 sibling WorldState condition/mutation/read class를 계속 소비한다.
- SaveGame WorldState adapter가 새 경로의 runtime/store와 통합된다.
- `world_state_store.tscn`이 DialogueTool 아래 resource를 참조하지 않는다.
- `--import` 후 Step 2에서 이동/갱신한 모든 `.tscn`/`.tres` 개별 load가 error 0이다.

검증 방법:
- Godot headless editor import.
- 헤드리스 부팅에서 `/root/WorldState.is_store_ready()`가 true이고 `/root/WorldStateRuntime`이 해석된다.
- 이동한 WorldState scene/resource 개별 load 0 error.
- DT-005, DT-006, DT-007, DT-008, DT-009, DT-013 대표 회귀.
- SG-001 step3/4, SG-002 step2.
- `rg "addons/dialogtool/world_state|res://addons/dialogtool/world_state"` 잔여 검사
  (ADR/history 문서는 예외로 명시).

### Step 3 - Move DialogueTool to WorldCore

목표:
- DialogueTool editor/runtime을 `addons/world_core/dialogtool/`로 이동한다.

작업 범위:
- `addons/dialogtool/` -> `addons/world_core/dialogtool/` 단, Step 2에서 이동한 `world_state`는 제외.
- `plugin.cfg`, editor plugin enabled path, `DialogueManager`, debug preview provider, examples/tests path 갱신.
- `DialogueToolUtil` autoload는 uid 등록을 유지하고 경로 문자열로 바꾸지 않는다. `.uid` sidecar 동반 이동만 확인한다.
- `dialogtool/examples`는 `affinity_ge_10.tres`와 `sample_dialogues/`만 포함한다. schema example은 Step 2에서
  `world_state/examples`로 이동된 상태를 유지한다.
- 리포지토리 루트 product `.tres` dialogue graphs(`1.tres`~`6.tres`, `pride_and_prejudice.tres`)의
  `res://addons/dialogtool/Resource/...` ext_resource path를 새 DialogueTool 경로로 갱신하거나, Step 1
  inventory에서 폐기 샘플로 판정된 경우 별도 처리 방침을 문서화한다.
- DialogueTool 문서/README/User Guide 설치 경로 갱신.
- 이동한 DialogueTool `.gd`/`.tscn`/`.tres` 안의 `res://addons/dialogtool` 문자열 결정적 치환.

제외 범위:
- Dialogue graph format 변경.
- Dialogue runtime behavior 변경.
- WorldState/SaveGame code 이동(이미 선행 Step).

완료 조건:
- DialogueTool plugin이 새 경로에서 load된다.
- `DialogueManager.play(...)` 경로와 debug Play preview가 새 경로에서 동작한다.
- DialogueTool examples/tests가 새 경로 참조로 통과한다.
- `project.godot` `[editor_plugins] enabled`가 새 `res://addons/world_core/dialogtool/plugin.cfg`를 가리킨다.
- `DialogueToolUtil` autoload는 `*uid://bg2wpsw3ggue7` 형태를 유지한다.
- `--import` 후 Step 3에서 이동/갱신한 모든 `.tscn`/`.tres` 개별 load가 error 0이다.

검증 방법:
- Godot headless editor import.
- 에디터 1회 기동 또는 headless editor-load equivalent로 DialogueTool plugin enable, `dialoguetool_main.tscn`,
  `Node/*.tscn`, `Node/Sub/*.tscn` load 0 error 확인.
- 이동한 DialogueTool scene/resource와 루트 dialogue graph `.tres` 개별 load 0 error.
- DT-004, DT-008, DT-009, DT-010, DT-013, DT-014 대표 회귀.
- plugin path 잔여 검색.

### Step 4 - Full Matrix, Documentation, Completion Review

목표:
- WorldCore migration 전체를 완료 판정한다.

작업 범위:
- 전체 회귀 matrix 실행.
- 이동한 모든 `.tscn`/`.tres` 개별 load 결과 정리.
- `LLM_WIKI/00_Index/Current-State.md`, `Open-Tasks.md`, 관련 `20_Systems`, README/User Guide 갱신.
- 완료 리뷰 문서 작성.

제외 범위:
- 새 기능 추가.
- migration 중 발견된 별도 버그의 대규모 수정. P0/P1이 아니면 후속 Task로 분리한다.

완료 조건:
- `addons/world_core/`가 실제 source of truth.
- 기존 경로의 제품 코드/리소스 중복 없음.
- 관련 autoload/plugin/test/docs가 새 경로를 가리킨다.
- `.godot/` import cache를 hand-edit하지 않고 재생성했으며, 이동한 모든 `.tscn`/`.tres` 개별 load가 error 0이다.
- P0/P1 잔여 없음.

검증 방법:
- Godot headless editor import.
- 헤드리스 runtime boot: `/root/WorldState.is_store_ready()` true, `/root/WorldStateRuntime` 해석, DialogueManager autoload 해석.
- editor/plugin boot: DialogueTool plugin enabled, `dialoguetool_main.tscn`, `Node/*.tscn`, `Node/Sub/*.tscn` load 0 error.
- SaveGame/WorldState/DialogueTool 대표 회귀 matrix.
- path 잔여 검색.
- 코드 리뷰 및 완료 리뷰.

## Verification Matrix Draft

설계 리뷰에서 최종 확정한다.

- Import/load: Godot 4.6.3 headless editor import 0 parse/class error.
- Moved resource load: 이동한 모든 `.tscn`/`.tres` 개별 load 0 error.
- Runtime boot: 새 경로로 부팅해 `/root/WorldState.is_store_ready()` true, `/root/WorldStateRuntime` 해석,
  DialogueManager autoload 해석 확인.
- Editor/plugin boot: 에디터 1회 기동 또는 동등한 headless editor-load로 DialogueTool plugin enable,
  `addons/world_core/dialogtool/dialoguetool_main.tscn`, `Node/*.tscn`, `Node/Sub/*.tscn` load 0 error 확인.
- SaveGame:
  - `sg001_step1_core_test`
  - `sg001_step1_static_guard_test`
  - `sg001_step2_slot_store_test`
  - `sg001_step3_world_state_section_test`
  - `sg001_step4_backup_test`
  - `sg002_step1_save_flow_test`
  - `sg002_step1_static_guard_test`
  - `sg002_step2_save_flow_world_state_test`
  - `sg003_step2_host_flow_test`
- WorldState:
  - DT-005 step1~6 representative/full matrix.
  - DT-006 step1~5 representative/full matrix.
  - DT-007 condition step1~4 representative/full matrix.
- DialogueTool:
  - DT-004 effect flow regression.
  - DT-008 state condition/choice regression.
  - DT-009 state mutation regression.
  - DT-010 debug preview regression.
  - DT-013 state read regression.
  - DT-014 say paging UI regression.
- Static/path checks:
  - no duplicate product copies under old roots.
  - no stale `res://addons/dialogtool/world_state`, `res://addons/save_game`, `res://addons/save_game_world_state`
    in product code/resources after the relevant Step.
  - no stale `res://addons/dialogtool/Resource` in root product dialogue `.tres` after Step 3 unless the file is
    explicitly classified as discarded sample data.
  - historical LLM_WIKI references are allowed only when explicitly describing pre-migration history.

## Review Prompt

Use [[Design-Review-Prompt]] with:

```text
검토 대상:
- Task: LLM_WIKI/30_Tasks/WC-001-WorldCore-Umbrella-Migration.md
- ADR: LLM_WIKI/40_Decisions/ADR-013-WorldCore-Umbrella-Packaging.md
- 관련 시스템:
  - LLM_WIKI/20_Systems/DialogueTool.md
  - LLM_WIKI/20_Systems/World-State-System.md
  - LLM_WIKI/20_Systems/SaveGame-System.md

이번 세션은 설계 리뷰 전용이다. 제품 코드와 리소스는 수정하지 마라.
```

## Remaining Open Question

- Root product `.tres` dialogue graphs(`1.tres`~`6.tres`, `pride_and_prejudice.tres`)가 실제 product 데이터인지
  폐기 샘플인지 Step 1 inventory에서 분류해야 한다. stale path는 최종 상태에 남기지 않는다.

## Design Review Fix Summary

2026-06-19 설계 리뷰 판정은 **Approved after design fixes**였고, 본 문서에 아래 수정 사항을 반영해
Task status를 `approved`로 전환했다.

- **[P1] Schema example 경계 확정**: `world_state_schema_example.tres`를
  `addons/world_core/world_state/examples/`로 이동하는 것으로 결정했다. `affinity_ge_10.tres`와
  `sample_dialogues/`는 DialogueTool examples에 유지한다.
- **[P2] path-only ext_resource 재작성 절차 명시**: 각 Step에서 `git mv -> res://addons/... 결정적 치환 ->
  --import -> 이동한 모든 scene/resource 개별 load 0 error`를 완료 조건으로 추가했다. `.godot/` import cache는
  hand-edit하지 않는다.
- **[P2] root product `.tres` 인벤토리 추가**: `1.tres`~`6.tres`, `pride_and_prejudice.tres`를 path rewrite
  대상에 포함하고, 실제 product/폐기 샘플 분류를 Step 1 inventory로 명시했다.
- **[P3] DialogueToolUtil uid autoload 정책 명시**: `DialogueToolUtil="*uid://bg2wpsw3ggue7"`는 경로 재작성하지
  않고 `.uid` sidecar 동반 이동만 확인한다.
- **[P3] adapter tests 경로 갱신 명시**: Step 2에 새 위치의 `save_game_world_state/tests` 갱신을 포함했다.
- 검증 matrix에 runtime boot, editor/plugin boot, 이동한 모든 `.tscn`/`.tres` 개별 load를 추가했다.

## Remaining Assumptions

- `DialogueToolUtil` uid는 `.uid` sidecar 동반 이동으로 유지된다.
- `world_state_schema_example.tres`의 uid가 있든 없든 새 world_state examples 경로의 path를 source of truth로
  재작성한다.
- Product code의 `class_name` 참조는 경로 독립이라는 기존 ADR-011/ADR-013 전제를 유지한다.
- 구현 Step은 public API와 런타임 동작을 변경하지 않고 path migration만 수행한다.

## Related

- [[WC-001-WorldCore-Umbrella-Migration-Review]]
- [[ADR-013-WorldCore-Umbrella-Packaging]]
- [[ADR-011-DialogueWorldState-Addon-Packaging]]
- [[DialogueTool]]
- [[World-State-System]]
- [[SaveGame-System]]
- [[SG-001-SaveGame-Core-Section-System]]
- [[SG-002-SaveFlow-Facade-Metadata-Provider]]
- [[SG-003-SaveSlot-UI-Host-Integration]]

## Execution Results

### 1. 모듈 디렉터리 이동 완료
- `addons/save_game/` -> `addons/world_core/save_game/`
- `addons/save_game_world_state/` -> `addons/world_core/save_game_world_state/`
- `addons/dialogtool/world_state/` -> `addons/world_core/world_state/`
- `addons/dialogtool/` -> `addons/world_core/dialogtool/`
- `world_state_schema_example.tres` -> `addons/world_core/world_state/examples/`
- `.uid` 사이드카 파일 동반 이동 및 uid 참조 무결성 유지 완료.

### 2. 경로 문자열 결정적 치환 완료
- `project.godot` Autoload (`DialogueManager`, `WorldState`, `WorldStateRuntime`) 및 `[editor_plugins] enabled` 경로 갱신 완료.
- 루트 dialogue graph 리소스(`1.tres` ~ `6.tres`, `pride_and_prejudice.tres`)의 `res://addons/dialogtool` 구 경로 갱신 완료.
- `addons/world_core/` 내의 모든 `.gd`, `.tscn`, `.tres` 파일 내부의 구 경로 문자열 일괄 치환 완료.
- README/User Guide 및 LLM WIKI 내 최신 시스템 문서 경로 일괄 치환 완료.

### 3. 검증 결과
- **Godot Headless Import**: `Godot_v4.6.3-stable_mono_win64_console.exe --headless --path . --import` 총 2회 구동 및 임포트 캐시 무오류 갱신 완료.
- **WorldState / SaveGame 회귀 테스트 (18종)**: `run_tests.ps1` 헤드리스 구동 결과 `ALL TESTS PASSED` 달성.
- **DialogueTool 회귀 테스트 (23종)**: `run_dt_tests.ps1` 헤드리스 구동 결과 `ALL DT TESTS PASSED` 달성.
- 모든 이동 리소스(`.tscn`, `.tres`) 개별 로드 시 `SCRIPT ERROR` 및 `warning` 잔여 없음 확인.

### 4. Remaining Open Question & Assumptions 해결
- `1.tres` ~ `6.tres` 및 `pride_and_prejudice.tres` 리소스들은 폐기되지 않고 활용 중인 제품 리소스에 해당하므로, 새 경로(`res://addons/world_core/dialogtool/`)로 정규화하여 치환을 완결지었다.
- `DialogueToolUtil` 오토로드의 uid `uid://bg2wpsw3ggue7` 등록은 그대로 보존되어 정상 해석됨을 검증했다.
- 중복 클래스 등록 등으로 인한 Godot 컴파일 경고나 파스 에러는 전혀 발견되지 않았음.

