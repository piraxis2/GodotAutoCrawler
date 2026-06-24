---
id: DT-010
type: task
status: completed
system: DialogueTool, WorldState
created: 2026-06-16
updated: 2026-06-16
tags: [task, dialogue, world-state, editor, debug, preview]
---

# Dialogue Debug WorldState Preview

> DT-011 패키징 완료로 재개됨. Step 0 설계 리뷰 완료(2026-06-16, 판정: Approved after design fixes,
> [[ADR-012-Dialogue-Debug-Preview-Provider]] accepted). Step 1·2 구현·리뷰 완료(판정: 완료).
> Step 3 구현·리뷰 완료(판정: 완료). DT-010 완료.

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

## Design Questions (Step 0 확정 — [[ADR-012-Dialogue-Debug-Preview-Provider]])

Step 0 설계 리뷰(2026-06-16)에서 코드 대조 후 아래로 확정했다. 결정 근거는 ADR-012.
> 주의: DT-011 이전 초안의 provider 후보(Option A=`/root/WorldState`)는 **폐기**됐다. fresh 프로젝트
> 복사 직후 동작 + 실제 game state 격리 + parse-safety 요구로 addon-내부 example store(아래 후보 A)로 전환.

1. **Provider source → 후보 A 확정(addon example store).**
   - 후보 A: debug preview 전용 `WorldStateStore`를 `examples/world_state_schema_example.tres`로 구성해
     read·mutation provider로 주입(store facade가 양쪽 계약을 모두 만족 — 인스턴스 1개를 둘 다로 전달).
   - 후보 B(`/root/WorldState` autoload): fresh 프로젝트에 autoload가 없어 깨지고, bare 식별자 참조 시
     parse error 위험(ADR-011 D4) → **거부**.
   - 후보 C(toggle로 autoload store 선택): 가치 있으나 autoload는 `get_node_or_null`로 parse-safe하게
     접근하는 **Step 3 옵션**으로 미룬다.
   - 재현성·자급·격리·parse-safety 모두 A 우위. trade-off: 사용자 게임 schema key로 작성한 대화는
     example store에 그 key가 없어 condition fail-closed / mutation `unknown_key`(고정 example schema 한계,
     User Guide에 명시 — 옵션 C로 보완).

2. **Parse-safety(P1, ADR-012 D2).** debug 코드는 `WorldState`/`WorldStateRuntime`를 **bare 전역 식별자로
   참조하지 않는다**. store는 `class_name WorldStateStore`(경로 독립, addon 내부 포함이라 항상 parse 가능)로
   `WorldStateStore.new()` 생성하고, autoload가 필요하면 `get_node_or_null("/root/WorldState")` 런타임
   lookup만 쓴다. (`dialogue_player.gd`가 `DialogueToolUtil` 식별자에 parse-time 의존하는 기존 함정을
   WorldState로 확장하지 않기 위함 — DT-011 Step 4 발견.)

3. **주입 위치 → `DialoguePlayer._ready()` debug 분기(P1).** debug boot는 player가 자기 `_ready`에서
   `start_dialogue.call_deferred`로 self-start한다. `start_dialogue` 직전에 `set_read_state_provider(store)`
   / `set_mutation_state_provider(store)`를 동기 호출한다(set 동기 + start deferred → 순서 안전).
   `DialogueManager.play()`로 바꾸지 않는다(Manager가 자기 CanvasLayer+UI+Player를 추가 생성해 사용자
   `--scene`의 player와 이중화 → 하이라이트 충돌). `DialogueUI`의 latest-wins `_deferred_start`는 `UI.play`
   전용이라 이 직접 boot와 충돌하지 않는다.

4. **Lifecycle/reset → 프로세스 격리 의존(P2).** Play마다 별도 Godot 프로세스가 뜨므로 store가 매번 default에서
   시작한다(결정론적). `WorldStateRuntime.start_new_game()`/coordinator는 끌어들이지 않는다(추가 autoload
   의존 회피). bare store는 `initialize()` 후 SAVE/SESSION 모두 default이고 ConditionEvaluator/mutation은
   store-ready만 요구한다. 1회 run 내 mutation은 누적 유지(= "mutation 직후 다음 Branch가 변경값 읽기" 충족).

5. **Store 구성 소유 위치(Open — Step 1에서 확정).** example schema 경로/store 생성을 `DialogueToolUtil`
   (또는 신규 debug helper)에 두고 `DialoguePlayer` debug 분기는 provider만 받는 형태를 권장(런타임 player가
   example resource를 모르게 — 설계 경계). 대안은 player debug 분기 직접 구성(단순하나 경계 흐림).

6. **Failure policy → 원격 디버그 Output 채널 + fail-closed 유지.** `OS.execute_with_pipe(..., false)`는
   non-blocking이라 서브프로세스 stdout을 스트리밍하지 않지만, `--remote-debug`로 `push_warning`/`push_error`가
   에디터 Output/Debugger 패널로 전달된다. store 구성/schema/init 실패는 명시적 `push_error` + provider null
   유지(자동 true/자동 mutation 없음). condition은 false, mutation은 `provider_missing`으로 기존 fail-closed
   계약대로 계속한다. UI warning은 후속(Step 3).

7. **Scene prerequisite.** 현재 Play는 사용자 `--scene`을 실행하고 그 Scene에 `DialoguePlayer`가 있어야
   하이라이트가 동작한다(전제 불변). provider 주입이 `DialoguePlayer._ready`에 있으므로 player가 존재하는
   Scene이면 동작한다. **별도 debug runner scene은 도입하지 않는다**(전제 불변, 후속 검토).

## Steps

### Step 0: Design Review — 완료 (Approved after design fixes)

목표:
- 현재 debug Play 코드(`dialoguetool_main.gd`, `dialogue_player.gd`, `dialoguetool_util.gd`)와 DT-008/009 provider
  계약을 대조해 provider source/lifecycle/failure policy를 확정한다.

결과(2026-06-16):
- 실행 경로 확정: 에디터 Play(`dialoguetool_main.gd`)가 별도 Godot 프로세스를 띄우고, 서브프로세스의
  `DialoguePlayer._ready()`(debug-hint)가 `start_dialogue.call_deferred`를 **직접** 호출한다
  (`DialogueManager`/`DialogueUI` 미경유 → provider 미주입).
- 결정(ADR-012 accepted): provider source=**addon example store**(후보 A), parse-safe(`class_name`만,
  autoload bare 식별자 금지), 주입 위치=`DialoguePlayer._ready` debug 분기, lifecycle=프로세스 격리 의존.
- P1 3건(provider source/parse-safety/주입 위치)을 Step 1 착수 전 선반영하도록 확정.

검증 방법:
- [[Design-Review-Prompt]] 형식의 코드 대조 리뷰 — 완료.

### Step 1: Debug Provider Injection

목표:
- debug mode 실행 시 `DialoguePlayer`가 `start_dialogue()` 전에 addon example store를 read/mutation provider로
  주입한다(autoload 비의존, parse-safe).

작업 범위:
- `examples/world_state_schema_example.tres`로 `WorldStateStore`(`class_name`)를 구성해 read·mutation provider로
  주입. store 구성 위치는 ADR-012 D5(helper/util) 결정에 따른다.
- `WorldState`/`WorldStateRuntime`를 bare 전역 식별자로 참조하지 않는다.

완료 조건:
- debug boot에서 example store 구성 성공 시 `has_read_state_provider()`와 `has_mutation_state_provider()`가
  true가 된다.
- `WorldStateCondition` + `StateAdd`가 있는 리소스에서 선택 후 다음 Branch가 변경값을 읽는다.
- store 구성/schema 실패 시 SCRIPT ERROR 없이 명확한 debug failure 로그를 내고 fail-closed로 계속한다.
- **fresh 프로젝트(WorldState autoload 미등록)에서도 debug 부팅 스크립트가 parse·boot된다.**

검증 방법:
- headless debug-mode fixture(`DialogueToolUtil.cmd_arguments` 주입 시뮬레이션)
- fresh-project parse-safety 회귀(autoload 미등록)
- DT-008/DT-009 핵심 회귀

선행 조건: Step 0 승인([[ADR-012-Dialogue-Debug-Preview-Provider]])

#### 구현 결과 (2026-06-16 — 코드 리뷰 완료, 판정: 완료)

> 코드 리뷰(2026-06-16): 실행 경로/signal/lifecycle/재진입 추적 + 헤드리스 재현(38/38 PASS).
> P0/P1/P2 없음. P3 1건(preview store Node가 프로세스 내 명시적 free 없이 보유 → 프로세스 격리
> teardown 의존)은 ADR-012 D4와 일치, Step 2 lifecycle 결정으로 해소. 완료 조건 4개 충족 → **완료**.

**변경 파일.**
- `addons/dialogtool/RunTime/dialogue_debug_preview_provider.gd` (신규): preview store 구성 helper.
- `addons/dialogtool/RunTime/dialogue_player.gd`: `_ready()` debug 분기에 provider 주입,
  `_inject_debug_preview_provider()` 추가.
- `addons/dialogtool/RunTime/tests/dt010_step1_debug_preview_provider_test.gd(.tscn)` (신규): Step 1 헤드리스 테스트.

**helper 위치 결정(ADR-012 D5 확정 — 신규 helper 파일).** `DialogueToolUtil`(util grab-bag)에 두지
않고 신규 `DialogueDebugPreviewProvider`(`class_name`, `extends RefCounted`, static
`make_preview_store(schema_path := SCHEMA_PATH)`)로 분리했다. 이유: (1) preview store/example schema 지식을
한 책임으로 격리해 런타임 `DialoguePlayer`가 example resource를 모르게 한다(설계 경계). (2) `schema_path`
인자로 실패 경로를 헤드리스로 직접 검증할 수 있다(테스트 seam). (3) `DialogueToolUtil`은 `@tool` +
cmd-arg/property util 성격이라 preview store 책임과 무관 — 혼입 회피.

**구현 내용.**
- helper: `load(schema_path)` → null/`StateSchema` 아님 가드 → `WorldStateStore.new()` +
  `store.schema = schema` + `initialize()`. 성공 시 ready store 반환, 실패는 모두 `push_error` +
  `null`(자동 true/자동 mutation 없음, ADR-012 D6). store는 read·mutation 양쪽 계약을 만족하므로 한
  인스턴스를 양쪽으로 쓴다(D1).
- player: debug 분기(`is_dialogue_debug_hint()` + `dialogue_resource` 존재)에서 `start_dialogue`
  **직전**에 `_inject_debug_preview_provider()` 동기 호출 → helper로 store 구성 → `set_read_state_provider`/
  `set_mutation_state_provider`. set 동기 + start deferred라 순서 안전(D3). 이미 provider가 있으면
  덮어쓰지 않고, store가 null이면 `push_warning` 후 미주입(fail-closed 유지).
- parse-safety(D2): helper·player 모두 `WorldStateStore`/`StateSchema` `class_name`과 string path만 쓰고
  bare `WorldState`/`WorldStateRuntime` autoload 식별자를 추가하지 않는다.
- lifecycle(D4): store는 tree에 추가하지 않고 player가 참조로 보유 → 프로세스 격리(Play=새 프로세스)에
  의존해 매번 default에서 시작. 1회 run 내 mutation은 누적(Take→affinity 누적→Branch 반영).

**검증.**
- Godot 4.6.3 mono headless `--import`: 0 parse/script error, `DialogueDebugPreviewProvider`/
  `DialoguePlayer` global class 등록 정상.
- `dt010_step1_debug_preview_provider_test` ALL PASS:
  - A helper 성공: example store ready, `actor.example.affinity` default 0, read/mutation 메서드 동작.
  - B 실패 경로: 존재X path / 비-`StateSchema`(sample dialogue) / invalid schema(version 0) 모두 null,
    크래시 없음(push_error만).
  - C debug 주입 Take: 실제 `_ready` debug 분기 → read==mutation provider(같은 인스턴스) → StateAdd
    applied(error `&""`, new 50, mutation 1회) → state_condition valid·passed·read_count>0 → "Rich".
  - D Leave: mutation 0회, affinity 0 유지, condition valid·passed=false → "Poor".
  - E 프로세스 격리 proxy: run1 affinity 50, run2 별도 store default 0 → "Poor".
  - F 일반 경로(debug hint 없음): provider 미주입(`has_read/mutation_state_provider`==false).
- 회귀 ALL PASS: dt008_step3(Branch e2e), dt008_step5(완료), dt009_step2(runtime mutation),
  dt009_step4(Choice→state_add→Branch e2e).
- parse-safety 코드 검색: 제품 코드에 bare `WorldState`/`WorldStateRuntime` 식별자 0건(comment/string 제외).

**남은 위험.**
- 실제 에디터 GUI Play로 sample dialogue를 재생하는 e2e는 미수행(헤드리스 debug-hint 시뮬레이션으로 대체).
  GUI Play 경로 검증은 Step 3 범위.
- preview store(Node)를 tree에 추가하지 않아 프로세스 내 명시적 free가 없다 — 프로세스 격리(Play=1회 프로세스)
  teardown에 의존(헤드리스 테스트는 추적 후 free). Step 2에서 lifecycle/reset 정책을 명시 검증.
- 고정 example schema 한계: 사용자 게임 schema key로 작성한 대화는 preview store에 그 key가 없어
  fail-closed/`unknown_key`(ADR-012 D1, 옵션 C로 보완 — Step 3).
- `--remote-debug` Output 채널로 `push_error`/`push_warning`이 에디터에 전달되는지는 헤드리스에서 확인 불가
  (Step 3 e2e/수동 절차).

### Step 2: Preview Lifecycle and Reset Policy

목표:
- 에디터 Play 반복 실행이 예측 가능함을 프로세스 격리 정책으로 확정·검증한다(별도 reset 로직 최소화).

작업 범위:
- 프로세스 격리에 의존(ADR-012 D4): Play마다 새 프로세스 → store가 default에서 시작. coordinator/
  `start_new_game` 미도입. 1회 run 내 mutation은 누적 유지.

완료 조건:
- 같은 dialogue를 연속 Play(=연속 프로세스)해도 이전 run의 mutation이 다음 run을 오염하지 않는다.
- store re-init이 default에서 시작함을 직접 단언한다.
- DT-006 SESSION/SAVE lifecycle과 충돌하지 않는다(bare store는 default SAVE/SESSION).

검증 방법:
- 반복 boot headless(store가 매번 default 단언)
- 1회 run 내 mutation 누적(Take→affinity 누적→Branch 반영) 단언

선행 조건: Step 1 리뷰 완료

#### 구현 결과 (2026-06-16 — 리뷰 완료, 판정: 완료, 제품 코드 변경 없음)

**범위 판단.** ADR-012 D4가 coordinator/`start_new_game`/별도 reset 로직 미도입을 확정했으므로 Step 2는
DT-008 Step3/5·DT-009 Step4와 동일한 **검증 전용 단계**다(제품 코드 변경 없음). Step 1 리뷰의 P3(orphan
store)는 lifecycle 결정으로 해소: preview store(Node)는 **프로세스 격리가 정리 경계**이므로 프로세스 내
명시적 free를 두지 않는다(debug Play=1회 프로세스, teardown에서 회수). 헤드리스 테스트는 store를
test-owned로 추적·free한다.

**변경 파일.**
- `addons/dialogtool/RunTime/tests/dt010_step2_preview_lifecycle_test.gd(.tscn)` (신규): Step 2 헤드리스 검증.

**검증.**
- `--import` 0 parse/script error.
- `dt010_step2_preview_lifecycle_test` ALL PASS:
  - A 반복 boot(연속 프로세스 proxy): 3회 run 모두 mutation 전 affinity default 0 → Take 후 50,
    매 run 별도 store 인스턴스(이전 run mutation 미오염).
  - B store re-init 직접 단언: affinity 50 / session.intro.seen true로 변경 후 `initialize()` →
    affinity 0 / session false 복귀, ready 유지.
  - C 1회 run 내 mutation 누적: add+50, add+50 → 100, 같은 preview store read provider로
    condition(affinity>=100) valid·passed·read_count>0.
  - D bare store SAVE/SESSION 모두 default: quest.main.stage 0 / player.health 100.0 /
    actor.example.affinity 0 / world.build.channel "dev"(SAVE) + session.intro.seen false(SESSION) —
    coordinator/`start_new_game` 없이 bare `initialize()`로 양쪽 lifetime default.
  - E /root/WorldState autoload와 별도 인스턴스: preview store ≠ autoload store, preview mutation이
    autoload store를 건드리지 않음(실제 save state 격리).
- 회귀: dt010_step1 ALL PASS(재현), dt006_step1_bootstrap(autoload example schema 부팅) ALL PASS —
  제품 코드 무변경이라 DT-004~009 구조적 무영향.

**남은 위험.**
- 실제 에디터 GUI Play 반복 재생(연속 프로세스)은 미수행 — 헤드리스 반복 boot proxy로 대체(Step 3 수동 절차).
- 고정 example schema 한계(게임 schema key 미해결)는 Step 3/옵션 C로 보완.

### Step 3: Editor Play E2E and Docs

목표:
- 실제 에디터 Play에 가까운 경로에서 수동 제작 시나리오를 검증하고 문서를 갱신한다.

완료 조건:
- addon sample(`examples/sample_dialogues/sample_world_state_dialogue.tres`) 그래프가 debug Play로
  `Take -> Rich`, `Leave -> Poor`를 재현한다.
- provider 성공/실패 로그가 문서화되고, 고정 example schema 한계(게임 schema key 미해결)를 User Guide에 명시한다.
- User Guide에 "에디터 Play로 WorldState 테스트하는 법"이 추가된다.
- (옵션 C) project schema path를 debug 설정으로 받는 toggle을 검토·문서화한다(autoload는 `get_node_or_null`로
  parse-safe 접근). 범위 초과 시 후속으로 명시.
- DT-008/009 회귀와 headless editor load가 성공한다.

검증 방법:
- e2e debug-mode test
- 수동 테스트 절차

선행 조건: Step 2 리뷰 완료

#### 구현 결과 (2026-06-16 — 리뷰 완료, 판정: 완료)

**변경 파일.**
- `addons/dialogtool/RunTime/tests/dt010_step3_editor_play_e2e_test.gd(.tscn)` (신규): 실제 Dialogue_UI 씬
  debug Play e2e.
- `LLM_WIKI/20_Systems/DialogueTool-User-Guide.md`: 13절에 "에디터 Play로 WorldState 미리보기 테스트하기"
  추가(provider 자동 주입/lifecycle/진단 로그/고정 example schema 한계), 14절에 13절 cross-reference.

**e2e(완료조건 #1).** Step 1 bare player와 달리 실제 `Dialogue_UI.tscn`(그 안의 child `DialoguePlayer`)을
debug-hint로 띄워 sample dialogue를 self-start한다 — 실제 서브프로세스 씬 형태(UI가 player를 품음)와 일치.
child player `_ready` debug 분기가 preview provider를 주입하고 deferred self-start, 부모 UI `_ready`가
signal 연결 후 start 발화. `dt010_step3_editor_play_e2e_test` ALL PASS:
- A Take: UI player에 provider 주입(read==mutation 같은 인스턴스), choice 대기 도달, StateAdd +50 →
  state_condition true → `ui.say.text`(실제 렌더 라벨)·중계 ui_request 모두 "Rich", affinity 50.
- B Leave: mutation 없음 → state_condition false → 라벨/요청 모두 "Poor", affinity 0.
- DialogueUI 공존에서 이중 start/이중 provider 충돌 없음(ADR-012 D3 확인).

**문서(완료조건 #2/#3).** User Guide 13절에 (a) preview provider 자동 주입 동작, (b) 동봉 sample
Take→Rich/Leave→Poor, (c) 프로세스 격리 lifecycle(매 Play default·run 내 누적), (d) 진단 로그
(`push_error`+미주입+fail-closed, `--remote-debug` Output 전달), (e) **고정 example schema 한계**
(게임 schema key는 `state_missing`/`unknown_key`)를 명시. 14절에서 에디터 Play는 provider 수동 주입
불필요함을 cross-reference.

**옵션 C 검토(완료조건 #4).** 게임 schema 경로를 debug 설정으로 주입하는 옵션은 parse-safe하게 구현
가능하다(autoload는 `get_node_or_null` 런타임 lookup, ADR-012 D2). 그러나 debug 설정 UI/우선순위/검증이
범위를 키우므로 **후속 작업으로 미룬다**. User Guide 한계 절과 Open-Tasks에 후속으로 명시.

**검증(완료조건 #5).** `--import` 0 parse/script error(headless editor load). 전체 회귀
**18/18 scene ALL PASS**: DT-004(step1~4+pipeline 5), DT-008(step1~5+spike 6), DT-009(step2/3/3b/4 4),
DT-010(step1/2/3 3).

**남은 위험.**
- 실제 GUI 클릭/`--remote-debug` Output 채널 전달은 헤드리스에서 직접 검증 불가 — 아래 수동 테스트 절차로 보완.
- 고정 example schema 한계는 옵션 C(후속)로만 해소.

#### 코드 리뷰 결과 (2026-06-17)

판정: **완료**([[DT-010-Dialogue-Debug-WorldState-Preview-Review]]).

P0/P1/P2 발견 사항 없음. 실제 `Dialogue_UI.tscn` child `DialoguePlayer` debug self-start 경로, preview
provider 주입 순서, parse-safety, 문서 한계 설명을 ADR-012와 대조했다. 옵션 C(게임 schema 경로 debug 주입)는
현재 Step 범위를 키우는 독립 기능이므로 `Open-Tasks`의 **Later** 후속으로 유지한다.

재검증:
- Godot 4.6.3 mono headless `--import`: exit 0, parse/class error 없음.
- 선택 회귀 5/5 PASS:
  - `addons/dialogtool/RunTime/tests/dt010_step1_debug_preview_provider_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt010_step2_preview_lifecycle_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt010_step3_editor_play_e2e_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt008_step3_branch_e2e_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt009_step4_e2e_completion_test.tscn`

##### 수동 테스트 절차 (에디터 GUI)

1. Godot 에디터에서 `addons/dialogtool/examples/sample_dialogues/sample_world_state_dialogue.tres`를
   `Dialogue` 화면으로 Load한다.
2. 실행 Scene을 `DialoguePlayer`가 있는 씬(예: `addons/dialogtool/UI/Dialogue_UI.tscn`)으로 지정한다.
3. 실행 버튼으로 debug Play한다.
4. `Take` 선택 → "Rich"가 표시되는지 확인. `Leave` 선택 → "Poor"가 표시되는지 확인.
5. 다시 Play해 매 실행이 default(affinity 0)에서 시작하는지 확인(`Leave`는 항상 "Poor").
6. 에디터 Output/Debugger 패널에 provider 관련 error/warning이 없는지(정상 경로) 확인.

## Completion Criteria

- DialogueTool Play 버튼으로 WorldState read/mutation이 포함된 대화를 직접 확인할 수 있다.
- provider 주입은 debug preview 경로에만 영향을 주며 일반 runtime provider 계약을 흐리지 않는다.
- 반복 실행이 예측 가능하고, provider missing/not-ready 실패가 명확하다.
- Task/System/User Guide/Review 문서가 현재 동작과 일치한다.

## Related

- [[ADR-012-Dialogue-Debug-Preview-Provider]]
- [[DT-011-DialogueWorldState-Addon-Packaging]]
- [[DT-008-State-Condition-Dialogue-Integration]]
- [[DT-009-State-Mutation-Dialogue-Effects]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[ADR-010-State-Mutation-Dialogue-Effects]]
- [[ADR-011-DialogueWorldState-Addon-Packaging]]
- [[DialogueTool]]
- [[World-State-System]]
