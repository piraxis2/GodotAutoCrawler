---
id: DT-016
type: task
status: completed
system: DialogueTool
created: 2026-06-19
updated: 2026-06-19
tags: [task, dialogue-tool, dialogue-manager, lifecycle, regression]
---

# DT-016 DialogueManager Lifecycle Regression

## Goal

`DialogueManager.play(resource, read_provider, mutation_provider)`의 반복 실행, 교체, 재진입, 지연 signal 차단
계약을 전용 headless 회귀 테스트로 고정한다.

현재 lifecycle 보장은 DT-004/DT-008/DT-009/DT-014/DT-015 테스트에 흩어져 있다. 이 작업은 새 기능을 추가하지
않고, 게임 코드 진입점인 `DialogueManager` 기준으로 다음 계약을 한 곳에서 검증한다.

- 같은 리소스를 반복 실행해도 이전 UI/Player/대기 상태가 새 실행으로 새지 않는다.
- 실행 중 `play()`로 다른 리소스를 시작하면 latest-wins가 보존되고 이전 UI의 지연 요청/종료 signal은 무시된다.
- 같은 프레임 연속 `play()`에서 폐기된 resource/provider가 deferred start 뒤늦게 실행되지 않는다.
- `dialogue_end` callback 안에서 새 대화를 시작해도 새 대화가 `_dismiss()`에 의해 지워지지 않는다.
- `ui_request` callback 한가운데서 `play()`가 재진입해도 이전 player의 후속 request가 새 실행으로 섞이지 않는다.

## Non-Goals

- 새 public API, 새 signal, 새 runtime node를 추가하지 않는다.
- `DialogueUI` Say line paging 자체는 재검증하지 않는다(DT-014 범위).
- canonical graph 저장/에디터 authored round-trip은 재검증하지 않는다(DT-015 범위).
- WorldState/SaveGame 기능의 정확성은 직접 검증하지 않는다. provider 격리는 최소 spy provider로만 확인한다.
- production UI, 입력 focus, 실제 마우스 클릭, screenshot 검증은 범위 밖이다.

## Context

### 현재 구현 사실

- `DialogueManager.play(...)`는 null resource를 거부하고, 정상 resource면 항상 `_dismiss()`로 기존 UI/layer를
  정리한 뒤 새 `CanvasLayer`와 `Dialogue_UI.tscn`을 생성한다.
- `_dismiss()`는 살아있는 `_ui`에 `cancel_pending_start()`를 먼저 호출하고, layer를 `queue_free()`한 뒤
  `_layer = null`, `_ui = null`로 만든다.
- `DialogueManager._on_ui_request(request, source_ui)`와 `_on_end(source_ui)`는 source UI guard를 사용한다.
  이미 다른 UI로 교체된 이전 UI가 지연 request/end를 발행해도 `source_ui != _ui`이면 무시한다.
- `_on_end(source_ui)`는 먼저 `_dismiss()`를 수행한 뒤 `dialogue_end.emit()`을 호출한다. 따라서 종료 callback에서
  새 `play()`를 호출해도 새 대화가 과거 종료 처리에 의해 지워지지 않아야 한다.
- `DialogueUI.play(...)`는 resource/read/mutation provider를 `_pending_start` Dictionary에 묶어 보관하고
  `_deferred_start.call_deferred()`로 시작한다. 같은 UI에서 같은 프레임에 여러 번 `play()`가 호출되면 마지막
  pending 요청만 시작된다.

### 기존 테스트와 남은 gap

- `dt004_step4_integration_test`는 Portrait effect callback 중 교체, 종료 callback 재진입, 같은 UI 반복 실행을
  검증한다.
- `dt008_step3_branch_e2e_test`, `dt009_step2_runtime_mutation_test`, `dt009_step4_e2e_completion_test`는
  provider 격리와 same-frame latest-wins를 특정 WorldState/mutation 시나리오에서 검증한다.
- `dt014_step1_say_paging_ui_test`는 같은 UI의 paging state 반복/교체 누수를 검증한다.
- `dt015_step1/2`는 기본 graph 실행과 에디터 authored round-trip을 검증하지만, Manager lifecycle 자체를
  전용 matrix로 묶지는 않는다.

DT-016은 위 보장을 새 기능 없이 **DialogueManager 계약 중심 회귀 matrix**로 묶는다.

## Design

### Test Location

권장 파일:

- `addons/world_core/dialogtool/RunTime/tests/dt016_step1_manager_lifecycle_test.gd`
- `addons/world_core/dialogtool/RunTime/tests/dt016_step1_manager_lifecycle_test.tscn`

Step 2가 필요하면 문서/완료 리뷰만 수행한다. 제품 코드 변경이 없고 Step 1에서 모든 matrix를 검증하면 DT-016은
2-Step으로 충분하다.

### Test Graph Fixtures

테스트 코드 내부에서 작은 runtime-only `DialogueGraphResource`를 만든다. 영구 `.tres`는 추가하지 않는다.

권장 fixture:

1. `say_then_end(text)`
   - `Start -> Say(text) -> End`
   - Say 진행은 `DialogueManager._ui.dialogue_player.advance()`로 한다.
   - 관찰은 `DialogueManager.ui_request` payload의 `"say"`와 `DialogueManager.dialogue_end`.

2. `choice_then_end(label)`
   - `Start -> Choice([label]) -> End`
   - 선택은 `select_choice(0)`로 한다.
   - Choice 대기 상태에서 교체/종료를 테스트하기 쉽다.

3. `effect_then_say(old_text, new_text)`
   - `Start -> portrait_show(effect) + Say(old_text) -> End`
   - `ui_request` callback에서 첫 `portrait_state`를 받자마자 `DialogueManager.play(say_then_end(new_text))`를 호출한다.
   - 기대: 첫 effect request는 callback을 트리거하기 위해 전달될 수 있지만, 교체 이후 이전 player의 후속
     Say(`old_text`)는 source guard로 차단되고 새 Say(`new_text`)만 전달된다.
   - `portrait_show`는 portrait 렌더 자체가 Non-Goal이므로 빈 `texture_path` 경고를 허용해도 된다. 경고가
     발생하면 테스트 주석/문서에 예상 경고로 명시한다. clean warning log를 원하면 실제 존재하는 Texture2D 경로를
     params에 넣는다.

4. `state_add_choice_graph()`
   - 필요한 경우 same-frame latest-wins provider 격리를 위해 `Choice -> state_add Effect -> End`를 사용한다.
   - provider는 실제 `WorldStateStore` 대신 test-only spy mutation provider를 써도 된다. 다만 mutation provider
     계약 reflection을 통과해야 하므로 메서드 signature와 report shape를 실제 계약에 맞춘다.
   - spy provider는 두 메서드 모두 인자를 untyped로 선언한다. 특히 `add_state(key, delta)`의 둘째 인자
     `delta`는 반드시 untyped여야 한다(`delta: int`처럼 타입을 달면 `_method_accepts`의 `{"types": []}`
     spec을 통과하지 못해 `provider_contract_invalid`가 된다). `apply_state_batch(changes)`도 untyped 또는
     정확히 `Array[Dictionary]`만 안전하다.
   - spy return shape 예: set path는 `{ "applied": true, "diff": [] }`, add path는
     `{ "applied": true, "old_value": old_value, "new_value": new_value }`.

### Observation and Input Seams

- Primary path는 항상 `DialogueManager.play(...)`다.
- Say advance는 `DialogueManager._ui.dialogue_player.advance()`를 직접 호출한다.
- Choice 선택은 `DialogueManager._ui.dialogue_player.select_choice(0)`로 한다.
- 렌더된 `ui.say.text`나 Button click에 의존하지 않는다. type effect timing과 DT-014 paging 검증을 재사용하지 않는다.
- `DialogueManager.ui_request` / `DialogueManager.dialogue_started` / `DialogueManager.dialogue_end`를 로그로
  수집한다. request payload에는 source UI가 없으므로, source guard 검증은 request 내용과 active `_ui` identity,
  old/new player identity, provider spy counters로 간접 단언한다.
- 매 시나리오 시작/종료 시 listener를 명시적으로 connect/disconnect하고 `DialogueManager._dismiss()`로 정리한다.
- **Freed old-player valid window:** 교체 또는 `_dismiss()` 직후 stale old player를 호출해야 하는 테스트는 반드시
  같은 프레임 안에서 호출한다. 순서:
  `play(OLD) -> await로 OLD 시작/대기 도달 -> old_player 캡처 -> play(NEW)(또는 _dismiss()) -> await 없이
  assert is_instance_valid(old_player) -> 즉시 old_player.advance()/select_choice() -> 그 다음 await`.
  `_layer.queue_free()`는 프레임 종료 때 실제 해제되므로, 이 창에서는 old player 호출이 안전하고 source guard를
  의미 있게 검증한다. 한 프레임이라도 await한 뒤 old player를 호출하면 freed instance 접근으로 `SCRIPT ERROR`가
  날 수 있고, `is_instance_valid` 가드로 호출을 스킵하면 테스트 단언이 공허해진다.
- DT-016의 단언 초점은 `DialogueManager` 계약으로 제한한다. 기존 DT-004/DT-009와 겹치는 시나리오라도
  (a) `DialogueManager` signal log/count, (b) active `_ui` identity, (c) spy provider counter만 본다.
  WorldState 값, portrait 렌더 상태, Say paging 렌더는 재검증하지 않는다.

## Steps

### Step 0 — Design Review

Scope:

- 이 문서가 실제 `dialogue_manager.gd`, `dialogue_ui.gd`, `dialogue_player.gd` lifecycle와 맞는지 대조한다.
- 기존 DT-004/DT-008/DT-009/DT-014/DT-015 테스트와 중복되는 부분과 DT-016이 추가로 보장하는 표면을 확인한다.
- 제품 코드, `.tscn`, `.tres`, `project.godot`은 수정하지 않는다.

Done condition:

- 설계 리뷰 판정이 `Approved` 또는 `Approved after design fixes`다.
- 구현 전에 필요한 test seam과 matrix가 확정돼 있다.

### Step 1 — DialogueManager Lifecycle Matrix

Scope:

- `DialogueManager.play` 경로만 사용하는 headless lifecycle 테스트를 추가한다.
- 테스트는 runtime-only graph를 코드에서 생성한다.
- 제품 코드 변경 없이 테스트 추가만 기본으로 한다.

Required tests:

1. **Repeat after end**
   - `say_then_end("A")` 실행 -> Say A -> advance -> `dialogue_end`.
   - 같은 resource 또는 동형 resource를 다시 실행 -> Say A가 다시 정확히 1회 발행.
   - 2회차 시작 직후 `waiting_for == &"text"`, `current_node_id`가 Say 노드, Say A request가 정확히 1회인지
     단언한다. `_on_end()`는 emit 전에 `_dismiss()`로 `_ui = null`을 만들기 때문에 UI identity 비교는 trivial하므로
     핵심 단언으로 쓰지 않는다.
   - `dialogue_started`와 `dialogue_end` count가 run 수와 일치한다.

2. **Replace while waiting for Say**
   - `say_then_end("OLD")` 실행 후 Say 대기 상태에서 `play(say_then_end("NEW"))`.
   - P1 valid-window seam을 따른다: OLD Say 대기 도달 후 `old_player`를 캡처하고, `play(NEW)` 직후 await 없이
     `is_instance_valid(old_player)`를 단언한 뒤 즉시 `old_player.advance()`를 호출한다. 그 다음 await한다.
   - old same-frame `advance()`가 `DialogueManager.ui_request`/`dialogue_end` log를 늘리지 않음을 단언한다.
   - NEW만 advance하면 NEW end가 정확히 1회 발행된다.

3. **Replace/stale-select while waiting for Choice**
   - `choice_then_end("OLD_CHOICE")` 실행 후 Choice 대기 상태에서 `play(say_then_end("NEW"))`.
   - P1 valid-window seam을 따른다: `old_player` 캡처 -> `play(NEW)` -> await 없이
     `is_instance_valid(old_player)` 확인 -> 즉시 `old_player.select_choice(0)` -> 그 다음 await.
   - stale `select_choice(0)`가 manager request/end log를 늘리지 않고, OLD choice flow가 새 실행으로 섞이지 않음을
     단언한다.
   - NEW Say와 NEW end만 정상 진행된다. 이 케이스는 `waiting_for == &"choice"` guard와 `_choice_visible_map`
     stale 선택 경계를 manager source guard 관점에서 검증한다.

4. **Same-frame latest-wins before deferred start**
   - 같은 프레임에 `play(say_then_end("OLD"))` 직후 `play(say_then_end("NEW"))`.
   - 두 process frame 후 request log에는 NEW만 있어야 한다.
   - OLD의 Say/request/end/provider side effect는 0회여야 한다.
   - 이 케이스는 `_dismiss()`의 `cancel_pending_start()`와 `DialogueUI._pending_start` latest-wins를 직접 검증한다.

5. **ui_request callback reentry source guard**
   - `effect_then_say("OLD_AFTER_EFFECT", "NEW")` 실행.
   - 첫 `portrait_state` request callback에서 즉시 `play(say_then_end("NEW"))`.
   - request log에 old first effect는 있을 수 있지만, old 후속 Say(`OLD_AFTER_EFFECT`)는 없어야 한다.
   - NEW Say는 정확히 1회 전달된다.
   - active `_ui`는 NEW UI여야 한다.

6. **dialogue_end callback reentry**
   - `dialogue_end` listener 안에서 `play(say_then_end("NEXT"))` 호출.
   - listener는 one-shot이어야 한다. 첫 end에서 플래그를 세우고 disconnect하거나, 플래그로 이후 end에서는
     `play(NEXT)`를 다시 호출하지 않는다. 그렇지 않으면 NEXT 종료가 같은 listener를 재발화해 무한 재진입/
     watchdog timeout이 될 수 있다.
   - 첫 dialogue end 후 `DialogueManager.is_playing()`이 true이고 active UI가 NEXT Say를 대기한다.
   - NEXT를 advance하면 두 번째 end가 1회만 발행된다.
   - 이 케이스는 `_on_end()`가 `_dismiss()` 후 `dialogue_end.emit()`하는 순서 보장을 검증한다.

7. **Provider tuple isolation on replacement**
   - read/mutation provider를 쓰는 작은 graph 또는 spy provider graph로 OLD/NEW provider를 구분한다.
   - same-frame replace 또는 waiting replace 후 OLD provider 호출 count는 0, NEW provider 호출 count는 기대값이어야 한다.
   - test-only spy mutation provider를 사용한다. WorldStateStore 기반 검증은 DT-009와 중복이 크므로 기본 경로로
     쓰지 않는다. spy signature는 위 Test Graph Fixtures의 untyped provider 계약을 따른다.

8. **Dismiss/null safety**
   - `DialogueManager._dismiss()`를 호출한 뒤 `is_playing() == false`.
   - P1 valid-window seam을 따른다: OLD 대기 도달 후 old player 캡처 -> `_dismiss()` -> await 없이
     `is_instance_valid(old_player)` 확인 -> 즉시 old_player method 호출 -> 그 다음 await.
   - same-frame stale old player 호출이 manager request/end log에 새 이벤트를 추가하지 않음을 단언한다.
   - `play(null)`은 기존 구현상 `push_error` 후 return이다. 이 테스트는 Godot `ERROR` 로그를 만들 수 있으므로
     기본 matrix에서는 제외한다. null resource 정책을 검증하려면 별도 negative test로 분리하고 문서에 로그 발생을
     명시한다.

Done condition:

- 모든 required tests가 headless에서 PASS한다.
- `SCRIPT ERROR:` 0.
- 예상된 `push_warning`이 있다면 테스트명/문서에 명시한다. 불필요한 Godot `ERROR` 로그를 새로 만들지 않는다.
- 임시 resource 파일을 만들지 않는다. 만들었다면 cleanup한다.
- 제품 코드 변경이 없거나, 제품 버그가 발견된 경우 Design Deviation으로 보고 후 사용자 결정에 따른다.

Suggested regression:

- `--import`
- `dt015_step1_integrated_graph_test`
- `dt004_step4_integration_test` 또는 `dt009_step4_e2e_completion_test`

### Step 2 — Documentation and Completion Review

Scope:

- [[DialogueTool]]에 DialogueManager lifecycle 회귀 테스트가 생겼다는 현재 사실을 추가한다.
- [[Current-State]]에 DT-016 완료 사실을 요약한다.
- [[Open-Tasks]]에서 DT-016을 Recently Completed로 이동한다.
- 리뷰 문서 `LLM_WIKI/50_Reviews/DT-016-DialogueManager-Lifecycle-Regression-Review.md`를 작성한다.
- Step 1 완료 조건 대조와 지정 회귀 재실행 결과를 기록한다.

## Failure / Mismatch Policy

- 기존 코드가 required tests를 통과하면 제품 코드는 수정하지 않고 테스트와 문서만 추가한다.
- OLD request/end가 NEW 실행으로 섞이거나, same-frame OLD provider가 호출되거나, end callback reentry에서 NEXT가
  즉시 사라지면 lifecycle 제품 버그 가능성이 높다. Step 1에서 임의로 큰 수정을 하지 말고 Design Deviation으로
  실패 증거와 최소 수정 후보를 보고한다.
- test seam이 잘못된 경우(예: old player를 이미 freed 이후 직접 호출해 의미 없는 경고만 만드는 경우)는 테스트만
  수정한다.
- Godot `ERROR` 로그를 의도적으로 발생시키는 negative case는 기본 matrix에 넣지 않는다. 필요하면 별도 문서화된
  negative test로 분리한다.

## ADR

작성하지 않는다. 이 작업은 새 runtime 정책을 정하는 것이 아니라 기존 `DialogueManager` source guard/latest-wins/
reentry 보장을 회귀 테스트로 고정하는 작업이다. 구현 중 public API나 수명주기 정책 변경이 필요해지면 별도 ADR을
검토한다.

## Open Questions

- provider tuple isolation 방식.
  - 확정: test-only spy mutation provider를 사용한다. spy는 `apply_state_batch(changes)`와
    `add_state(key, delta)`를 untyped 인자로 선언한다. 특히 `add_state`의 `delta`는 untyped 필수다
    (`dialogue_player.gd`의 mutation provider reflection 계약상 typed `delta: int`는 거부됨).
    리턴 shape는 실제 provider 계약에 맞춘다.
- Step 1에서 DT-004의 Portrait callback replace 케이스를 거의 재현하는 것이 중복인가?
  - 권장: 재현하되 assertion을 Portrait 상태가 아니라 `DialogueManager` source guard와 OLD/NEW request log에 맞춘다.

## Completion Criteria

- `DialogueManager.play` 반복/교체/연속/same-frame/reentry lifecycle이 전용 headless matrix로 검증된다.
- stale UI/Player의 지연 `ui_request`/`dialogue_end`가 active Manager signal로 섞이지 않음을 단언한다.
- provider tuple isolation이 검증되거나, 중복/제약으로 제외한 이유가 Task에 기록된다.
- SCRIPT ERROR 0, `--import` 0 parse error, 지정 회귀 GREEN.
- 문서와 리뷰가 완료되고 [[Open-Tasks]]에서 DT-016이 제거되거나 완료로 이동한다.

## Implementation Result

### Step 1 — DialogueManager Lifecycle Matrix (완료)

변경 파일:

- `addons/world_core/dialogtool/RunTime/tests/dt016_step1_manager_lifecycle_test.gd`(신규)
- `addons/world_core/dialogtool/RunTime/tests/dt016_step1_manager_lifecycle_test.tscn`(신규)

**제품 코드 변경 없음**(Design Deviation 없음). 기존 source guard/latest-wins/reentry 보장이 required
tests를 그대로 통과했다.

구현 내용:

- runtime-only `DialogueGraphResource`를 코드에서 생성(영구 `.tres` 없음). fixtures: `say_then_end`,
  `choice_then_end`, `effect_then_say`(portrait_show Effect), `provider_effect_graph`(state_add Effect +
  test-only untyped spy mutation provider).
- Primary path는 `DialogueManager.play(...)`, 진행은 `_ui.dialogue_player.advance()`/`select_choice(0)`,
  관찰은 `DialogueManager.ui_request`/`dialogue_started`/`dialogue_end` recorder(log/count). 렌더 텍스트·
  Button 클릭 비의존.
- 8 required tests 전부 구현: [1] Repeat after end, [2] Replace while waiting Say, [3] Replace/stale-select
  while waiting Choice, [4] Same-frame latest-wins, [5] ui_request callback reentry, [6] dialogue_end
  callback reentry(one-shot), [7] Provider tuple isolation(same-frame), [8] Dismiss/null safety.
- stale old player 호출은 교체/`_dismiss()` 직후 같은 프레임 valid window(`is_instance_valid` true 단언 후
  즉시 호출)에서 수행해 source guard를 의미 있게 검증한다. `play(null)`은 Godot `ERROR` 로그 회피 위해
  기본 matrix 제외.

검증:

- `godot --headless --path . --import` — exit 0, 0 parse error.
- `dt016_step1_manager_lifecycle_test.tscn` — ALL PASS, `SCRIPT ERROR:` 0.
- 완료 회귀 matrix 4/4 ALL PASS: `dt016_step1`, `dt015_step1`, `dt004_step4`, `dt009_step4`(각 `SCRIPT ERROR:` 0).
- 예상 경고: 시나리오 [5] `portrait_show` 빈 `texture_path` `push_warning` 1회(portrait 렌더 Non-Goal,
  테스트 주석 명시). 새 `ERROR` 없음.

### Step 2 — Documentation and Completion Review (완료)

- [[DialogueTool]]에 "DialogueManager Lifecycle Regression (DT-016)" 절 추가.
- [[Current-State]]에 DT-016 완료 사실 요약 추가.
- [[Open-Tasks]]에서 DT-016을 Next → Recently Completed로 이동.
- 리뷰 문서 [[DT-016-DialogueManager-Lifecycle-Regression-Review]] 작성(판정: 완료).

## Related

- [[DialogueTool]]
- [[DT-016-DialogueManager-Lifecycle-Regression-Review]]
- [[DT-004-Nonblocking-Effect-Flow]]
- [[DT-009-State-Mutation-Dialogue-Effects]]
- [[DT-015-Dialogue-Integrated-Regression-Graph]]
- [[STEP_REVIEW_WORKFLOW]]
