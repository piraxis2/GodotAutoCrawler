---
id: ADR-012
type: decision
status: accepted
date: 2026-06-16
system: DialogueTool, WorldState
---

# Dialogue Debug Preview Provider

## Context

DT-008/DT-009로 `WorldStateCondition` Data 노드와 `StateSet`/`StateAdd` Effect가 구현됐다. 실제 게임
경로는 `DialogueManager.play(dialogue, WorldState, WorldState)`로 read/mutation provider를 주입해 동작한다.
그러나 DialogueTool 에디터 Play/Debug 실행은 provider를 주입하지 않아 condition은 fail-closed,
mutation은 `provider_missing`이 된다.

코드 대조로 확인한 실행 경로:

- `addons/dialogtool/dialoguetool_main.gd` `_on_button_button_up()`이 리소스를 저장한 뒤
  **별도 Godot 프로세스**를 `OS.execute_with_pipe(godot_executable, [..., "--dialogue_resource", path,
  "--is_dialogue_debug_mod", "true", "--remote-debug", addr], false)`로 띄운다.
- 서브프로세스의 `dialogue_player.gd` `_ready()`가 `DialogueToolUtil.is_dialogue_debug_hint()`를 보고
  `cmd_arguments["dialogue_resource"]`를 로드해 **`start_dialogue.call_deferred(resource)`를 직접 호출**한다.
  `DialogueManager.play()`/`DialogueUI.play()`를 거치지 않으므로 `set_read_state_provider`/
  `set_mutation_state_provider`가 한 번도 불리지 않는다.

확인한 핵심 기술 사실:

- 서브프로세스는 호스트 `project.godot` 기준으로 autoload를 로드한다. 이 저장소엔 `WorldState`가 있지만,
  DT-011(ADR-011 D4)에 따라 **`WorldState`/`WorldStateRuntime`는 호스트 수동 등록**이라 addon만 복사한
  fresh 프로젝트엔 없다.
- `dialogue_player.gd`/`dialogue_manager.gd`는 `DialogueToolUtil` autoload **식별자에 parse-time 의존**한다
  (DT-011 Step 4 발견). 같은 방식으로 `WorldState`를 bare 식별자로 참조하면 autoload 미등록 프로젝트에서
  스크립트 parse 자체가 실패한다.
- `WorldStateStore`는 read facade(`has_state`/`read_state`/`try_read_state`)와 mutation facade
  (`apply_state_batch`/`add_state`)를 **모두** 구현한다(`world_state_store.gd`). 인스턴스 1개를
  read·mutation provider 양쪽으로 전달할 수 있다.
- DT-011로 `examples/world_state_schema_example.tres`(6-key, `actor.example.affinity` INT 포함)와
  sample dialogue(`examples/sample_dialogues/sample_world_state_dialogue.tres`,
  `affinity_ge_10` ConditionSet + `state_add(+50)` 소비)가 addon 내부에 포함됐다.
- Play마다 별도 OS 프로세스가 뜨므로 **프로세스 수명 = preview 1회**다.

## Decision

### D1. Provider source — addon example store (후보 A)

debug preview는 `examples/world_state_schema_example.tres`로 debug 전용 `WorldStateStore`를 구성해
read·mutation provider 양쪽으로 주입한다. autoload `/root/WorldState`에 의존하지 않는다.

근거: addon 복사 직후 동작(재사용 1순위 목표), 실제 game/save state 격리, parse-safety. 한계는 사용자
게임 schema key로 작성한 대화가 example store에 그 key가 없어 fail-closed/`unknown_key`가 되는 점이며,
User Guide에 명시하고 옵션 C로 보완한다.

### D2. Parse-safety — class_name만, autoload bare 식별자 금지

debug 코드는 `WorldState`/`WorldStateRuntime`를 bare 전역 식별자로 참조하지 않는다. store는
`class_name WorldStateStore`(경로 독립, addon 내부 포함이라 항상 parse 가능)로 `WorldStateStore.new()`
생성한다. autoload가 필요한 경우(옵션 C)에도 `get_node_or_null("/root/WorldState")` 런타임 lookup만 쓴다.
fresh 프로젝트에서 debug 부팅 스크립트가 parse·boot되어야 한다(Step 1 완료조건/게이트).

### D3. 주입 위치 — DialoguePlayer._ready() debug 분기

`start_dialogue.call_deferred` 직전에 `set_read_state_provider(store)` / `set_mutation_state_provider(store)`를
동기 호출한다(set 동기 + start deferred → 순서 안전). `DialogueManager.play()`로 전환하지 않는다 —
Manager가 자기 CanvasLayer + DialogueUI + DialoguePlayer를 추가 생성해 사용자 `--scene`의 player와
이중화되고 디버그 하이라이트(`current_node_changed`)가 충돌하기 때문이다. `DialogueUI`의 latest-wins
`_deferred_start`는 `UI.play` 전용 경로라 이 직접 boot와 충돌하지 않는다.

### D4. Lifecycle/reset — 프로세스 격리 의존

Play마다 새 프로세스가 뜨므로 store가 매번 default에서 시작한다(결정론적). `WorldStateRuntime.start_new_game()`
/coordinator는 끌어들이지 않는다(추가 autoload 의존 회피). bare store는 `initialize()` 후 SAVE/SESSION 모두
default이고, ConditionEvaluator/mutation은 store-ready만 요구한다. 1회 run 내 mutation은 누적 유지되어
"mutation 직후 다음 Branch/Condition이 변경값을 읽는" 흐름을 만족한다. 동시 preview는 프로세스 격리로 안전하다.

### D5. Store 구성 소유 위치 — debug helper / util (Step 1 확정)

example schema 경로와 store 생성은 `DialogueToolUtil`(또는 신규 debug-preview helper)에 두고,
런타임 `DialoguePlayer`의 debug 분기는 provider만 받는다. 런타임 player가 example resource/preview 지식을
직접 갖지 않게 한다(설계 경계). 구체적 소유 위치는 Step 1 구현에서 최종 확정한다.

### D6. Failure policy — 원격 디버그 Output 채널 + fail-closed

`OS.execute_with_pipe(..., false)`는 non-blocking이라 서브프로세스 stdout을 스트리밍하지 않지만,
`--remote-debug`로 `push_warning`/`push_error`가 에디터 Output/Debugger 패널로 전달된다. store 구성/schema/
init/provider 구성 실패는 명시적 `push_error` + provider null 유지로 fail-closed한다(자동 true/자동 mutation
없음). condition은 false, mutation은 `provider_missing`으로 기존 계약대로 Flow를 계속한다. UI warning과
`condition_evaluated`/`state_mutation_evaluated` report 노출은 후속(Step 3).

### D7. Scene prerequisite — 전제 불변, 별도 runner scene 미도입

현재 Play는 사용자 `--scene`을 실행하고 그 Scene에 `DialoguePlayer`가 있어야 하이라이트가 동작한다.
provider 주입이 `DialoguePlayer._ready`에 있으므로 player가 존재하는 Scene이면 동작한다. 별도 debug
runner scene은 도입하지 않는다(후속 검토).

## Consequences

### Positive

- addon만 복사한 fresh 프로젝트에서도 에디터 Play로 condition/mutation preview가 동작한다.
- 실제 game/save runtime 상태를 오염시키지 않는다(별도 store 인스턴스).
- bare autoload 식별자 의존이 없어 parse error 위험이 없다.
- 기존 게임 runtime provider 주입 계약을 흐리지 않는다(debug 경로 전용).

### Negative

- 사용자 게임 schema key로 작성한 대화는 고정 example schema preview로 검증되지 않는다(옵션 C로 보완).
- debug boot가 store 구성 책임을 추가로 진다(helper로 격리).

## Alternatives Rejected

- **후보 B(`/root/WorldState` autoload):** fresh 프로젝트에 autoload가 없어 깨지고, bare 식별자 참조 시
  parse error 위험(ADR-011 D4) → 거부.
- **`DialogueManager.play()`로 debug boot 전환:** Manager가 UI/Player를 추가 생성해 `--scene`의 player와
  이중화, 디버그 하이라이트 충돌 → 거부(D3).
- **`WorldStateRuntime.start_new_game()` 기반 lifecycle:** 추가 autoload 의존 + 프로세스 격리로 이미 결정론적
  → 거부(D4).
- **옵션 C(project schema toggle)를 MVP에 포함:** 가치 있으나 범위 확대 → Step 3/후속으로 미룸(D1).

## Review Gate

[[DT-010-Dialogue-Debug-WorldState-Preview]] Step 0 설계 리뷰(2026-06-16)에서 실행 경로를 코드 대조로
확인하고 provider source/parse-safety/주입 위치/lifecycle을 확정해 **accepted**로 전환했다.
판정: Approved after design fixes(P1 3건 — provider source=example store, parse-safety, 주입 위치 —
은 Step 1 착수 전 선반영).

## Related

- [[DT-010-Dialogue-Debug-WorldState-Preview]]
- [[ADR-011-DialogueWorldState-Addon-Packaging]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[ADR-010-State-Mutation-Dialogue-Effects]]
- [[DialogueTool]]
- [[World-State-System]]
