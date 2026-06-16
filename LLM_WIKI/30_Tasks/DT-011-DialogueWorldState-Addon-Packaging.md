---
id: DT-011
type: task
status: proposed
system: DialogueTool, WorldState
created: 2026-06-16
updated: 2026-06-16
tags: [task, addon, packaging, dialogue, world-state, reuse]
---

# DialogueWorldState Addon Packaging

## Goal

현재 프로젝트 안에 흩어진 `DialogueTool`과 `WorldState`를 다른 Godot 프로젝트에서도 재사용 가능한 하나의 addon
패키지 구조로 정리한다. `addons/dialogtool`만 복사하면 `Assets/Script/gds/world_state` 의존성 때문에 깨지는
현재 구조를 해소한다.

## Context

현재 구조:

```text
addons/dialogtool/
  DialogueTool editor/runtime addon

Assets/Script/gds/world_state/
  StateSchema / WorldStateStore / WorldStateRuntime
  ConditionSet / ConditionEvaluator
```

하지만 DT-008/DT-009 이후 `addons/dialogtool`은 이미 WorldState에 의존한다.

- `WorldStateConditionDef`는 `ConditionSet`을 들고 `ConditionEvaluator.evaluate(...)` 계약을 사용한다.
- `state_set`/`state_add` Effect는 mutation provider 계약(`apply_state_batch`, `add_state`)을 사용한다.
- 일반 런타임 API는 provider 주입으로 decoupled되어 있지만, addon을 다른 프로젝트에 복사하려면 WorldState 코드도
  함께 이동해야 한다.

따라서 DT-010(debug Play provider 주입)을 진행하기 전에, 재사용 가능한 addon 경계와 파일 배치를 먼저 확정한다.

## User Outcome

- 새 프로젝트에 addon 폴더를 복사/활성화하면 Dialogue editor, WorldState schema/store/runtime, ConditionSet,
  StateCondition/StateSet/StateAdd 노드를 함께 사용할 수 있다.
- 게임 프로젝트는 자기 Schema와 save slot 구현만 제공하면 된다.
- `DialogueManager.play(dialogue, WorldState, WorldState)` 또는 후속 debug preview가 addon 안에서 일관되게 동작한다.

## Target Shape (초안)

후보 구조:

```text
addons/dialogue_state_tool/
  plugin.cfg

  dialogtool/
    Editor/
    Node/
    Resource/
    RunTime/
    UI/
    debugger_plugin/

  world_state/
    state_definition.gd
    state_schema.gd
    world_state_store.gd
    world_state_runtime.gd
    condition/

  examples/
    world_state_schema_example.tres
    sample_dialogues/

  tests/
    dialogtool/
    world_state/
```

대안:

```text
addons/dialogtool/
  ...
  world_state/
```

Step 0에서 이름과 구조를 확정한다.

## Scope

### Included

- addon 패키지 경계와 폴더 이름 결정
- `Assets/Script/gds/world_state`를 addon 내부로 옮길지 여부
- 기존 `addons/dialogtool` 경로 유지 vs 새 addon root 도입 결정
- `class_name`, UID, `.tres`/`.tscn` ext_resource path migration 전략
- autoload 등록 책임(`WorldState`, `WorldStateRuntime`, `DialogueManager`, `DialogueToolUtil`) 결정
- game-specific schema/example schema 분리
- 테스트/예제 리소스 배치 원칙
- DT-010 debug preview가 새 구조에서 어디에 붙을지 후속 경계 정의

### Out of Scope

- 실제 파일 이동 구현(Step 0 승인 전)
- SaveGame file/slot 구현
- schema-aware key picker
- editor trace inspector
- AssetLib 배포 메타데이터

## Design Questions (Step 0 확정 — [[ADR-011-DialogueWorldState-Addon-Packaging]])

Step 0 설계 리뷰(2026-06-16)에서 코드 대조 후 아래로 확정했다. 결정 근거는 ADR-011.

1. **Addon root name → 후보 B 확정.** `addons/dialogtool` 루트 유지 + 내부
   `addons/dialogtool/world_state/` 하위모듈. dialogtool 경로(plugin.cfg/editor_plugins/
   debugger/main.tscn/icon)는 변경하지 않는다(ADR-011 D1).

2. **Migration granularity → 한 번에 이동(move), shim 없음.** `class_name`은 경로 독립이라
   코드 참조는 무수정이고 깨지는 것은 path 문자열/`.tres` ext_resource/autoload뿐이다.
   복사 금지(같은 class_name 2중 등록 = 프로젝트 open 실패). `.uid` 동반 이동(ADR-011 D3).

3. **Autoload ownership → 호스트가 런타임 autoload 3종 수동 등록.** 플러그인은 자동 등록하지
   않는다(`add_autoload_singleton`이 `WorldState`→`WorldStateRuntime` 순서를 보장 못 함).
   `DialogueToolUtil`만 플러그인 자동 등록(현행 유지). 설치 문서는 수동 `DialogueToolUtil`
   항목을 넣지 않는다(이중 등록 방지)(ADR-011 D4).

4. **Schema ownership → addon은 example만, 게임 schema는 호스트.** 현 6-key bootstrap을
   `examples/world_state_schema_example.tres`로 이동·개명. store.tscn은 example에 연결해
   out-of-box 부팅, 호스트가 autoload schema를 자기 것으로 교체(ADR-011 D5).

5. **Resource path compatibility → one-time rewrite.** 재작성 대상: `project.godot` autoload,
   `world_state_store.tscn` ext_resource, **이동하는 world_state/condition 테스트
   `.tscn`/`.tres`의 ext_resource path 17개**(`world_state/tests/dt005_step1~6_*.tscn`,
   `dt009_step1_*.tscn`, `condition/tests/dt007_step1~4_*.tscn`, `dt007_spike_*.tscn`),
   코드 path 문자열(`SCHEMA_PATH`/`RUNTIME_SCRIPT`/`STORE_SCENE`/`CLAUSE_SCRIPT`),
   `examples`로 옮길 `affinity_ge_10.tres`. 상수만 고치면 씬 로드가 실패한다.
   `world_state_schema.tres`는 이미 uid(`uid://urle8xa2dmc`) 보유 → 이동 시 uid 보존만
   확인(신규 부여 불필요)(ADR-011 D3).

## Addon 필수 폐쇄집합 (Step 0 확정 — P0/P1)

addon 자급에 다음을 **모두** 포함한다. `condition_validator`가 `StateSchema.KEY_PATTERN` +
`StateDefinition` enum에 의존하므로 코어 2파일(state_definition/state_schema) 누락은 단독
설치 실패다(ADR-011 D2).

```text
state_definition.gd   state_schema.gd
world_state_store.gd  world_state_store.tscn  world_state_runtime.gd
condition/*  (condition_clause, state_condition, condition_group,
              condition_set, condition_validator, condition_evaluator)
```

## Steps

### Step 0: Design Review and Packaging ADR — 완료 (Approved after design fixes)

목표:
- 실제 코드/리소스 경로를 대조해 addon root, autoload, schema, migration 전략을 확정한다.

결과(2026-06-16):
- 결합 표면 확정: 제품 코드 결합은 condition `class_name` 하나(경로 독립), mutation/store/
  runtime은 provider 주입으로 decoupled. 깨지는 것은 소수의 path 문자열/`.tres`/autoload뿐.
- 결정: 후보 B / 호스트 autoload 등록 / example-only schema →
  [[ADR-011-DialogueWorldState-Addon-Packaging]] accepted.
- Step 1 착수 전 fix(ADR-011 D2~D5): 폐쇄집합에 state_definition/state_schema 포함,
  move-not-copy + path 재작성 인벤토리, autoload 순서·소유권, example/게임 schema 분리.
- DT-010은 이 Task 이후로 deferred 유지.

검증 방법:
- [[Design-Review-Prompt]] 형식 코드 대조 — 완료.

### Step 1: Move WorldState into Addon Boundary

목표:
- 폐쇄집합(state_definition/state_schema/store(.gd/.tscn)/runtime/condition)을
  `addons/dialogtool/world_state/`로 **이동**(복사 금지, `.uid` 동반)하고 참조를 갱신한다.

작업 범위:
- `project.godot` autoload 3종 path 재작성, `world_state_store.tscn` ext_resource 재작성.
- 이동하는 world_state/condition 테스트 `.tscn`/`.tres`의 ext_resource path 17개 재작성
  (코드 const뿐 아니라 씬 파일도 — 누락 시 씬 로드 실패).
- `world_state_schema.tres`는 uid(`uid://urle8xa2dmc`) 보존만 확인.

완료 조건:
- 프로젝트 open + Godot `--import`가 새 위치에서 **0 에러**(class_name 중복 0).
- StateSchema/Store/ConditionSet/Dialogue StateCondition 리소스가 로드된다.
- DT-005/006/007 핵심 회귀가 통과한다.

제외 범위: dialogtool 코드 이동(B에선 없음), example/schema 분리(Step 3).

선행 조건: Step 0 승인([[ADR-011-DialogueWorldState-Addon-Packaging]]).

#### 구현 결과 (2026-06-16 — 리뷰 대기)

**이동(move-not-copy).** `git mv Assets/Script/gds/world_state addons/dialogtool/world_state`로
폐쇄집합 전체를 한 번에 이동했다(복사·shim 없음, 원본 디렉터리 완전 제거 확인). `.gd`/`.gd.uid`/
`.tscn`/`.tres` 사이드카가 모두 동반 이동했다. 새 위치:

```text
addons/dialogtool/world_state/
  state_definition.gd(.uid)  state_schema.gd(.uid)
  world_state_store.gd(.uid)  world_state_store.tscn  world_state_runtime.gd(.uid)
  world_state_schema.tres
  condition/  (condition_clause/state_condition/condition_group/condition_set/
               condition_validator/condition_evaluator + .uid)
  condition/tests/  (dt007_step1~4 + spike)
  tests/  (dt005_step1~6, dt006_step1~5, dt009_step1)
```

총 이동: 제품 코드 6 + condition 6 = 12 `.gd`, 1 `.tscn`(store), 1 `.tres`(schema),
WorldState/Condition 테스트 일습(`.gd`+`.tscn`+`.uid`). dt009_step1은 staged-new였고 이동 후에도
보존됐다. 진행 중이던 사용자 작업트리 변경(DT-009 staged/modified)은 되돌리지 않고 그대로 옮겼다.

**path rewrite.** `res://Assets/Script/gds/world_state` → `res://addons/dialogtool/world_state`
일괄 치환. 대상:
- `project.godot` autoload 2종(`WorldState`=store.tscn, `WorldStateRuntime`=runtime.gd).
- `world_state_store.tscn` ext_resource(store.gd + schema.tres).
- `world_state_schema.tres` ext_resource(state_definition.gd / state_schema.gd) — uid
  `uid://urle8xa2dmc` 보존 확인(신규 부여 없음).
- 이동한 world_state/condition 테스트 `.tscn`/`.tres`의 ext_resource path 전체.
- 이동한 테스트 `.gd` const(`SCHEMA_PATH`/`STORE_SCENE`/`RUNTIME_SCRIPT`/`CLAUSE_SCRIPT`).
- addon `RunTime/tests`의 dt008_step1/3/5 `SCHEMA_PATH` const.
- `addons/dialogtool/Test/affinity_ge_10.tres`의 ConditionSet/StateCondition ext_resource path.

**검증.**
- Godot 4.6.3 mono headless `--import` **0 parse/script error**, class_name 중복 0
  (`WorldStateStore`/`StateSchema`/`StateDefinition`/`ConditionSet`/`ConditionEvaluator`/
  `ConditionValidator`/`ConditionClause`/`StateCondition`/`ConditionGroup` 각 1회).
  ※ autoload는 uid 캐시로 부팅 시 해석되므로 첫 import 패스는 stale uid→old path 에러를 1회
  보였고, 캐시 재생성 후 2차 import에서 사라졌다(에디터 캐시 `.godot/editor/*.cfg`의
  "Cannot navigate to old path"만 잔존 — gitignore 캐시, 재생성됨, parse 에러 아님).
- 회귀 20 scene ALL PASS(headless, exit 0):
  DT-005 step1~6(6), DT-006 step1~5(5), DT-007 step1~4+spike(5),
  DT-008 step1/3/5(3), DT-009 step4 e2e(1).
- `rg "Assets/Script/gds/world_state"` — 제품 코드/테스트/리소스 0건(`.idea`/`.godot` IDE·에디터
  캐시와 LLM_WIKI 과거 기록 제외).

**남은 위험.** (1) `.godot/editor/script_editor_cache.cfg`·`editor_layout.cfg`와 `.idea` workspace에
old path 잔존 — gitignore 캐시라 에디터 재진입 시 재생성, 기능 영향 없음. (2) DT-008 step2/step4,
DT-009 step2/3/3b 등 비-spot 테스트는 이번에 재실행하지 않음(경로 비의존 또는 Step 2 범위). 전체
matrix는 DT-011 Step 4 수용 기준에서 확인. (3) example/schema 분리·설치 문서는 Step 2~3 범위로 미수행.

### Step 2: Normalize DialogueTool Paths

목표:
- 후보 B이므로 dialogtool 자체는 이동하지 않는다. addon 테스트 path 문자열과 example `.tres`
  참조를 새 world_state 경로로 정규화한다.

작업 범위:
- addon 테스트의 `SCHEMA_PATH` 등 문자열 경로 재작성, `affinity_ge_10.tres`를 `examples`로
  이동 + ext_resource/uid 재작성, condition 참조 정상 확인.

완료 조건:
- DialogueTool editor load, save/reload, debug highlight가 유지된다.
- DT-004/008/009 핵심 회귀가 통과한다.

선행 조건: Step 1 리뷰 완료

#### 구현 결과 (2026-06-16 — 리뷰 대기)

**이미 Step 1에서 처리된 부분.** addon 테스트의 world_state path 문자열 정규화는 Step 1 path
rewrite에서 함께 완료됐다(`RunTime/tests/dt008_step1/3/5`의 `SCHEMA_PATH` →
`res://addons/dialogtool/world_state/world_state_schema.tres`). 재확인 결과 addon 테스트에
잔존 stale world_state 경로 0건.

**Step 2 고유 작업 — example ConditionSet 이동.** `affinity_ge_10.tres`는 커밋된 헤드리스
테스트가 아니라 루트 샘플 `test.tres`(Choice→state_condition/Branch→state_add 데모)만 참조하는
example fixture였다. 후보 B의 example 위치로 addon 루트 레벨(`world_state/`와 sibling)을 선택해
(ADR-011 target shape 일치, Step 3 example schema/sample dialogue도 같은 곳에 모임) 이동:

```text
addons/dialogtool/Test/affinity_ge_10.tres
  → addons/dialogtool/examples/affinity_ge_10.tres   (git mv, uid uid://bwsq70tpasvaw 보존)
```

- 비게 된 `addons/dialogtool/Test/` 디렉터리 제거.
- 참조 재작성: 루트 `test.tres`의 ext_resource `path`를 새 위치로 갱신(uid 보존이라 기능은
  무영향이나 stale path 제거). 파일명·내용은 그대로(rename/schema 분리는 Step 3 범위).
- 이동한 `affinity_ge_10.tres` 내부 ext_resource(condition_set.gd/state_condition.gd)는 Step 1에서
  이미 새 world_state condition 경로를 가리키며 이동으로 불변.

**검증.**
- headless `--import`(2-pass) 0 parse/script error, class_name 중복 0, affinity 관련 에러 0.
  (Step 1과 동일한 `.godot/editor` 캐시 "Cannot navigate to old runtime path" 1줄만 잔존 — gitignore
  캐시, 기능 무관.)
- DT-004/008/009 회귀 15 scene ALL PASS(headless exit 0): DT-004 step1~4+pipeline(5),
  DT-008 step1~5+spike(6), DT-009 step2/3/3b/4(4). save/reload는 dt008_step2·dt009_step3 editor
  roundtrip로, editor load는 `--import`로 커버.
- `rg "dialogtool/Test/affinity_ge_10"` — 제품 코드/리소스 0건(LLM_WIKI 과거 기록 제외).

**남은 위험.** (1) debug highlight는 헤드리스 직접 검증 불가 — 해당 경로 코드 무변경이라 회귀
없음으로 간주. (2) `test.tres`는 addon 외부 루트 샘플이라 장기적으로 Step 3 sample_dialogues로
이전할지 미정(이번엔 path만 갱신). (3) Step 3 example schema 개명/설치 문서 미수행(범위 밖).

### Step 3: Examples, Migration, and Docs

목표:
- 다른 프로젝트에 복사할 때 필요한 예제 schema/dialogue와 설치 문서를 제공한다.

완료 조건:
- 새 프로젝트 설치 절차 문서화
- example schema와 sample dialogue가 addon 내부에서 유효
- 기존 프로젝트 리소스 migration 방법 문서화

선행 조건: Step 2 리뷰 완료

#### 구현 결과 (2026-06-16 — 리뷰 대기)

**example schema 이동·개명(ADR-011 D5).** `git mv`로
`world_state/world_state_schema.tres` → `examples/world_state_schema_example.tres`
(uid `uid://urle8xa2dmc` 보존). 참조 재작성: `world_state_store.tscn` ext_resource +
테스트 6개 `SCHEMA_PATH`(dt006_step1, dt007_step3/4, dt008_step1/3/5). store.tscn이 example을
가리켜 out-of-box 부팅 유지, 호스트는 autoload schema를 자기 것으로 교체한다.

**sample dialogue(완료조건 #2).** 루트 사용자 샘플 `test.tres`를 채택해
`examples/sample_dialogues/sample_world_state_dialogue.tres`로 `git mv`(uid 보존, 자기 외 참조처
없음 확인). `Start → Choice(Take/Leave) → state_condition/Branch → Say Rich/Poor → End` +
`state_add(+50 actor.example.affinity)`로 example schema·example ConditionSet(affinity≥10)을 실제
소비한다. 내부 ext_resource는 모두 절대 `res://` 경로라 이동에도 불변(affinity는 Step 2의 examples
경로 유지). example schema에 `actor.example.affinity`(INT) 키가 있어 데모가 end-to-end 동작.

**설치/마이그레이션 문서(완료조건 #1·#3).** 포터블 패키지가 함께 들고 다니도록 `addons/dialogtool/README.md`
신규:
- 폴더 구조 + 복사 → `--import` → 플러그인 활성화 절차.
- **런타임 autoload 수동 등록(순서 명시: DialogueManager → WorldState → WorldStateRuntime)**, `DialogueToolUtil`은
  플러그인 자동 등록이라 수동 추가 금지(ADR-011 D4 / ADR-007).
- 게임 schema 교체 절차 + SaveGame adapter 경계(D6), provider 주입 실행 예.
- 기존 프로젝트 마이그레이션(move-not-copy, path 재작성 인벤토리, uid 보존, `--import` 검증).

**examples 위치 결정.** addon 루트 `addons/dialogtool/examples/`(world_state와 sibling, ADR target shape
일치). schema/ConditionSet/sample_dialogues가 한 곳에 모임(Step 2 affinity 이동과 동일 기준).

**검증.**
- headless `--import`(2-pass) 0 parse/script error, class_name 중복 0, examples/schema/sample 관련 에러 0
  (Step 1~2와 동일한 `.godot/editor` 캐시 navigate 1줄만 잔존).
- 명시적 load check(throwaway SceneTree `-s`): sample dialogue 로드(8 nodes), example schema
  `validate().valid==true`(6 keys), affinity ConditionSet 로드 — exit 0. ※ `-s` bare-boot 특성상
  say_def.gd "depended scripts" compile 경고가 떴으나 scene 기반 회귀(dt004/008/009 say)에서는 미재현 —
  global class cache 로드 순서 artifact, 산출물 로드는 성공.
- schema 의존 회귀 8 scene ALL PASS: dt006_step1, dt007_step3/4, dt008_step1/3/5,
  dt005_step6, dt009_step4(autoload store가 example schema로 부팅).
- 제품 코드/리소스에 old schema/Test 경로 0건.

**남은 위험.** (1) 에디터 Play로 sample dialogue를 실제 재생하는 preview는 DT-010(deferred) 범위 —
이번엔 load 유효성 + 헤드리스 e2e로 대체 검증. (2) DT-006/DT-007 **Task 문서**의 과거 schema 경로는
historical record라 미수정(System 문서·Current-State는 갱신). (3) Fresh-project 설치 수용 테스트는 Step 4 범위.

### Step 4: Integration Matrix and Completion Review

목표:
- 패키징 후 전체 DT-004~009 회귀와 import를 통과시키고 완료 판정을 받는다.

완료 조건:
- 전체 DT-004~009 회귀 + `--import` GREEN, stale 경로 문구 없음.
- **Fresh-project 설치 수용 테스트(신규, DT-011 수용 기준):** 빈 Godot 프로젝트에
  `addons/dialogtool/`만 복사 → 플러그인 활성화 → 문서대로 autoload 3종 등록(순서 포함) →
  `--import` 0 에러 → ConditionSet 분기 + StateSet/StateAdd가 동작하는 샘플 대화 실행.
- autoload 누락/중복/순서 실패 시나리오: 잘못된 순서 → 크래시 없이 not-ready,
  WorldState 누락 → mutation `provider_missing` fail-closed.
- DT-010 debug preview를 재개할 수 있는 구조 확정.

선행 조건: Step 3 리뷰 완료

#### 구현 결과 (2026-06-16 — 완료 판정 대기, 제품 코드 변경 없음)

**전체 회귀 matrix.** `--import` 0 parse/script error(class_name 중복 0), 전체 DT-004~009 테스트
**32/32 scene ALL PASS**(dt004×5, dt005×6, dt006×5, dt007×4+spike, dt008×6, dt009×6). 제품 코드/
리소스에 stale 경로 0(`.godot`/`.idea` 에디터·IDE 캐시 navigate 1줄만 잔존 — gitignore, 기능 무관).

**Fresh-project 설치 수용 테스트.** 임시 빈 프로젝트(`_dt011_fresh`, 검증 후 삭제)에 `addons/dialogtool/`만
복사 + `project.godot`에 autoload 등록 + 플러그인 enable 후:
- `--import` 2-pass **0 에러**.
- 수용 회귀 **7/7 PASS**: dt006_step1/step2(autoload `/root/WorldState`가 example schema로 부팅·ready),
  dt009_step1(add_state), dt007_step4(condition e2e), dt008_step3(Branch state_condition e2e via
  DialogueManager), dt008_step4(conditional Choice), dt009_step4(Choice→state_add→Branch 전체 경로).
  → addon 단독으로 ConditionSet 분기 + StateSet/StateAdd 동작 확인.

**autoload 실패 시나리오(크래시 0).**
- 잘못된 순서(Runtime를 WorldState보다 먼저): **크래시 없음**, store도 정상 해석(`store_ready=true`).
  Godot 4.6.3이 autoload 노드를 모두 root에 add한 뒤 `_ready()`를 돌려 inter-autoload 순서가
  `get_node_or_null` 해석에 영향을 주지 않음(스펙의 worst-case "not-ready"보다 안전 방향). 권장 순서는
  버전 무관 안전을 위해 유지.
- WorldState autoload 누락: **크래시·SCRIPT ERROR 없음**, `is_store_ready()==false`(graceful not-ready,
  null-safe `world_state_runtime.gd:54`).
- mutation `provider_missing` fail-closed: dt009 음성 케이스로 커버(전수 PASS).

**Step 4에서 발견(문서화 완료).** `dialogue_player.gd`/`dialogue_manager.gd`는 `DialogueToolUtil`
autoload 식별자에 **parse-time 의존**(기존 addon 설계, dt004 주석에 명시). GUI 플러그인 활성화는 이
autoload를 `project.godot`에 persist하지만, **GUI를 거치지 않은 순수 헤드리스/CI 설치**는 `DialogueToolUtil`
미등록으로 `dialogue_manager.gd` parse error가 난다. → README 설치 절에 "헤드리스/CI 주의" 박스로
`DialogueToolUtil`을 함께 등록하도록 문서화. 제품 코드 변경 없음(packaging/docs 범위).

**DT-010 재개 구조 확정.** debug preview는 새 addon 구조에서 다음 seam에 붙는다: 에디터 Play 진입점
(`addons/dialogtool` 에디터 코드)이 별도 Godot 프로세스를 띄울 때, addon에 동봉된
`examples/world_state_schema_example.tres`로 `WorldStateStore`를 구성해 read/mutation provider로
`DialoguePlayer`에 주입하면 된다. WorldState 코어가 이제 addon 내부(`world_state/`)에 있어 호스트
프로젝트 경로에 의존하지 않으므로, DT-010은 addon-내부 example provider만으로 자급 가능하다.

**검증 한계.** 에디터 Play로 sample dialogue를 실제 GUI 재생하는 것은 DT-010(deferred) 범위 —
이번엔 headless e2e + fresh-project 수용으로 대체. fresh-project 테스트는 임시 디렉터리에서 수행 후
삭제(저장소 미포함).

## Completion Criteria

> Step 1~4 구현·검증 완료(완료 판정 대기). 아래 기준 대비 현황:
> - addon 한 경계로 이동 — Step 1(world_state move) + Step 2/3(examples 정리)로 충족, fresh-project 수용 PASS.
> - game schema/save 분리 — example-only schema(D5) + SaveGame adapter 경계(D6) README 문서화.
> - DT-004~009 유지 — 전체 32/32 + fresh 7/7 PASS.
> - 설치/마이그레이션 문서 — `addons/dialogtool/README.md`(자체 포함, 헤드리스 DialogueToolUtil 주의 포함).

- `DialogueTool + WorldState`가 한 addon 경계 안에 들어가 다른 프로젝트로 옮길 수 있다.
- game-specific schema/save data는 addon 코드와 분리된다.
- 기존 기능(DT-004~009)이 경로 이동 후에도 유지된다.
- 설치/마이그레이션 문서가 존재한다.

## Related

- [[DT-010-Dialogue-Debug-WorldState-Preview]]
- [[DT-009-State-Mutation-Dialogue-Effects]]
- [[DT-008-State-Condition-Dialogue-Integration]]
- [[World-State-System]]
- [[DialogueTool]]
