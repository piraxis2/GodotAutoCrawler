---
type: review
task: BS-003-Battle-Completion-Lobby-Return
step: 0
status: complete
reviewed: 2026-07-13
tags: [review, combat, battle-session, workspace, result, reentry, design-review]
---

# Design Review: BS-003 Battle Completion and Lobby Return (Step 0)

BS-003 Step 0(설계 리뷰) 결과다. 제품 코드/`.tscn`/`.tres`는 수정하지 않았고, 리뷰 결론 반영으로 Task 문서만
갱신했다(아래 `-> 반영`). 새 ADR은 만들지 않고 [[ADR-022-Battle-Session-Lifecycle]]에 의존한다.

## 검토 대상

- Task: [[BS-003-Battle-Completion-Lobby-Return]]
- 선행 완료: [[BS-001-Battle-Session]]([[Battle-Session-System]]), [[BS-002-Lobby-to-Battle-Entry-Integration]]
  ([[Workspace-Window-System]] "Lobby Battle Entry")
- 대조 코드(실측):
  - `Assets/Script/UI/Window/LobbyBattleEntry.cs`(`TryEnterBattle`, `OnBattleCompleted(BattleResult)`,
    `_battleMountVisibility`, `_windowManager`, `_enterBattleButton`)
  - `Assets/Script/Battle/BattleSession.cs`(`Completed` deferred + teardown 후 발행, `BattleResult`)
  - `Assets/Script/UI/Window/WorkspaceWindowManager.cs`(`ApplyPreset`), `WorkspaceLayoutPreset.cs`(`Outgame`)
  - `Assets/Scenes/workspace.tscn`(`WorldWindow/BattleMount`(SubViewportContainer, `visible=false`),
    `WorldWindow/Panels/HubPanels`)

## 코드 대조 결과 (Task 현황 서술 검증)

Task "Current Code Facts"는 실측과 일치한다. 확정한 사실:

- `LobbyBattleEntry.OnBattleCompleted(BattleResult result)`는 현재 outcome/result를 쓰지 않고 구독 해제 →
  `_activeSession = null` → `session.QueueFree()`만 한다. World는 battle mode, `BattleMount`는 visible로 남는다
  (`LobbyBattleEntry.cs:81~89`). BS-003이 확장할 정확한 지점이다.
- `BattleSession.Completed`는 `CompleteBattleDeferred`가 **teardown(전투 씬 `RemoveChild`+`QueueFree`, 정적
  `BattleFieldScene.BattleField` null)을 끝낸 뒤** `InvokeCompleted(result)`로 발행한다(`BattleSession.cs`). 즉
  `OnBattleCompleted`가 실행될 때 전투 씬은 이미 없고 정적 context는 null이며, `result`는 scene-free record라
  session/씬 해제 뒤에도 안전하다. **Completed는 이미 사망/물리 콜스택 밖 deferred**이므로 handler에서 추가
  deferral 없이 preset/mount/presentation을 동기 수행해도 재진입/tree 수정 위험이 없다(OD2 답).
- `BattleMount`는 `SubViewportContainer`(mouse_filter 기본 Stop, full-rect)다. visible이면 하위 HubPanels의 입력과
  렌더를 가린다 → 완료 후 반드시 숨겨야 한다(Finding 1).
- `ApplyPreset("outgame")`은 World `ContentMode="hub"`(HubPanels 표시) + TacticBoard/Report 숨김을 적용하고
  headless-safe다(WS-002 테스트가 이미 호출).
- BS-001 Step 3은 완료 handler 안에서 다음 세션을 시작하는 재진입 + seed/상태 격리를 이미 검증했다. BS-003은 이를
  production 버튼 왕복으로 재검증한다.

## Findings

1. **[P2] 완료 후 `BattleMount`(SubViewportContainer)를 숨기지 않으면 hub 입력/렌더를 가린다.**
   - 근거: `BattleMount`는 full-rect `SubViewportContainer`(mouse_filter 기본 Stop, `workspace.tscn`). 전투 씬은
     Completed 이전에 이미 free되지만 mount 자체는 visible로 남아(현 `OnBattleCompleted`) outgame 복귀 후 빈
     viewport가 HubPanels(전투 시작 버튼·결과 라벨) 위에 남아 입력/렌더를 가린다(Risk #2).
   - 권장: 완료 handler에서 `_battleMountVisibility.Visible = false`를 반드시 수행한다(진입 시 표시와 대칭).
     mount 가시성은 controller 소유(BS-002 Finding 1). -> Task OD3/Step 1에 반영.

2. **[P2] session cleanup을 preset/presentation보다 먼저·독립적으로 수행해야 한다.**
   - 근거: preset 적용/결과 라벨 갱신이 실패(unknown preset false, 라벨 null 등)해도 구독 해제/active 해제/session
     `QueueFree`는 건너뛰면 안 된다(Risk #1, OD3).
   - 권장: 순서 = **result 값 캡처 → 구독 해제 + `_activeSession=null` + `session.QueueFree()` → mount 숨김 →
     `ApplyPreset("outgame")` → 결과 라벨 갱신**. preset/presentation은 null-safe + 실패 시 로그만. -> Task OD3에 반영.

3. **[P2] 결과 라벨/preset/mount 참조는 null-safe여야 Step 1 headless seam이 workspace 없이 완결된다.**
   - 근거: BS-002 Step 1과 동형. `_resultLabel`(신규)/`_windowManager`/`_battleMountVisibility`가 null이면 해당
     presentation을 건너뛰고 cleanup만 수행한다. 이래야 `bs003_step1`이 full workspace 로드 없이 completion/cleanup
     계약만 검증한다. -> Task Step 1/Proposed Contract에 반영.

4. **[P3] 동기 재진입 시 완료 session Node가 한 프레임 mount 아래 공존할 수 있다.**
   - 근거: `session.QueueFree()`는 프레임 끝에 실제 해제된다(Risk #3). GUI 왕복은 다음 입력=다음 프레임이라 이전
     session이 이미 해제돼 안전하지만, 테스트가 **동기적으로** 재진입하면 이전(해제 예약) session Node + 새 session이
     잠깐 공존한다. 단일 세션 가드는 정적 `BattleFieldScene.BattleField`(전투)이지 빈 Node가 아니므로 제품 문제는
     아니다.
   - 권장: 재진입 시점은 "다음 입력"으로 정의하고, 테스트는 raw child count가 아니라 `IsBattleActive`/active
     session 기준으로 관찰하거나 한 프레임 대기한다. -> Task OD5/Verification에 관찰 규칙 명시.

5. **[P3] 고정 production request의 Aborted는 hub 복귀 + "전투 시작 실패" + `GD.PushError`.**
   - 근거: 정상 배선이면 Aborted는 안 나지만 fail-closed상 가능(OD4). Aborted도 deferred `Completed`로 같은
     handler를 타므로 Victory/Defeat와 동일하게 hub 복귀 + cleanup + "시작 실패" 라벨. 고정 request의 Aborted는
     구성 오류 신호라 `GD.PushError` 진단만 남긴다(자동 retry/상세 노출 없음). -> Task OD4에 반영.

6. **[P2 확인] `BattleResult` 소비는 teardown 후에도 안전, 추가 deferral 불필요.**
   - `Completed`가 teardown 완료 + deferred 발행이므로 `OnBattleCompleted`는 idle에서 실행되고 `result`는
     scene-free다. handler에서 `session.QueueFree()`(자기 콜백 안이지만 QueueFree는 프레임 끝 해제라 안전) +
     preset + 라벨을 동기 수행해도 된다. OD2의 "추가 deferred 필요 여부"는 **불필요**로 확정.

## Open Decisions 확정 (Step 0)

1. **결과 표시 형태 -> 확정: HubPanels 안 inline `Label` 하나(권장).** 초기 "최근 전투: 없음", 완료 후 승리/패배/
   시작 실패. modal/토스트/별도 결과 국면 없음.
2. **완료 전환 시점 -> 확정: `Completed` handler에서 즉시 cleanup + outgame 복귀(권장). 추가 deferral 불필요**
   (Finding 6). 별도 확인 단계/타이머 없음.
3. **cleanup ↔ presentation 순서 -> 확정: 캡처 → cleanup → mount 숨김 → outgame preset → 결과 라벨(Finding 1/2).**
   presentation/preset 실패가 cleanup을 막지 않는다.
4. **Aborted 정책 -> 확정: Victory/Defeat와 동일 hub 복귀 + "전투 시작 실패" 표시 + `GD.PushError`(권장, Finding 5).**
   자동 retry/상세 노출 없음.
5. **재전투 시점·이전 결과 -> 확정: 로비 복귀 후 다음 입력부터 재전투. `LastResult`는 nullable이며 첫 완료 전
   `null`, `Completed`에서만 갱신한다. 새 진입 시 라벨을 "전투 진행 중"으로 바꾸고 마지막 완료 결과는 보존
   (진입 시 초기화하지 않음)한다.** 최소 계약은 active session·정적 context 독립성.
   테스트 관찰은 Finding 4 규칙.
6. **outgame preset 실패 -> 확정: cleanup + mount 숨김 유지, 오류 기록, 되살리기/자동 새 session 없음(권장,
   Finding 2와 동일 격리).** 테스트로 active/session 무누수 확인.

**추가 확정(Step 0 scope): 테스트 fixture** — BS-002 자산 재사용(`battle_field.tscn`, 즉시 완료는
`bs001_step3_lethal_start.tscn`) + 신규 `bs003_step1`/`bs003_step2` 테스트 파일. 새 `.tscn` fixture는 불필요.

## Step Assessment

- **Step 1 (Completion + Lobby Return Seam)**: 입력=BS-002 `LobbyBattleEntry`. 출력=`OnBattleCompleted` 결과 소비 +
  cleanup-first + mount 숨김 + outgame preset + 결과 라벨(null-safe) + `LastResult`. **선행 blocker 없음** — 전부
  headless-testable(Victory/Defeat/Aborted는 BS-001 Health kill 패턴, 즉시 완료는 lethal fixture, 실패 격리는
  unknown preset). 순수 seam이라 GUI 불필요.
- **Step 2 (Workspace Result + Sequential Re-entry)**: 입력=Step 1. 출력=HubPanels 결과 `Label` + export wiring +
  버튼 2회 연속 전투. headless로 완료→hub 복귀→결과 라벨→재전투 Running까지 관측 가능. 실제 결과 문구/화면 전환은
  GUI smoke.
- **Step 3 (Docs + Completion Review)**: 표준.

Step 분해는 적절하다. seam(headless) → workspace/re-entry → docs가 독립 검증 가능하고, Step 1이 GUI에 의존하지
않아 실패 전파를 막는다. "로비↔전투 왕복 + 재전투"를 완료 기준으로 못 박아 정산/이월 범위 확장을 차단한 설계가
견고하다.

## Verification Assessment

- **Step 1 headless(`bs003_step1`)**: Victory/Defeat/Aborted 각각 → `IsBattleActive == false` + mount 숨김 +
  outgame preset(manager 있을 때) + 결과 라벨(label 있을 때) + 정적 context null. 완료 session의 mount 무잔존은
  `QueueFree`가 처리되는 **다음 process frame 이후** 단언한다.
  첫 턴 lethal 즉시 완료도 동일. **preset 실패(unknown) 시에도 cleanup/mount 숨김 유지**(Finding 2). null-safe refs로
  workspace 없이 완결.
- **Step 2 headless(`bs003_step2`)**: workspace 로드 → 버튼 → 전투 → Health kill 완료 → World `hub` +
  `BattleMount.Visible == false` + 결과 라벨 = outcome + 버튼 재노출; 두 번째 버튼 → 두 번째 session 하나만 Running
  → 두 번째 완료도 1회 + hub 복귀. seed/정적 context/Node 격리(재진입은 프레임 대기 후 관찰, Finding 4).
- **GUI smoke(Step 2)**: 실제 전투 종료 → 결과 문구/hub 표시 → 재전투 표시·진행. headless 불가 부분.
- **빠진 시나리오(보완 권장)**: (a) 결과 라벨 갱신 실패/preset 실패에도 cleanup·mount 숨김 성립(Finding 2) —
  `bs003_step1`에 격리 케이스. (b) 재진입 관찰 프레임 규칙(Finding 4) — 테스트 주석/대기.
- **회귀**: `bs002_step1/2`, `bs001_step1~3`, WS-001/WS-002. Known Regressions SK-001/CB-001 로스터는 clean-baseline
  동등성으로 구분(Task에 명시).

## Verdict

**Approved after design fixes.**

P0 없음. 설계는 실측 코드(`Completed` deferred + teardown 후 발행, `LobbyBattleEntry`가 cleanup/preset/mount 소유,
`BattleResult` scene-free)와 정합하고 Step 분해가 독립 검증 가능하다. 아래 문서 수정(이 세션 반영)으로 Step 0 완료
조건(OD1~6 확정, Step 1/2 경계 확정, P0/P1 없음)을 충족한다:

- OD1~6 권장안 확정 + fixture 재사용 결정.
- Finding 1: 완료 시 mount 숨김 필수(hub 입력/렌더 가림 방지).
- Finding 2/OD3: cleanup-first, preset/presentation 실패 격리.
- Finding 3: UI refs null-safe(Step 1 headless 분리).
- Finding 6: Completed 이미 deferred라 추가 deferral 불필요(OD2 확정).

**착수 순서**: Step 1은 OD/Finding 반영으로 즉시 착수 가능(headless 완결). Step 2는 Step 1 통과 후 workspace 배선 +
GUI smoke.

---

type: review
task: BS-003-Battle-Completion-Lobby-Return
step: 1
status: complete
reviewed: 2026-07-13
tags: [review, combat, battle-session, workspace, result, reentry, code-review]

---

# Code Review: BS-003 Step 1 Completion and Lobby Return Seam

## 검토 대상

- 코드: `Assets/Script/UI/Window/LobbyBattleEntry.cs`
- 테스트: `Assets/Script/Tests/Bs003Step1CompletionReturnTest.cs`, `Assets/Script/Tests/bs003_step1_completion_return_test.tscn`
- 기준: 본 Task Step 1 완료 조건, Step 0 OD1~6/Finding 1~6, [[STEP_REVIEW_WORKFLOW]]

## 발견 사항

P0/P1/P2 발견 없음.

`LobbyBattleEntry.OnBattleCompleted`는 확정된 OD3 순서(result 캡처 -> 구독 해제/active 해제/session `QueueFree` -> `BattleMount` 숨김 -> outgame preset -> 결과 라벨)를 따른다. `_windowManager`, `_battleMountVisibility`, `_resultLabel` 경로는 null-safe이고, preset 실패/Aborted 진단도 cleanup 이후에만 수행된다. 도메인 `BattleSession`/`BattleResult` 계약 변경은 없다.

## 확인한 내용

- `LastResult`는 첫 완료 전 null이고, `Completed`에서만 갱신되며 재진입 시 보존된다.
- Victory/Defeat/Aborted/첫 턴 lethal 즉시 완료가 모두 active 해제, mount 숨김, 결과 라벨 갱신으로 닫힌다.
- 완료 session 무잔존은 `QueueFree` 처리 뒤 프레임 대기 후 관찰한다.
- outgame preset 실패는 active/session cleanup과 mount 숨김을 막지 않는다.
- 새 전투 진입 시 라벨은 "전투 진행 중"으로 바뀌고, `LastResult`는 초기화되지 않는다.

## 검증 결과

- `dotnet build AutoCrawler.sln -c Debug` PASS, warning 0/error 0.
- `bs003_step1_completion_return_test.tscn` PASS, 30/30 assertions. Aborted/unknown preset 케이스의 `GD.PushError`는 의도된 진단 로그다.
- 회귀 PASS: `bs002_step1_entry_test.tscn`, `bs002_step2_workspace_test.tscn`, `bs001_step1_lifecycle_test.tscn`, `bs001_step2_session_start_test.tscn`, `bs001_step3_completion_test.tscn`.
- Godot `--headless --path . --import` exit 0. 종료 시 `graph_offset` deprecation warning, ObjectDB/resource 잔류 로그가 남았으나 이번 변경과 직접 관련된 신규 실패는 확인되지 않았다.

## 남은 테스트 공백

- 실제 workspace의 HubPanels 결과 Label export wiring과 outgame preset 성공 시 World=`hub` 복귀는 Step 2 범위다.
- GUI smoke(실제 화면 결과 문구, 같은 버튼 재전투)는 Step 2에서 수행한다.

## 판정

**완료.** Step 1 완료 조건을 충족하고 P0/P1 없음. Step 2(workspace 결과 Label 배선 + 버튼 기반 2회 연속 전투) 착수 가능.
---

type: review
task: BS-003-Battle-Completion-Lobby-Return
step: 2
status: complete
reviewed: 2026-07-14
tags: [review, combat, battle-session, workspace, result, reentry, code-review]

---

# Code Review: BS-003 Step 2 Workspace Result and Sequential Re-entry

## 검토 대상

- 씬: `Assets/Scenes/workspace.tscn`
- 테스트: `Assets/Script/Tests/Bs003Step2WorkspaceRoundTripTest.cs`, `Assets/Script/Tests/bs003_step2_workspace_roundtrip_test.tscn`
- 관련 코드: `Assets/Script/UI/Window/LobbyBattleEntry.cs`, `WorkspaceWindowManager.cs`, `WorkspaceWindow.cs`, `WorkspaceLayoutPreset.cs`
- 기준: 본 Task Step 2 완료 조건, Step 0 OD1~6/Finding 1~6, Step 1 완료 계약

## 발견 사항

P0/P1/P2 발견 없음.

`workspace.tscn`은 HubPanels 아래에 `ResultLabel`을 추가하고, root `LobbyBattleEntry`의 `_resultLabel` export를 해당 Label로 연결한다. completion handler는 Step 1에서 검증된 cleanup-first 경로를 유지하며, 완료 시 `ApplyPreset("outgame")`으로 World를 `hub`로 되돌리고 `BattleMount`를 숨긴 뒤 결과 라벨을 갱신한다. 새 전투는 같은 버튼 signal 경로로 시작되며, `IsBattleActive` latch와 `BattleFieldScene.BattleField` 정리 후 재진입한다.

## 확인한 내용

- `ResultLabel`은 `HubPanels` 내부에 있고 초기 text는 "최근 전투: 없음"이다.
- `LobbyBattleEntry`는 `EnterBattleButton`, `BattleViewport`, `BattleMount`, `WorkspaceWindowManager`, `ResultLabel`을 모두 씬 export로 참조한다.
- workspace 실제 씬 로드 후 버튼 signal -> battle preset -> session Running -> 상대 사망 완료 -> outgame/hub 복귀 -> 결과 라벨 "최근 전투: 승리" -> 같은 버튼 재전투가 headless에서 재현된다.
- 완료 후 `BattleMount.Visible == false`, mount 아래 유효 `BattleSession` 없음, 정적 battle context null을 확인했다.
- 두 번째 진입 시 mount 아래 session count가 1이고, 두 번째 완료도 hub 복귀와 정적 정리를 반복한다.

## 검증 결과

- `dotnet build AutoCrawler.sln -c Debug` PASS, warning 0/error 0.
- `bs003_step2_workspace_roundtrip_test.tscn` PASS, 23/23 assertions.
- `bs003_step1_completion_return_test.tscn` PASS.
- 회귀 PASS: `bs002_step1_entry_test.tscn`, `bs002_step2_workspace_test.tscn`, `ws001_step1_registry_test.tscn`, `ws002_step2_master_dock_test.tscn` 41/41, `ws002_step4_window_diet_test.tscn` 40/40, `bs001_step1_lifecycle_test.tscn`, `bs001_step2_session_start_test.tscn`, `bs001_step3_completion_test.tscn`.
- Godot `--headless --path . --import` exit 0. 종료 시 기존 `graph_offset` deprecation warning 및 ObjectDB/resource 잔류 로그가 남았으나, parse/script error나 이번 변경 관련 신규 실패는 없었다.

## 남은 테스트 공백

- `bs003_step2`는 World=`hub`, HubPanels 표시, mount 숨김, 결과 라벨, 재전투를 직접 검증한다. TacticBoard/Report가 outgame preset에서 숨겨지는 조건은 `ApplyPreset("outgame")` 경로와 `ws002_step4_window_diet_test` 회귀로 간접 보존되며, Step 2 신규 테스트에서 별도 direct assertion은 없다.
- 실제 픽셀 화면에서 결과 문구와 hub가 보이고 같은 버튼으로 재전투가 표시·진행되는지는 GUI 수동 smoke/오너 사인오프가 필요하다.

## 판정

**완료.** Step 2 완료 조건을 headless 범위에서 충족하고 P0/P1 없음. GUI 수동 smoke만 Step 3/완료 리뷰 전 잔여 확인 항목으로 남긴다.
## Related

- [[BS-003-Battle-Completion-Lobby-Return]]
- [[BS-002-Lobby-to-Battle-Entry-Integration]]
- [[BS-001-Battle-Session]]
- [[Battle-Session-System]]
- [[Workspace-Window-System]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[STEP_REVIEW_WORKFLOW]]



