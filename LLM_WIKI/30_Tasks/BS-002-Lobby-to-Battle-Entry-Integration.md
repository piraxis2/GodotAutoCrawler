---
id: BS-002
type: task
status: complete
system: Combat, Workspace
created: 2026-07-13
updated: 2026-07-13
step0: Approved after design fixes ([[BS-002-Lobby-to-Battle-Entry-Integration-Review]])
completion: 완료, 오너 GUI 표시 smoke 사인오프 ([[BS-002-Lobby-to-Battle-Entry-Integration-Completion-Review]])
depends_on: [BS-001, WS-002]
tags: [task, combat, battle-session, workspace, lobby, entry]
---

# BS-002 Lobby-to-Battle Entry Integration

## Goal

`workspace.tscn`의 World hub를 현재 게임 로비로 보고, 로비의 전투 시작 버튼을 누르면 기존 `BattleSession`을 통해
`battle_field.tscn`이 World 창에 표시되고 전투가 시작되는 **단방향 진입 흐름**을 연결한다.

이번 Task의 완료 지점은 다음 한 줄이다.

```text
게임 로비 -> 전투 시작 -> World 창에 전투 표시 + BattleSession Running
```

전투 결과 표시, 정산, 로비 복귀는 이번 Task가 아니다.

## User Intent

지금 필요한 것은 데모 루프 조립이 아니라, 이미 완성된 workspace 로비와 BattleSession 사이의 첫 연결이다.

- 로비에서 버튼을 누른다.
- 화면이 battle preset으로 바뀐다.
- 기존 전투 씬이 World 창에 나타난다.
- 자동 전투가 실제로 진행된다.

이 네 가지가 성립하면 이번 Task는 목적을 달성한다.

## Current Code Facts (2026-07-13)

아래는 BS-002 착수 시점(설계 리뷰 baseline) 사실이다. 괄호는 BS-002 완료 후 현재 상태다.

- `run/main_scene`은 `Assets/Scenes/workspace.tscn`(`uid://caplqnfi4j3ys`)이다.
- `Windows/WorldWindow`는 (착수 시) hub placeholder만 가졌다. → BS-002가 `전투 시작` 버튼(`HubPanels/EnterBattleButton`)과
  전투 표시 mount(`BattleMount`(SubViewportContainer) + `BattleViewport`(SubViewport))를 추가했다.
- `WorkspaceLayoutPreset.Battle`은 World=`battle`, TacticBoard=`read` 배치를 이미 정의한다.
- `BattleSession`은 `BattleRequest`로 전투 씬을 생성하고 `Start()` 성공 시 `Running`이 된다.
- `BattleSession`은 완료 시 전투 씬과 정적 `BattleFieldScene.BattleField`를 정리하지만 session Node 자체는 소비자가
  제거해야 한다.
- (착수 시) production workspace에 `BattleSession` 소비자가 없었다. → 이제 첫 소비자는 root `LobbyBattleEntry`
  (`Assets/Script/UI/Window/LobbyBattleEntry.cs`)다.

문서와 코드가 다르면 실제 코드와 실행 결과를 최종 사실로 사용한다.

## Target Flow v0

```text
WorldWindow / hub
  -> [전투 시작] Button.Pressed
  -> 고정 테스트용 BattleRequest 생성
     - BattleScene = battle_field.tscn
     - PlayerPath = Articles/Ally/Character
     - Seed = 명시적 고정값
  -> WorkspaceWindowManager.ApplyPreset("battle")
  -> BattleSession 생성·장착·Start
  -> World battle 영역에서 battle_field.tscn 표시
  -> BattleSession.State == Running
```

## Scope

- World hub에 `전투 시작` 버튼 추가.
- 버튼에서 고정 v0 `BattleRequest` 생성.
- battle preset 적용.
- WorldWindow에 전투 씬을 표시할 최소 mount 추가.
- 활성 `BattleSession` 하나 생성·장착·시작.
- active 중 중복 버튼 입력으로 두 번째 session이 생기지 않게 차단.
- 전투가 자연 종료될 경우 Completed 구독 해제, active 참조 해제, 완료된 session Node 정리.
- headless wiring/start 검증과 main scene GUI 수동 확인.

완료 cleanup은 Node·구독 누수를 막는 내부 위생 처리다. 결과를 표시하거나 다른 화면으로 이동시키지 않는다.

## Out of Scope

- `BattleResult` 결과 창/텍스트/토스트.
- 전투 종료 후 lobby/outgame preset 복귀.
- 같은 UI 흐름에서 두 번째 전투 재시작.
- Victory/Defeat에 따른 보상·XP·주차·WorldState 변경.
- DemoState, 행동 3종, 스탯 구매.
- Dungeon/Floor/Encounter definition, PartySnapshot, 로스터 조립.
- 특별 동료 대화, SaveGame, GameLog live 전투 이벤트.
- 전투 중 World 창 hide/reopen 정책과 창 닫기 UX.
- resize/stretch/aspect의 범용 정책. 전투가 mount 안에 보이기 위한 최소 설정만 허용한다.
- TacticBoard 실제 데이터, replay/report, 후퇴/round limit.
- `BattleRequest`/`BattleResult` 계약 확장, 전투 밸런스/캐릭터/적/BT/스킬 변경.

## Responsibility Split

| 책임 | 소유자 |
| --- | --- |
| 전투 씬 생성·검증·턴 시작·승패·전투 씬 정리 | 기존 `BattleSession` |
| 로비 버튼과 고정 request, battle preset, 활성 session Node | BS-002 entry controller |
| World 창의 전투 표시 영역 | World battle presentation/mount |
| 결과 소비·정산·로비 복귀 | 후속 DemoState/U3 flow |

entry controller는 WorldState나 보상을 변경하지 않는다. 이번 Task에서 `BattleResult`는 cleanup을 위해서만 받으며
presentation에 노출하지 않는다.

## Proposed Entry Contract

정확한 이름은 Step 0에서 확정한다.

```csharp
public bool IsBattleActive { get; }
public bool TryEnterBattle(BattleRequest request);
```

- active session이 있거나 request가 잘못되면 false로 fail-closed한다.
- `Configure` -> Completed 구독 -> battle mount에 장착 -> `Start` 순서를 지킨다.
- `Start` 성공 시 session은 `Running`이고 전투 씬은 World battle 영역 아래에 존재한다.
- Completed는 구독/active/session Node cleanup만 수행한다. preset 전환, 결과 표시, 상위 게임 상태 변경은 하지 않는다.

## Open Decisions for Step 0

Step 0 설계 리뷰([[BS-002-Lobby-to-Battle-Entry-Integration-Review]])에서 OD1~4를 권장안으로 확정했다.

### OD1. World battle mount 방식 — 확정: `SubViewportContainer/SubViewport` 격리

- battle Camera2D(zoom 3)를 World client에 격리해 UI 좌표계와 분리한다. 안 보이면 대안(WorldWindow viewport
  직접 장착)이 fallback.
- **battle mount 가시성은 entry controller가 소유한다(Finding 1).** `WorkspaceWindow._modeGatedPanels`는 hub
  하나만 게이팅하므로 `WorkspaceWindow`를 바꾸지 않고 entry controller가 `TryEnterBattle`에서 mount를 켜고
  cleanup에서 관리한다. content_mode=battle 전환과 함께 세팅해 v0에서 동기가 깨지지 않는다.
- 실제 표시/카메라/stretch는 headless 검증 불가 → Step 2 GUI smoke 전용(Finding 2).

### OD2. Entry controller 위치 — 확정: 전용 entry Node

- WorldWindow/workspace의 전용 entry Node가 버튼, `WorkspaceWindowManager`(root 직계), battle mount를 export
  참조하고 활성 session을 소유한다. `WorkspaceShell`에는 게임 진행 책임을 넣지 않는다.

### OD3. 완료 시 최소 cleanup — 확정: unsubscribe → active 해제 → session `QueueFree` (Aborted 포함)

- Completed 핸들러는 outcome(Victory/Defeat/**Aborted**)을 구분하지 않고 동일하게 unsubscribe → active=false →
  session `QueueFree`만 수행한다(Finding 3: `BattleSession`은 자기 Node를 free하지 않고, invalid setup도 deferred
  `Completed(Aborted)`로 닫히므로 성공만 가정하면 누수). World는 battle mode에 남고 결과/복귀 UI는 없다. root
  종료는 자연 tree teardown(ADR-022).

### OD4. v0 request source — 확정: scene export `PackedScene`/`NodePath` + 명시 고정 seed

- encounter/modifier/party/seed 생성 정책은 후속 U3 호출자가 소유한다.

## Step 0: Design Review

Goal:

- 로비 버튼에서 BattleSession Running까지의 가장 작은 장착 경로를 확정한다.

Scope:

- `workspace.tscn`, `WorkspaceWindow`, manager/shell, `BattleSession`, `battle_field.tscn`의 실제 장착 경로 대조.
- OD1~4, entry API, mount, cleanup 순서, 자동/수동 관찰점 확정.

Done condition:

- [[Design-Review-Prompt]] 판정이 `Approved` 또는 `Approved after design fixes`다.
- P0/P1 없음, OD1~4와 Step 1/2 경계가 확정된다.
- 제품 코드/`.tscn`/`.tres`를 수정하지 않는다.

판정: **Approved after design fixes** ([[BS-002-Lobby-to-Battle-Entry-Integration-Review]], 2026-07-13). P0/P1
없음. OD1~4 확정 + Finding 1(mount 가시성=entry controller 소유)/Finding 2(표시=GUI smoke 전용)/Finding 3
(Completed cleanup은 Aborted 포함)을 위 OD와 아래 Step에 반영. Step 1 public seam(`TryEnterBattle`/
`IsBattleActive`)은 headless 완결로 착수 blocker 없음.

## Step 1: Lobby Battle Entry Controller

Goal:

- 고정 request로 활성 BattleSession 하나를 생성하고 Running까지 시작하는 entry seam을 만든다.

Scope:

- `TryEnterBattle`, active latch, request 주입, mount 장착, Completed cleanup.
- valid/invalid/duplicate start.

Done condition:

- valid request -> session 한 개가 mount 아래에서 `Running`.
- active 중 두 번째 호출은 첫 session에 영향 없이 거부(latch를 Start **이전에** 동기 설정, Finding 4).
- invalid setup은 crash하지 않으며 완료된 session Node/active 참조가 남지 않는다(deferred `Completed(Aborted)`
  cleanup 후 검사, Finding 3).
- 자연 완료(Victory/Defeat)와 Aborted를 Completed 핸들러가 동일하게 정리한다: unsubscribe → active 해제 →
  session `QueueFree`. 결과 UI/preset 복귀 없음.

Verification:

- 전용 headless entry test.
- `dotnet build`, Godot `--import`, BS-001 Step 1~3 회귀.

코드 리뷰(2026-07-13): **수정 후 완료.** P2 1건 + P3 1건 반영:

- [P2] `ApplyPreset` 실패 무시 → manager가 있을 때 preset 실패(unknown preset)를 **세션 생성 전에** 감지해
  fail-closed(정리할 것 없이 false 반환). 배선 오류로 hub가 그대로인 채 전투만 시작되는 상태를 막는다. 테스트 `F`.
- [P3] 즉시 완료 회귀 → `bs001_step3_lethal_start.tscn` 재사용으로 Start() 직후 완료 예약(첫 턴 lethal)도 entry가
  active/session/static 정리하는지 고정. 테스트 `G`.

구현 결과(2026-07-13):

- 신규 `Assets/Script/UI/Window/LobbyBattleEntry.cs`(`Node`): `IsBattleActive`, `TryEnterBattle(BattleRequest)`.
  가드(active/null request/null mount → false) → active latch를 Start 이전에 동기 설정(Finding 4) → preset
  (`_windowManager` null-safe)/mount 가시성(`_battleMountVisibility` null-safe) 적용 → `Configure → Completed 구독
  → mount.AddChild(session) → Start`(ADR-022 순서). `OnBattleCompleted`은 outcome 구분 없이(Aborted 포함,
  Finding 3) unsubscribe → active 해제 → session `QueueFree`.
- 배치: Battle 도메인이 UI에 의존하지 않도록 controller를 `UI/Window` 네임스페이스(UI→Battle 방향). manager/
  mount 가시성은 null-safe optional이라 headless seam 테스트가 workspace 없이 완결. preset 실배선은 Step 2.
- 신규 테스트: `bs002_step1_entry_test`(29 assert ALL PASS) — [A] valid→mount 아래 Running+active+static,
  [B] null 동기 거부 + invalid(null scene) deferred Aborted cleanup(active/Node 무잔존), [C] duplicate 차단·첫
  session 무영향, [D] 자연 완료(상대 kill→Victory) cleanup·static 정리, [E] mount 미배선 거부, [F] preset 실패
  fail-closed, [G] lethal turn-start 즉시 완료 cleanup.
- 검증: `dotnet build` 경고/오류 0, `--import` OK. 회귀 bs001_step1/2/3 ALL PASS(BattleSession 무변경).

## Step 2: Lobby Button and Battle Presentation

Goal:

- main workspace의 로비 버튼으로 실제 World 전투 화면에 진입한다.

Scope:

- World hub `전투 시작` 버튼.
- World battle mount와 v0 request wiring.
- 버튼 입력 -> battle preset -> entry controller 호출.

Done condition:

- `workspace.tscn` 실행 후 로비 버튼을 누르면 World=`battle`, TacticBoard=`read`가 된다.
- World 창 안에 실제 `battle_field.tscn`이 보이고 자동 전투가 진행된다.
- session은 `Running`이며 정적 context는 그 session의 전투 씬을 가리킨다.
- 빠른 중복 클릭으로 전투/session이 복제되지 않는다.

Verification:

- workspace headless wiring/start test.
- main scene GUI 수동 smoke: 버튼, preset, 실제 전투 표시/진행.
- WS-001/WS-002 영향권 회귀.

구현 결과(2026-07-13, 코드 리뷰 완료 — P3 Open-Tasks 상태 문구 갱신):

- `LobbyBattleEntry.cs`에 버튼 핸들러 + v0 request 소스 추가: `[Export] Button _enterBattleButton`(`_Ready`에서
  `Pressed` 연결, `_ExitTree` 해제), `[Export] PackedScene _battleScene`/`string _playerPath`/`long _seed`.
  `OnEnterBattlePressed`이 고정 request로 `TryEnterBattle`.
- `workspace.tscn` 배선: `Windows/WorldWindow/Panels/HubPanels/EnterBattleButton`(text "전투 시작"),
  `Windows/WorldWindow/BattleMount`(SubViewportContainer, stretch, `visible=false`) + `BattleViewport`(SubViewport),
  root `LobbyBattleEntry` 노드(exports: button/mount(SubViewport)/mount 가시성(SubViewportContainer)/manager +
  `_battleScene`=battle_field.tscn/`_playerPath`/`_seed`). ext_resource 2종 추가.
- 신규 테스트: `bs002_step2_workspace_test`(14 assert ALL PASS) — workspace 로드 후 outgame(hub)에서 mount 숨김·
  비활성 확인 → 버튼 `Pressed` → session Running(mount 아래) + World `ContentMode==battle` + mount 표시 + 정적
  context가 그 세션 전투 씬 + 중복 클릭 미복제.
- 검증: `dotnet build` 경고/오류 0, `--import` parse/script 에러 0, 회귀 `ws001_step1/2`·`ws002_step2`(41)/
  `step4`(40)·`bs001_step1~3`·`bs002_step1` ALL PASS.
- **GUI 수동 smoke 사인오프 완료(오너 확인)**: main scene에서 실제 `battle_field.tscn`이 World client에 보이고
  자동 턴이 진행됨을 확인했다.

## Step 3: Docs and Completion Review

Goal:

- 단방향 로비→전투 진입 계약과 다음 경계를 Wiki에 고정한다.

Scope:

- [[Battle-Session-System]], [[Workspace-Window-System]], [[Current-State]], [[Open-Tasks]], 본 Task 갱신.
- `50_Reviews/BS-002-Lobby-to-Battle-Entry-Integration-Completion-Review.md` 작성.

Done condition:

- main scene에서 로비 버튼 -> 전투 표시 -> `BattleSession.Running`이 재현된다.
- P0/P1 없음, 코드와 문서 일치.
- 결과/정산/로비 복귀가 후속임이 명확하다.

구현 결과(2026-07-13, **제품 코드 변경 없음** — 문서/완료 리뷰만):

- [[Workspace-Window-System]] "Lobby Battle Entry" 절 신규(entry controller·SubViewport mount·버튼·preset·검증),
  [[Battle-Session-System]] "Consumers" 절 신규(첫 소비자 = LobbyBattleEntry). [[Current-State]] Combat에 BS-002
  추가, [[Open-Tasks]] 상태 갱신.
- 완료 리뷰 [[BS-002-Lobby-to-Battle-Entry-Integration-Completion-Review]](Step 0~3 대조, Verification Matrix,
  판정: 완료 — 코드/headless + 오너 GUI smoke 사인오프).
- 판정: **완료, P0/P1 없음.** 코드·headless 계약과 오너 GUI 수동 smoke(픽셀 렌더/자동 턴 진행)가 모두 충족됐다.

## Verification Matrix

필수 신규 검증:

- valid request -> mount 아래 session Running.
- invalid request fail-closed cleanup.
- active 중 duplicate start 차단.
- 자연 완료 시 구독/active/session Node cleanup.
- lobby button -> battle preset/mode(headless: `button.Pressed` → `TryEnterBattle` → session Running +
  World `ContentMode=="battle"` + HubPanels 숨김까지 관측).
- World client 영역의 실제 battle scene 표시와 자동 턴 진행 — **GUI smoke 전용**(headless 렌더 검증 불가, Finding 2).

필수 회귀 후보:

- `bs001_step1_lifecycle_test`, `bs001_step2_session_start_test`, `bs001_step3_completion_test`
- `ws001_step1_registry_test`, `ws001_step2_geometry_test`
- `ws002_step2_master_dock_test`, `ws002_step4_window_diet_test`
- `dotnet build AutoCrawler.sln -c Debug`, Godot headless `--import`

Known Regressions의 SK-001/CB-001 로스터 실패는 [[Open-Tasks]] rebaseline 소관이며 clean-baseline 동등성으로
BS-002 회귀와 구분한다.

## Risks

- native Window 아래 viewport/camera 가정 때문에 전투가 안 보이거나 client 영역 밖에 그려질 수 있다.
- button/preset/session 시작 순서가 틀리면 빈 battle 화면 또는 중복 session이 생길 수 있다.
- 결과/복귀를 같이 구현하려는 범위 확장이 가장 큰 위험이다. 이번 완료 기준은 Running 진입이다.

## Follow-ups

- 전투 결과 표시와 lobby 복귀.
- DS-001 DemoState 행동/상태.
- DL-001 U3 Dungeon/Party/Encounter 조립, BattleResult 정산, 다음 전투/층 진행.
- U4 SaveSection round-trip.
- 전투 창 resize/aspect/input 고도화, TacticBoard live read, GameLog kill/death.

## Related

- [[BS-002-Lobby-to-Battle-Entry-Integration-Review]]
- [[BS-002-Lobby-to-Battle-Entry-Integration-Completion-Review]]
- [[BS-001-Battle-Session]]
- [[Battle-Session-System]]
- [[Workspace-Window-System]]
- [[WS-002-Embedded-MDI-Master-Window]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[STEP_REVIEW_WORKFLOW]]
