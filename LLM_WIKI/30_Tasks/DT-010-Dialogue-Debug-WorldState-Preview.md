---
id: DT-010
type: task
status: deferred
system: DialogueTool, WorldState
created: 2026-06-16
updated: 2026-06-16
tags: [task, dialogue, world-state, editor, debug, preview]
---

# Dialogue Debug WorldState Preview

> Deferred: DialogueTool을 다른 프로젝트에서도 재사용하려면 debug preview보다
> `DialogueTool + WorldState` addon packaging 설계가 먼저 필요하다.
> 이 Task는 [[DT-011-DialogueWorldState-Addon-Packaging]] 이후 새 addon 구조 기준으로 재개한다.

## Goal

DialogueTool 에디터의 테스트 Play에서 `WorldStateCondition`과 `StateSet`/`StateAdd`를 실제 `WorldState`
provider로 실행할 수 있게 한다. 제작자가 에디터에서 `Take -> StateAdd -> Branch -> Rich/Poor` 같은 그래프를
바로 확인할 수 있어야 한다.

## Context

- [[DT-008-State-Condition-Dialogue-Integration]]은 `WorldStateCondition` Data 노드와 조건부 Branch/Choice를
  완료했다.
- [[DT-009-State-Mutation-Dialogue-Effects]]는 `StateSet`/`StateAdd` Effect와 명시적 mutation provider를 완료했다.
- 게임 코드에서는 다음처럼 read/mutation provider를 모두 넘기면 정상 동작한다.

```gdscript
DialogueManager.play(dialogue, WorldState, WorldState)
```

- 그러나 현재 DialogueTool 에디터 테스트 Play는 별도 Godot 프로세스에 다음 인자만 넘긴다.

```text
--scene <scene_path> --dialogue_resource <resource_path> --is_dialogue_debug_mod true
```

- debug mode의 `DialoguePlayer._ready()`는 `--dialogue_resource`를 로드해 `start_dialogue()`만 호출한다.
  read/mutation provider를 주입하지 않는다.
- 결과적으로 에디터 Play에서는:
  - `WorldStateCondition`이 provider 없이 fail-closed
  - `StateSet`/`StateAdd`가 `provider_missing`
  - 실제 제작자가 기대하는 WorldState 연동 흐름을 확인할 수 없다.

## User Outcome

- 제작자가 DialogueTool에서 저장한 `.tres`를 Play 버튼으로 실행한다.
- 프로젝트에 `/root/WorldState` autoload가 있으면 debug 실행에서 read/mutation provider로 자동 주입되거나,
  명시적 옵션으로 주입된다.
- `test.tres` 같은 그래프가 에디터 Play에서 다음처럼 동작한다.

```text
Take  -> StateAdd(actor.example.affinity, +50) -> Branch(actor.example.affinity >= 10) -> Rich
Leave -> mutation 없음                         -> Branch false                         -> Poor
```

- provider 누락/invalid schema/not-ready는 조용히 실패하지 않고 Output에 명확히 표시된다.

## Scope

### Included

- DialogueTool debug Play 경로의 WorldState read/mutation provider 주입
- debug 프로세스 CLI 인자 또는 debug-mode resolver 정책
- `WorldStateRuntime.start_new_game()` 또는 기존 Store 상태 사용 여부에 대한 preview lifecycle 정책
- provider 주입 성공/실패 로그와 최소 진단
- headless 회귀: debug-mode `DialoguePlayer`가 provider를 받아 StateCondition/StateAdd를 함께 실행
- 수동 테스트 시나리오 문서화

### Out of Scope

- 실제 SaveGame file/slot
- 에디터 안에서 WorldState 값을 편집하는 Inspector UI
- schema-aware key picker
- condition/mutation trace inspector UI
- Dialogue runtime 일반 경로의 provider 계약 변경
- DT-009 StateSet/StateAdd 동작 변경

## Design Questions

Step 0에서 확정한다.

1. **Provider source**
   - Option A: debug mode에서 `/root/WorldState`를 1회 resolve해 read/mutation provider로 주입한다.
   - Option B: `DialogueManager.play(..., WorldState, WorldState)`와 같은 별도 debug runner를 만든다.
   - 기본 권장: A. debug-only 경로이며 기존 runtime의 provider 주입 계약을 유지한다.

2. **Lifecycle/reset policy**
   - Option A: debug Play 시작 시 `WorldStateRuntime.start_new_game()`을 호출해 매번 deterministic default에서 시작.
   - Option B: autoload Store의 현재 값을 그대로 사용.
   - Option C: 에디터 옵션/toggle로 선택.
   - 기본 권장: Step 1은 A 또는 C의 "reset on play" 기본값. 반복 수동 테스트의 예측 가능성이 중요하다.

3. **Failure policy**
   - `/root/WorldState` 없음, Store not-ready, invalid schema, mutation provider 계약 위반 시 어떻게 보여줄지.
   - 기본 권장: debug Output에 명시적 `push_warning`/`push_error`, 대화는 기존 fail-closed 계약대로 계속.

4. **Scene prerequisite**
   - 현재 Play는 사용자가 지정한 Scene을 `--scene`으로 실행한다. 그 Scene에 debug-mode `DialoguePlayer` 또는
     `DialogueUI`가 있어야 한다.
   - provider 주입 위치가 `DialoguePlayer._ready()`라면 어떤 Scene에서도 해당 Player가 존재할 때만 동작한다.
   - 별도 debug runner scene을 만들지 여부를 Step 0에서 검토한다.

## Steps

### Step 0: Design Review

목표:
- 현재 debug Play 코드(`dialoguetool_main.gd`, `dialogue_player.gd`, `dialoguetool_util.gd`)와 DT-008/009 provider
  계약을 대조해 provider source/lifecycle/failure policy를 확정한다.

완료 조건:
- P0/P1 설계 문제가 없고, debug Play가 WorldState provider를 어디서 어떻게 얻는지 구현 가능한 수준으로 고정된다.
- 필요하면 ADR-011을 작성한다.

검증 방법:
- [[Design-Review-Prompt]] 형식의 코드 대조 리뷰

### Step 1: Debug Provider Injection

목표:
- debug mode 실행 시 `DialoguePlayer`가 `start_dialogue()` 전에 read/mutation provider를 세팅한다.

완료 조건:
- `/root/WorldState`가 있으면 `has_read_state_provider()`와 `has_mutation_state_provider()`가 true가 된다.
- `WorldStateCondition` + `StateAdd`가 있는 리소스에서 선택 후 다음 Branch가 변경값을 읽는다.
- provider가 없으면 SCRIPT ERROR 없이 명확한 debug failure 로그를 낸다.

검증 방법:
- headless debug-mode fixture
- DT-008/DT-009 핵심 회귀

선행 조건: Step 0 승인

### Step 2: Preview Lifecycle and Reset Policy

목표:
- 에디터 Play 반복 실행이 예측 가능하도록 WorldState 초기화 정책을 적용한다.

완료 조건:
- reset-on-play 또는 documented current-state policy가 적용된다.
- 같은 dialogue를 연속 실행해도 이전 debug run의 mutation이 다음 run을 오염하지 않는다.
- SESSION/SAVE lifecycle 정책이 DT-006과 충돌하지 않는다.

검증 방법:
- 반복 실행 headless
- `WorldStateRuntime.start_new_game()` 또는 선택한 policy의 직접 단언

선행 조건: Step 1 리뷰 완료

### Step 3: Editor Play E2E and Docs

목표:
- 실제 에디터 Play에 가까운 경로에서 수동 제작 시나리오를 검증하고 문서를 갱신한다.

완료 조건:
- `test.tres` 스타일 그래프가 debug Play로 `Take -> Rich`, `Leave -> Poor`를 재현한다.
- provider 성공/실패 로그가 문서화된다.
- User Guide에 "에디터 Play로 WorldState 테스트하는 법"이 추가된다.
- DT-008/009 회귀와 headless editor load가 성공한다.

검증 방법:
- e2e debug-mode test
- 수동 테스트 절차

선행 조건: Step 2 리뷰 완료

## Completion Criteria

- DialogueTool Play 버튼으로 WorldState read/mutation이 포함된 대화를 직접 확인할 수 있다.
- provider 주입은 debug preview 경로에만 영향을 주며 일반 runtime provider 계약을 흐리지 않는다.
- 반복 실행이 예측 가능하고, provider missing/not-ready 실패가 명확하다.
- Task/System/User Guide/Review 문서가 현재 동작과 일치한다.

## Related

- [[DT-008-State-Condition-Dialogue-Integration]]
- [[DT-009-State-Mutation-Dialogue-Effects]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[ADR-010-State-Mutation-Dialogue-Effects]]
- [[DialogueTool]]
- [[World-State-System]]
