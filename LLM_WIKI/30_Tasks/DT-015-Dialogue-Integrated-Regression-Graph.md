---
id: DT-015
type: task
status: in-progress
system: DialogueTool
created: 2026-06-19
updated: 2026-06-19
tags: [task, dialogue-tool, regression, integration-test]
---

# DT-015 Dialogue Integrated Regression Graph

## Goal

DialogueTool의 기본 대화 조합을 한 기준 그래프로 고정한다.

현재 `Start`, `Say`, `Choice`, `Expression`, `Branch`, `End`는 개별 Step과 일부 e2e 테스트에서 검증되지만,
이 노드들이 **하나의 DialogueGraphResource** 안에서 연결·저장·실행되는 canonical 회귀 리소스는 없다.
WC-001 경로 이동 이후와 향후 Dialogue 기능 추가 전에, 기본 플로우가 계속 살아있는지 빠르게 확인할 수 있는
작은 통합 기준점을 만든다.

## Non-Goals

- 새 Dialogue 런타임 기능, 새 노드, 새 public API를 추가하지 않는다.
- WorldState, SaveGame, mutation Effect, Portrait, ConditionSet, State Read를 필수 경로로 넣지 않는다.
  이 작업은 순수 DialogueTool 기본 조합 회귀다.
- DialogueManager 반복 실행/교체/연속 실행 전용 테스트는 다음 Next 항목으로 유지한다.
- production sample dialogue의 서사/UX를 확장하지 않는다. 이 작업의 산출물은 테스트 fixture와 회귀 기준이다.
- 에디터 UX 개선, 노드 display alias, Response Selector, History/Inspector는 범위 밖이다.

## Context

- [[Current-State]] Verification Baseline은 `Start -> Say -> Choice -> Branch -> End` 흐름이 종료까지 실행돼야
  한다고 적고 있다.
- [[Open-Tasks]] Next에는 "Dialogue 통합 회귀 그래프 작성: Start, Say, Choice, Expression, Branch, End를 한
  리소스에서 검증한다"가 남아 있다.
- 기존 테스트는 다음 표면을 부분적으로 커버한다.
  - `dt008_step1_state_condition_test`: `Variable -> Expression -> Branch` 런타임 회귀를 작은 in-memory 그래프로 검증.
  - `dt009_step4_e2e_completion_test`: `Choice -> Branch -> Say` 및 mutation e2e를 검증하지만 WorldState mutation
    목적의 테스트라 기본 DialogueTool smoke 기준으로 쓰기에는 무겁다.
  - `dt013_step3_e2e_test`: `State Read -> Expression -> Branch/Choice`를 검증하지만 WorldState provider가 필수다.
  - `dt014_step1_say_paging_ui_test`: 실제 UI 클릭 경로를 검증하지만 `Expression/Branch` 통합 기준은 아니다.
- 따라서 DT-015는 **작고 독립적인 canonical graph**를 만든다. 실패하면 "기본 Dialogue graph 조합이 깨졌다"는
  신호가 되도록, WorldState/SaveGame 같은 다른 시스템 이유로 실패하지 않게 한다.

## Design

### Canonical Graph Shape

하나의 `DialogueGraphResource`에 아래 노드를 모두 포함한다.

```text
Start
  -> Say "Intro"
  -> Choice ["Strong", "Weak", "Leave"]

Choice[Strong]
  -> Branch( Expression(strength >= 5), strength = 7 )
     true  -> Say "Strong success" -> End
     false -> Say "Strong fail"    -> End

Choice[Weak]
  -> Branch( Expression(strength >= 5), strength = 3 )
     true  -> Say "Weak success" -> End
     false -> Say "Weak fail"    -> End

Choice[Leave]
  -> Say "Leave" -> End
```

의도:

- `Start`, `Say`, `Choice`, `Expression`, `Branch`, `End`가 모두 같은 리소스에 존재한다.
- 같은 expression 문자열(`strength >= 5`)을 true/false 양쪽에서 검증한다.
- `Choice`의 여러 flow output이 서로 다른 Branch/Say/End 경로로 가는 것을 검증한다.
- WorldState provider 없이 `Variable` Data 노드가 Expression 입력값을 공급한다.
- "Strong" 선택은 true branch, "Weak" 선택은 false branch, "Leave" 선택은 Branch 없이 직접 종료된다.
- 각 타입은 1회 이상 등장한다. Strong/Weak 경로는 각각 독립 `Variable + Expression + Branch` trio를 갖는다.
- 에디터 authored Expression의 입력 변수명은 `expression_node.gd`가 자동 생성하는 `A`이므로, Step 2 expression
  문자열은 `A >= 5`를 쓴다. Step 1 hand-built runtime graph는 읽기 쉬운 `strength >= 5`를 써도 된다.
- Step 1과 Step 2가 보장하는 동일성은 세 경로의 Say sequence와 End 도달이다. 두 Step의 Expression params가
  byte-identical해야 한다는 뜻이 아니다.

### Runtime Resource Contract

Step 1 테스트는 in-memory 또는 test-owned `.tres`로 `DialogueGraphResource`를 구성한다.

- runtime nodes:
  - `start`
  - `say` with `params.text`
  - `choice` with `params.choices`
  - `variable` with `params.value`
  - `expression` with `params.expression` and ordered `params.inputs`
  - `branch`
  - `end`
- Strong/Weak 경로는 독립 `variable`/`expression`/`branch` 노드를 가진다. `variable`은 RANDOM을 쓰지 않고
  `{"value": 7}` / `{"value": 3}` 같은 literal params만 사용해 deterministic하게 만든다.
- runtime connections:
  - Flow input은 기존 런타임 관례대로 Branch input port `1`, Branch condition Data input port `0`.
  - Choice item flow output은 항목 index와 같은 output port를 쓴다.
  - Variable output port `0` -> Expression input port `0`.
  - Expression output port `0` -> Branch condition input port `0`.
- `runtime_nodes`/`runtime_connections`가 source of truth다. editor `nodes/connections`와 Definition authoring은
  Step 2에서 따로 검증한다.

### Test Artifact Policy

- Step 1은 `addons/world_core/dialogtool/RunTime/tests/`에 headless test scene/script를 추가한다.
  권장 이름:
  - `dt015_step1_integrated_graph_test.gd`
  - `dt015_step1_integrated_graph_test.tscn`
- Canonical graph를 영구 example `.tres`로 둘지 여부는 Step 1 구현자가 확정한다.
  - 권장: 테스트 코드가 graph를 생성하고 `res://__dt015_integrated_graph.tres`에 저장한 뒤
    `ResourceLoader.CACHE_MODE_IGNORE`로 재로드해 실행한다. 이 방식은 repository product resource를 늘리지 않으면서
    `.tres` 저장/로드 경로까지 검증한다.
  - 대안: `addons/world_core/dialogtool/examples/sample_dialogues/integrated_regression_dialogue.tres`를 추가한다.
    이 경우 example maintenance 비용이 생기므로, 구현 전 필요성을 명확히 기록한다.
- 임시 `.tres`를 쓰는 경우 테스트 종료 시 삭제한다. 기존 `__dt*` test fixture 패턴을 따른다.

## Steps

### Step 0 — Design Review

Scope:

- 이 문서가 현재 `DialoguePlayer`, `DialogueGraphResource`, `VariableDef`, `ExpressionValueDef`, Choice/Branch 포트
  관례와 맞는지 코드로 대조한다.
- Step 1/2 분해, artifact 위치, verification matrix가 충분한지 확인한다.
- 제품 코드, 제품 `.tscn`, 제품 `.tres`, `project.godot`은 수정하지 않는다.

Done condition:

- 설계 리뷰 판정이 `Approved` 또는 `Approved after design fixes`다.
- 미정 artifact 정책이 구현 전에 선택 가능한 수준으로 좁혀져 있다.

### Step 1 — Runtime Integrated Graph Resource

Scope:

- `Start + Say + Choice + Variable + Expression + Branch + End` canonical graph를 test-owned resource로 만든다.
- 저장이 필요한 경우 `ResourceSaver.save` -> `ResourceLoader.load(..., CACHE_MODE_IGNORE)` -> 실행 순서를 따른다.
- 실제 `DialogueManager.play(resource)` 또는 실제 `Dialogue_UI.tscn` + `ui.play(resource)` 중 하나를 primary 경로로 쓴다.
  권장 primary는 `DialogueManager.play`다. 이유는 게임 코드 진입점까지 포함하고, UI request를 통해 Say/Choice/End를
  관찰할 수 있기 때문이다.
- 선택 입력은 `DialogueManager._ui.dialogue_player.select_choice(index)` 또는 실제 UI helper를 통해 보낸다.
- Say 진행은 `DialogueManager._ui.dialogue_player.advance()`를 직접 호출해 넘긴다. 단언은 렌더된
  `ui.say.text`가 아니라 `DialogueManager.ui_request` payload의 `"say"`/`"choices"` 문자열로 한다
  (`dt013_step3_e2e_test._run` 방식).
- 이 방식은 `DialogueUI`의 type effect 렌더링/프레임 delta를 관찰하지 않으므로, DT-014에서 필요했던
  `say.set_process(false)` 같은 타이핑 정지 처리가 필요 없다.

Required tests:

- Strong 선택:
  - 첫 request가 Intro Say다.
  - `player.advance()`로 Intro Say를 넘긴 뒤 다음 request가 Choice로 온다.
  - 다음 request가 Choice `["Strong", "Weak", "Leave"]`다.
  - 선택 후 Expression true branch로 가서 `"Strong success"` Say payload를 표시한다.
  - `player.advance()`로 final Say를 넘기면 End에 도달한다.
- Weak 선택:
  - 같은 resource를 재실행한다.
  - Intro Say를 `player.advance()`로 넘기고 Choice에서 Weak를 선택한다.
  - 선택 후 Expression false branch로 가서 `"Weak fail"` Say payload를 표시한다.
  - `player.advance()`로 final Say를 넘기면 End에 도달한다.
- Leave 선택:
  - 같은 resource를 재실행한다.
  - Intro Say를 `player.advance()`로 넘기고 Choice에서 Leave를 선택한다.
  - Branch/Expression 경로 없이 `"Leave"` Say payload를 표시한다.
  - `player.advance()`로 final Say를 넘기면 End에 도달한다.
- Save/reload:
  - 저장한 `.tres`를 cache-ignore로 재로드해도 위 세 선택 결과가 동일하다.
  - `runtime_nodes`와 `runtime_connections`의 노드 수, 연결 수, `start_node_id`가 보존된다.
  - Choice 항목 0/1/2 flow output port가 reload 후 각각 Strong/Weak/Leave 경로로 보존됨을 단언한다.
    특히 Leave는 Branch를 거치지 않고 직접 Say로 연결되는 직결 경로를 확인한다.
- Negative sanity:
  - Expression 입력이 미연결인 variant 한 개를 만들어 errored Data가 Branch false로 fail-closed되는지 확인한다.
- Failure hygiene:
  - SCRIPT ERROR 0.
  - watchdog으로 hang을 막는다.
  - 테스트가 생성한 임시 resource는 cleanup한다.

Out of scope:

- 에디터 노드 생성/캡처/저장 왕복.
- DialogueManager same-frame replacement, repeated rapid play lifecycle 전용 검증.
- WorldState provider, mutation Effect, Portrait request 검증.

Suggested regression:

- `--import`
- `dt008_step1_state_condition_test` 또는 최소 Variable/Expression/Branch 기존 회귀
- `dt014_step1_say_paging_ui_test` 또는 UI path sanity

### Step 1 완료 결과

- **상태**: 완료 (2026-06-19)
- **산출물**:
  - `addons/world_core/dialogtool/RunTime/tests/dt015_step1_integrated_graph_test.gd`
  - `addons/world_core/dialogtool/RunTime/tests/dt015_step1_integrated_graph_test.tscn`
- **검증 내용**:
  - `Start -> Say "Intro" -> Choice` 및 `Strong`, `Weak`, `Leave` 세 개의 선택 분기 경로에 대해 수동 advance와 select_choice를 활용하여 기대하는 Say payload 도달 및 End 노드 도달을 성공적으로 단언함.
  - 임시 `.tres` 파일(`res://__dt015_integrated_graph.tres`) 저장 후 `CACHE_MODE_IGNORE`로 재로드하여 node count (15), connection count (18), `start_node_id` (0)가 완벽히 복원됨을 단언함.
  - 재로드 후 Choice 항목들의 output port (0/1/2)가 각각 Strong/Weak/Leave 경로로 흘러가며, 특히 Leave는 Branch를 우회하고 Say로 직결됨을 검증함.
  - 재로드된 리소스를 사용해 Strong/Weak/Leave 3가지 경로를 재수행하여 동일한 결과를 내는지 검증함.
  - Negative sanity: Expression 입력 포트 미연결 시, `errored` 전파로 인해 Branch가 Godot Expression ERROR 로그는 발생하지만 SCRIPT ERROR(런타임 크래시) 없이 graceful하게 false 분기(`Strong fail`)로 fail-closed됨을 검증함.
  - 실행 완료 후 임시 `.tres` 파일을 성공적으로 정리함.
  - 지정 회귀 테스트(`dt008_step1_state_condition_test`, `dt014_step1_say_paging_ui_test`)를 수행하여 100% 통과함을 확인함.
- **검증 커맨드 및 결과**:
  - `D:\SteamLibrary\steamapps\common\Godot Engine\Godot_v4.6.3-stable_mono_win64_console.exe --headless --path . res://addons/world_core/dialogtool/RunTime/tests/dt015_step1_integrated_graph_test.tscn`
  - 결과: `[DT-015 Step1] ALL PASS` (Exit Code 0, SCRIPT ERROR 0)

### Step 2 — Editor Authored Round-Trip and Completion Review

Scope:

- 실제 `addons/world_core/dialogtool/dialoguetool_main.tscn` fixture에서 같은 canonical graph를 에디터 노드로 작성한다.
- `capture_current_graphedit()` -> `ResourceSaver.save` -> cache-ignore reload -> runtime execution을 검증한다.
- Step 1의 in-memory/runtime-only graph와 에디터 authored graph가 같은 three-route 결과를 낸다는 것을 확인한다.
- Expression 입력 포트 authoring + Variable -> Expression 연결 round-trip은 이번 작업의 최초 회귀 대상이다.
  capture port-index 매핑 등에서 제품 버그가 발견되면 Step 2에서 임의 수정하지 말고
  [[#Failure / Mismatch Policy]]에 따라 Design Deviation으로 보고한다.
- 구현 권고: 전체 canonical graph를 만들기 전에 작은 `Variable -> Expression -> Branch -> Say` authored spike를
  먼저 작성·저장·재로드·실행해 Expression 입력 포트 round-trip 경로를 검증한다.
- 문서와 리뷰를 마감한다.

Required tests:

- Start/Say/Choice/Variable/Expression/Branch/End 노드가 에디터 Definition/Adapter 경로에서 생성 가능하다.
- Choice 3개 항목의 flow output port가 저장/재로드 후 유지된다.
- Expression 입력 key 순서(`inputs`)와 Variable -> Expression 연결이 저장/재로드 후 유지된다. 에디터 authored
  Expression은 자동 입력명 `A`를 사용하므로 expression 문자열은 `A >= 5`다.
- Branch true/false output이 각각 올바른 Say로 연결된다.
- reload 후 Strong/Weak/Leave 세 경로가 Step 1과 같은 Say sequence + End를 만든다.
- 제품 코드 변경 없이 테스트만 추가하는 것을 기본으로 한다. 실제 제품 버그가 발견되면 Design Deviation으로 보고한다.

Documentation:

- [[DialogueTool]] Validation 또는 Verification 절에 canonical integrated regression graph가 생겼다는 현재 사실을 추가한다.
- [[Current-State]] Known Gaps의 "별도의 에디터-저장 기반 통합 회귀 .tres 샘플은 아직 없다"를 해소 여부에 맞게 갱신한다.
- [[Open-Tasks]]에서 DT-015를 완료로 이동하고, 남은 DialogueManager lifecycle 테스트는 Next에 유지한다.
- 리뷰 문서 `LLM_WIKI/50_Reviews/DT-015-Dialogue-Integrated-Regression-Graph-Review.md`를 작성한다.

### Step 2 완료 결과

- **상태**: 완료 (2026-06-19)
- **산출물**:
  - `addons/world_core/dialogtool/RunTime/tests/dt015_step2_editor_authored_roundtrip_test.gd`
  - `addons/world_core/dialogtool/RunTime/tests/dt015_step2_editor_authored_roundtrip_test.tscn`
- **검증 내용**:
  - 실제 `dialoguetool_main.tscn` 에디터 씬을 인스턴스화하고 GraphEdit를 찾아 canonical graph 노드들을 동적으로 생성하고 배치함.
  - 에디터 상에서 Start, Say, Choice, Variable, Expression, Branch, End 노드들을 생성 및 연결 포트(to_port: 0)로 연결하여 정상 캡처(`capture_current_graphedit`) 및 임시 리소스 파일(`res://__dt015_step2_graph.tres`)로 저장하고 재로드함.
  - 재로드한 리소스를 런타임에 실행하여 Strong/Weak/Leave 3가지 경로(각각 true branch, false branch, branch 우회 직결)의 Say 텍스트와 End 노드 도달을 검증함.
  - 에디터 authored Expression의 자동 변수명 `A` 및 Choice 리스트, 포트 연결 정보 보존을 단언하고 런타임 e2e 실행을 매칭함.
  - VariableDef의 프로퍼티가 `.value` 대신 `.variable_name`, `.variable_type`, `.variable`로 올바르게 세팅되도록 수정하여 `Nil` 에러 없이 완벽히 패스함을 확인함.
  - 실행 완료 후 임시 `.tres` 파일을 성공적으로 정리함.
  - 지정 회귀 테스트(`dt008_step1_state_condition_test`, `dt014_step1_say_paging_ui_test`)를 수행하여 100% 통과함을 확인함.
- **검증 커맨드 및 결과**:
  - `D:\SteamLibrary\steamapps\common\Godot Engine\Godot_v4.6.3-stable_mono_win64_console.exe --headless --path . res://addons/world_core/dialogtool/RunTime/tests/dt015_step2_editor_authored_roundtrip_test.tscn`
  - 결과: `[DT-015 Step2] ALL PASS` (Exit Code 0, SCRIPT ERROR 0)

## Failure / Mismatch Policy

- 기존 코드가 설계된 canonical graph를 정상 실행하면 제품 코드는 수정하지 않고 테스트만 추가한다.
- 포트 번호, runtime param 이름, 저장/재로드 형식이 문서 예상과 다르면 실제 코드가 source of truth다.
  단, 차이가 회귀 위험이면 Task에 Design Deviation으로 기록하고 설계 리뷰로 되돌린다.
- `Expression` parse/execute 오류, Choice flow 오배선, Branch port 오해처럼 테스트 fixture 문제로 판명되면
  테스트만 수정한다.
- 에디터 authored Expression 입력 포트 round-trip에서 capture port-index 매핑, 자동 입력명(`A`, `B`, ...)
  보존, Variable -> Expression 연결 보존 문제가 발견되면 제품 버그 가능성이 높다. Step 2에서 즉시 고치지 말고
  실패 증거, 영향 범위, 최소 수정 후보를 Design Deviation으로 보고한다.
- 기본 사용자 흐름이 실제 제품 코드 문제로 실패하면 Step 1에서 임의 수정하지 말고, 실패 증거와 최소 수정 범위를
  보고한다. P0/P1이면 DT-015 Step 1 안에서 수정할지 별도 bugfix Task로 분리할지 사용자 결정 후 진행한다.

## ADR

작성하지 않는다. 이 작업은 새 장기 설계 판단이 아니라, 이미 존재하는 기본 DialogueTool 노드 조합의 회귀 기준을
추가하는 검증 작업이다.

## Open Questions

- Step 1에서 영구 example `.tres`를 만들지, test-owned 임시 `.tres`만 만들지.
  - 권장: 임시 `.tres`만 사용. Step 2에서 에디터 authored round-trip까지 검증하면 product example 유지 비용 없이
    목표를 달성한다.
- Step 1 primary 실행 경로를 `DialogueManager.play`로 할지 `Dialogue_UI.tscn + ui.play`로 할지.
  - 확정: primary = `DialogueManager.play`, Say advance = `DialogueManager._ui.dialogue_player.advance()`,
    Choice input = `select_choice(index)`, 관찰 = `DialogueManager.ui_request` payload. DT-014가 UI 클릭/typing
    경로를 이미 고정했고, 이번 기준은 게임 코드 진입점에서 기본 graph 조합을 보는 것이 목적이다.

## Completion Criteria

- 한 리소스에서 `Start`, `Say`, `Choice`, `Variable`, `Expression`, `Branch`, `End` 조합이 세 선택 경로로 검증된다.
- 저장 후 cache-ignore 재로드한 리소스도 같은 결과를 낸다.
- 에디터 authored graph의 capture/save/reload/runtime execution까지 검증된다.
- SCRIPT ERROR 0, `--import` 0 parse error, 지정 회귀 GREEN.
- 문서와 리뷰가 완료되고 [[Open-Tasks]]에서 DT-015가 제거되거나 완료로 이동한다.

## Related

- [[DialogueTool]]
- [[DialogueTool-Architecture]]
- [[Current-State]]
- [[Open-Tasks]]
- [[STEP_REVIEW_WORKFLOW]]
