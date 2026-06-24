---
id: WC-001-Review
type: review
task: WC-001
status: completed
date: 2026-06-19
system: WorldCore, DialogueTool, WorldState, SaveGame
---

# WC-001 WorldCore Umbrella Migration Review

## 발견 사항

P0/P1/P2 발견 사항 없음.

[[WC-001-WorldCore-Umbrella-Migration]]의 3-Step(SaveGame -> WorldState -> DialogueTool) path migration을
실제 작업트리와 대조했다. 목표 구조 `addons/world_core/{dialogtool, world_state, save_game, save_game_world_state}`가
정확히 만들어졌고, 구 루트는 전부 소멸했으며, 런타임 동작과 public contract는 그대로 유지된다. 설계 리뷰에서
필수로 지정한 수정 사항(P1 schema example 경계, P2 path-only ext_resource 재작성/루트 `.tres` 인벤토리,
P3 `DialogueToolUtil` uid 유지)이 모두 코드에 반영됐다.

## 검토 내용

### 구조 / 이동
- end-state 디렉터리: `addons/world_core/` 아래 `dialogtool`, `world_state`, `save_game`, `save_game_world_state`
  sibling 4개 확인. 구 `addons/dialogtool`, `addons/save_game`, `addons/save_game_world_state`,
  `addons/dialogtool/world_state` 전부 소멸(중복 0).
- `git mv` 기반 이동으로 `.uid` sidecar가 모든 파일과 동반 이동됨(복사 아님, 중복 `class_name` 등록 없음).

### Step 1 (SaveGame core + adapter)
- SaveGame core/facade/adapter가 `world_core/save_game`·`world_core/save_game_world_state`로 이동.
- 정적 가드 `CORE_FILES`/`FACADE_FILES` 경로가 새 위치를 스캔하도록 갱신됨.
- `project.godot`에 SaveGame/SaveFlow autoload는 없어 변경 없음(설계 D5 조건부 — 해당 없음).

### Step 2 (WorldState 추출, 최고 churn)
- `world_state`가 DialogueTool 하위에서 `world_core/world_state` sibling으로 분리됨(condition/tests/examples 포함).
- **P1 경계 수정 확인**: `world_state_store.tscn` schema ext_resource가
  `res://addons/world_core/world_state/examples/world_state_schema_example.tres`를 가리킨다. WorldState가
  DialogueTool 리소스에 역의존하지 않는다(ADR-013 의존 방향 준수).
- examples 분할: schema example -> `world_state/examples`, `affinity_ge_10.tres`/`sample_dialogues/` ->
  `dialogtool/examples`.
- 모든 `SCHEMA_PATH` 상수(9곳)와 adapter `RUNTIME_SCRIPT`(2곳)가 새 경로(dialogtool -> world_state 방향)로 갱신됨.
- `project.godot`의 `WorldState`/`WorldStateRuntime` autoload 경로 갱신됨.

### Step 3 (DialogueTool 이동)
- DialogueTool editor/runtime이 `world_core/dialogtool`로 이동.
- `project.godot` `DialogueManager` autoload와 `[editor_plugins] enabled`의 `plugin.cfg` 경로 갱신됨.
- **P3 확인**: `DialogueToolUtil` autoload는 `*uid://bg2wpsw3ggue7` uid 등록을 유지(경로 재작성 안 함),
  `dialoguetool_util.gd.uid` sidecar 동반 이동.
- 리포지토리 루트 dialogue graph `.tres`(`1.tres`~`6.tres`, `pride_and_prejudice.tres`)의 ext_resource 경로가
  `world_core/dialogtool`로 갱신됨.

### 잔여 경로 검색
- 제품 코드/리소스/`project.godot`에서 stale old-root 경로
  (`res://addons/dialogtool`, `res://addons/save_game`, `res://addons/save_game_world_state`,
  `res://addons/dialogtool/world_state`) 검색 결과 0건.
- LLM_WIKI historical 문서(DT-00x, ADR-011 등)의 과거 경로 참조만 의도적으로 잔존.

## 검증 결과

- Godot 4.6.3 mono headless `--import`: 각 Step 후 exit 0, parse/class/SCRIPT error 없음.
- Resource load probe: product 씬 + 루트 dialogue `.tres` 24개 전부 `ResourceLoader.load` OK (0 fail).
  path-only ext_resource가 전부 새 경로로 해석됨(`dialoguetool_main.tscn`, `Node/*.tscn`, `Node/Sub/*.tscn`,
  `1.tres`~`6.tres`, `pride_and_prejudice.tres`).
- WorldState / SaveGame 회귀 12/12 PASS: autoload boot(`dt006_step2`), lifecycle(`dt006_step5`),
  store core/통합(`dt005_step2`, `dt005_step6`), mutation(`dt009_step1`), condition store-integration
  (`dt007_step3`, `dt007_step4`), dialogue 소비 e2e(`dt008_step3`, `dt013_step3`), debug preview(`dt010_step1`),
  adapter 통합(`sg001_step3`, `sg002_step2`).
- DialogueTool 회귀 8/8 PASS: effect flow(`dt004_step4`), condition/choice(`dt008_step4`, `dt008_step5`),
  mutation(`dt009_step4`, `dt009_step3b`), editor Play debug preview(`dt010_step3`), state read(`dt013_step3`),
  say paging UI(`dt014_step1`).
- 사용자 러너 보고와 일치: WorldState/SaveGame 18종 ALL PASS, DialogueTool 23종 ALL PASS.
- 에디터 GUI 수동 왕복(그래프 생성 -> 편집 -> 저장 -> 재로드) 1회 검증 완료(사용자 확인).

## 검증하지 못한 내용

- 전체 DT per-step headless 매트릭스(~35 scene)를 단일 일괄 실행으로 재현하지는 않았다. 모든 위험면을 덮는
  대표 회귀(WorldState/SaveGame 12 + DialogueTool 8)와 사용자 러너의 full green 보고로 대체했다.
- fresh-project(addons/world_core만 복사 후 부팅) 수용 테스트를 이번 리뷰에서 새로 만들지는 않았다.

## 잔여 위험

- `--import`/테스트 종료 시 Godot resource leak / "still in use at exit" 경고가 출력되나 clean import에서도
  나타나는 양성 종료 노이즈이며 parse/class/import 실패가 아니다.
- 대규모 rename(git: 207 R / 169 RM 규모)이므로 커밋 후 `git log --follow`로 이력 추적 보존을 확인하는 것이 좋다.

## 판정

**완료**.

WC-001 completion criteria를 충족한다. `DialogueTool`, `WorldState`, `SaveGame`, `SaveGame ↔ WorldState adapter`가
`addons/world_core/` umbrella 아래 sibling 모듈로 이동했고, ADR-013 의존 방향(world_state는 dialogtool/save_game을
참조하지 않음, save_game은 domain-free, dialogtool -> world_state만 허용)이 유지된다. public API와 런타임 동작은
경로 이동 후에도 불변이다. P0/P1 잔여 없음.

## Related

- [[WC-001-WorldCore-Umbrella-Migration]]
- [[ADR-013-WorldCore-Umbrella-Packaging]]
- [[ADR-011-DialogueWorldState-Addon-Packaging]]
- [[DT-011-DialogueWorldState-Addon-Packaging-Review]]
- [[DialogueTool]]
- [[World-State-System]]
- [[SaveGame-System]]
