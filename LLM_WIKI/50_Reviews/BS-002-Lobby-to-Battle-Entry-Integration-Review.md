---
type: review
task: BS-002-Lobby-to-Battle-Entry-Integration
step: 0
status: complete
reviewed: 2026-07-13
tags: [review, combat, battle-session, workspace, entry, design-review]
---

# Design Review: BS-002 Lobby-to-Battle Entry Integration (Step 0)

BS-002 Step 0(설계 리뷰) 결과다. 제품 코드/`.tscn`/`.tres`는 수정하지 않았고, 리뷰 결론 반영으로 Task 문서만
갱신했다(아래 `-> 반영` 표기). 이 Task는 새 ADR을 만들지 않고 [[ADR-022-Battle-Session-Lifecycle]]에 의존한다.

## 검토 대상

- Task: [[BS-002-Lobby-to-Battle-Entry-Integration]]
- 선행 완료: [[BS-001-Battle-Session]]([[Battle-Session-System]]), [[WS-002-Embedded-MDI-Master-Window]]
  ([[Workspace-Window-System]])
- 대조 코드(실측):
  - `Assets/Scenes/workspace.tscn`(`WorkspaceWindowManager`=root 직계, `Windows/WorldWindow` Window,
    `WorldWindow/Panels/HubPanels`=hub 게이팅 패널)
  - `Assets/Script/UI/Window/WorkspaceWindow.cs`(`ContentMode`, `_modeGatedPanels`/`_gatedModeId`)
  - `Assets/Script/UI/Window/WorkspaceWindowManager.cs`(`ApplyPreset`, `GetContentRect`)
  - `Assets/Script/UI/Window/WorkspaceLayoutPreset.cs`(`Battle` preset = World `battle` 64% + TacticBoard
    `read` 33%)
  - `Assets/Script/Battle/BattleSession.cs`(`Configure`/`Start`/`Completed`/state, teardown)
  - `Assets/Scenes/Map/battle_field.tscn`(Node2D 루트 + `Camera2D` zoom 3)

## 코드 대조 결과 (Task 현황 서술 검증)

Task의 "Current Code Facts(2026-07-13)"는 실측과 일치한다. 확정한 사실:

- `run/main_scene` = `workspace.tscn`(`uid://caplqnfi4j3ys`). `WorkspaceWindowManager`는 `workspace.tscn:18`의
  root 직계 노드다(entry controller가 export로 참조 가능).
- `Windows/WorldWindow`는 `WorkspaceWindow : Godot.Window`이고 `Panels(VBox)/ContentMode(Label)` +
  `HubPanels(VBox: CalendarStrip/DangerGauge/ActionCards)`만 가진다. `HubPanels`는 `_modeGatedPanels`로
  `ContentMode == "hub"`일 때만 보인다(`WorkspaceWindow.cs:23~26,76~79`). 즉 **World 창엔 아직 battle 표시 영역이
  없다** — BS-002가 추가할 mount 자리다.
- `ApplyPreset("battle")`는 각 창에 `ContentMode`를 먼저 세팅(`WorkspaceWindowManager.cs:334`)한 뒤 Show/Hide +
  rect를 적용한다. World `ContentMode="battle"` → `HubPanels` 자동 숨김. World visible 64%, TacticBoard `read`
  33%, Report 숨김. ApplyPreset은 display 없이도 동작한다(WS-002 headless 테스트가 이미 호출).
- `BattleSession`은 완료 시 `TeardownScene()`이 battle scene을 `RemoveChild`+`QueueFree`하고 정적 context를
  `_ExitTree`로 null하지만, **자기 session Node는 free하지 않는다**(`BattleSession.cs`에 `QueueFree(this)` 없음).
  Task의 "session Node는 소비자가 제거" 서술과 일치 → entry controller가 정리해야 한다.
- `battle_field.tscn`은 Node2D 루트 + `Camera2D`(zoom 3)를 포함한다. `BattleSession : Node`가 이 Node2D를 자식으로
  AddChild하므로, 세션을 `SubViewport` 아래에 mount하면 전투 Node2D가 그 SubViewport에 렌더되고 battle의 Camera2D가
  그 뷰포트 카메라가 된다(OD1 격리의 근거).

## Findings

1. **[P2] World 창의 battle mount 가시성 게이팅이 미정이다.**
   - 근거: `WorkspaceWindow`는 `_modeGatedPanels` **하나**를 `_gatedModeId`(기본 `hub`)에만 게이팅한다
     (`WorkspaceWindow.cs:23~26,76~79`). battle mount는 `battle` 모드에서 보여야 하는데 현 구조엔 battle 게이팅
     슬롯이 없다.
   - 문제: mount를 `HubPanels`처럼 content_mode에 묶으려면 `WorkspaceWindow`를 content_mode별 다중 게이팅으로
     일반화해야 하는데, 그건 WS-002 창 구조 변경이라 "최소 mount 추가"를 넘는다.
   - 권장: **entry controller가 mount의 `Visible`을 소유**한다(`TryEnterBattle`에서 켜고, cleanup에서 유지/끔).
     `WorkspaceWindow` 무변경. content_mode=battle과 mount 표시는 entry controller가 함께 전환하므로 v0에서 동기가
     깨지지 않는다. Step 2 mount 설계 전 확정. -> Task OD1/Step 2에 반영.

2. **[P2] SubViewport 렌더/카메라/stretch는 headless로 검증 불가한 GUI 전용 관찰점이다.**
   - 근거: battle의 `Camera2D`(zoom 3)가 SubViewport 카메라가 되고, 전투가 World client에 보이는지·스케일·중심은
     실제 렌더가 필요하다. headless DisplayServer는 dummy라 트리/상태는 관측되지만 픽셀은 아니다.
   - 권장: `SubViewportContainer`(stretch) + World client 크기 `SubViewport`. 실제 표시는 **Step 2 GUI smoke가
     유일 증거**로 명시한다(WS-002 GUI 사인오프와 동형). 안 보이면 OD1 대안(WorldWindow viewport 직접 장착)이
     fallback이라 설계 blocker는 아니다. -> Task Verification/Step 2에 GUI 전용 관찰로 명시.

3. **[P2] Completed cleanup은 Victory/Defeat와 Aborted를 동일하게 처리해야 한다.**
   - 근거: 고정 유효 request는 정상 시작하지만, fail-closed상 Start 실패 시 `BattleSession`이
     `CompleteAbortDeferred`로 `Completed(Aborted/InvalidSetup)`을 deferred 발행한다(`BattleSession.cs`). 또한
     세션은 자기 Node를 free하지 않는다.
   - 문제: entry controller의 Completed 핸들러가 성공만 가정하면 invalid/abort 시 active latch·session Node가
     남는다(Task Step 1 Done "invalid setup은 … 완료된 session Node/active 참조가 남지 않는다" 위반).
   - 권장: Completed 핸들러는 outcome을 구분하지 않고 **unsubscribe → active=false → session `QueueFree`**만
     수행한다. Completed는 deferred + `GetInvocationList` 스냅샷 순회라, 핸들러 안에서 unsubscribe/QueueFree는
     안전하다. -> Task OD3/Step 1 Done에 abort 포함 명시.

4. **[P3] 중복 차단 latch는 Start 이전에 동기 설정해야 한다.**
   - `TryEnterBattle`이 `IsBattleActive`를 동기 확인·설정한 뒤 mount/Start한다. Start 완료가 deferred라 같은
     프레임 두 번째 클릭도 첫 호출이 세운 latch로 거부된다. battle 전환으로 `HubPanels`(버튼 포함)가 숨어 물리적
     재클릭도 어렵지만 latch가 진짜 가드다. -> Task Step 1에 순서 명시.

5. **[P3] 자연 종료 후 World는 battle 모드에 빈 mount로 남는다(의도된 v0 경계).**
   - OD3대로 결과/복귀 미구현이라, 완료 후 World는 battle 비율 + 빈 SubViewport(HubPanels 숨김 유지)로 남는다.
     재전투/복귀/결과 표시는 후속. Task Out of Scope와 일치 — 관찰자 혼동 방지로 문서에만 명시.

## Open Decisions 확정 (Step 0)

1. **World battle mount 방식 -> 확정: `SubViewportContainer/SubViewport` 격리(권장).** battle Camera2D를 World
   client에 격리해 UI 좌표계와 분리한다. 가시성은 Finding 1대로 entry controller가 소유. 안 보이면 OD1 대안이
   fallback.
2. **Entry controller 위치 -> 확정: WorldWindow/workspace의 전용 entry Node(권장).** 버튼·manager·mount를 export
   참조하고 활성 session을 소유한다. `WorkspaceShell`에는 게임 진행 책임을 넣지 않는다.
3. **완료 시 최소 cleanup -> 확정: unsubscribe → active 해제 → session `QueueFree`(권장), Aborted 포함.**
   preset 전환/결과 표시/상위 상태 변경 없음. root 종료는 자연 tree teardown.
4. **v0 request source -> 확정: scene export `PackedScene`/`NodePath` + 명시 고정 seed(권장).** encounter/party/seed
   생성 정책은 후속 U3 호출자 소유.

## Step Assessment

- **Step 1 (Lobby Battle Entry Controller)**: 입력=BS-001 `BattleSession` + workspace manager. 출력=`TryEnterBattle`/
  `IsBattleActive`/active latch/mount 장착/Completed cleanup. **선행 blocker 없음** — OD1 mount 타입과 entry API
  이름만 확정하면 착수 가능하고 전부 headless-testable(BS-001 Step 2/3 패턴 재사용: session Running·duplicate 거부·
  invalid fail-closed·자연 완료 cleanup). 순수 seam이라 GUI 불필요.
- **Step 2 (Lobby Button + Battle Presentation)**: 입력=Step 1. 출력=World hub `전투 시작` 버튼 + mount + v0 request
  wiring + 버튼→preset→controller. headless는 button.Pressed→TryEnterBattle→session Running + `ApplyPreset("battle")`
  content_mode 전환까지 관측 가능. **실제 전투 표시/턴 진행은 GUI smoke 전용**(Finding 2). 구조/기능 분리 적절.
- **Step 3 (Docs + Completion Review)**: 표준 문서/완료 리뷰. 문제 없음.

Step 분해는 적절하다. controller(seam) → button/presentation → docs가 독립 검증 가능하고, Step 1이 GUI에 의존하지
않아 실패 전파를 막는다(워크플로 원칙 7 준수). "Running 진입"을 완료 기준으로 못 박아 결과/복귀 범위 확장을 차단한
설계가 견고하다.

## Verification Assessment

- **Step 1 headless entry test(필수)**: 유효 request → mount 아래 session `Running`(reflection/`State`), active 중
  두 번째 `TryEnterBattle`는 false·첫 session 무영향, invalid request는 crash 없이 active/Node 미잔존(deferred
  cleanup 후 검사), 자연 완료(Health=0 주입으로 구동, BS-001 Step 3 패턴)에서 unsubscribe/active/Node 정리.
- **Step 2 headless wiring test**: `button.Pressed.emit()` → `TryEnterBattle` → session Running + World
  `ContentMode=="battle"` + `HubPanels` 숨김. 빠른 중복 emit로 session 미복제.
- **Step 2 GUI smoke(유일한 표시 증거)**: 버튼 → battle preset(World 64%/TacticBoard read) → World client에 실제
  `battle_field.tscn` 표시 + 자동 턴 진행. 정적 context가 그 세션의 전투 씬을 가리킴.
- **빠진 실패 시나리오(보완 권장)**: (a) 첫 턴에 매우 빨리 끝나는 전투(예: lethal turn-start)에서도 entry
  controller cleanup이 성립하는지 — Step 1 자연 완료 테스트에 포함. (b) Aborted request의 cleanup(Finding 3) —
  Step 1 invalid 케이스로 커버.
- **회귀**: BS-001 step1~3(BattleSession 무변경), WS-001/WS-002(workspace는 mount/버튼 추가만). Known Regressions
  SK-001/CB-001 로스터 실패는 clean-baseline 동등성으로 BS-002와 구분(Task에 이미 명시).

## Verdict

**Approved after design fixes.**

P0 없음. 설계는 실측 코드(World 창 구조·`ApplyPreset("battle")`·`BattleSession` 소비자 정리 계약)와 정합하고 Step
분해가 독립 검증 가능하다. 최대 위험(SubViewport 렌더)은 GUI 사인오프 항목이지 설계 blocker가 아니며 OD1 대안
fallback이 있다. 아래 문서 수정(이 세션 반영)으로 Step 0 완료 조건(OD1~4 확정, Step 1/2 경계 확정, P0/P1 없음)을
충족한다:

- OD1~4 권장안 확정(위).
- Finding 1: battle mount 가시성은 entry controller 소유(`WorkspaceWindow` 무변경).
- Finding 2: 실제 전투 표시는 Step 2 GUI smoke 전용, headless는 wiring/state까지.
- Finding 3: Completed cleanup은 Aborted 포함 동일 처리(active/Node 무잔존).

**착수 순서**: Step 1은 OD1(mount 타입)·entry API 이름만 닫으면 즉시 착수 가능(headless 완결). Step 2는 Step 1
통과 후 GUI smoke로 표시를 사인오프한다.

## Related

- [[BS-002-Lobby-to-Battle-Entry-Integration]]
- [[BS-001-Battle-Session]]
- [[Battle-Session-System]]
- [[Workspace-Window-System]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[STEP_REVIEW_WORKFLOW]]
