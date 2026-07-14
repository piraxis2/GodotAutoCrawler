---
id: BS-003
type: task
status: complete
system: Combat, Workspace
created: 2026-07-13
updated: 2026-07-13
step0: Approved after design fixes ([[BS-003-Battle-Completion-Lobby-Return-Review]])
completion: 완료 (오너 GUI smoke 사인오프, [[BS-003-Battle-Completion-Lobby-Return-Completion-Review]])
depends_on: [BS-001, BS-002, WS-002]
tags: [task, combat, battle-session, workspace, lobby, result, reentry]
---

# BS-003 Battle Completion and Lobby Return

## Goal

BS-002의 단방향 로비→전투 진입 뒤에 최소 완료 흐름을 연결한다. `BattleSession.Completed`가 반환한
`BattleResult`를 소비해 결과를 짧게 표시하고, World 창을 로비로 복귀시킨 뒤 같은 버튼으로 다음 전투를 다시
시작할 수 있게 한다.

이번 Task의 완료 지점은 다음 한 줄이다.

```text
게임 로비 -> 전투 -> 승패/실패 결과 -> 로비 복귀 -> 같은 프로세스에서 재전투 가능
```

이 Task는 게임 진행 규칙이나 정산을 만들지 않는다. 목적은 BS-001의 결과·재진입 계약을 BS-002 production
소비자에서 실제 화면 흐름으로 닫는 것이다.

## User Intent

- 현재는 완전한 데모나 던전 등반 루프보다 로비와 전투 사이의 기본 왕복이 먼저다.
- 전투가 끝났다는 사실과 Victory/Defeat/Aborted 구분만 사용자가 확인할 수 있으면 된다.
- 결과 확인 후 로비의 `전투 시작` 버튼으로 같은 고정 전투를 다시 실행할 수 있어야 한다.
- 보상·성장·스토리·특별 동료·층 진행은 이 왕복이 안정된 다음 별도 Task에서 연결한다.

## Current Code Facts (2026-07-13)

- `LobbyBattleEntry.TryEnterBattle`은 battle preset을 적용하고, World `BattleViewport` 아래에 활성
  `BattleSession` 하나를 장착해 시작한다.
- `OnBattleCompleted(BattleResult)`는 현재 outcome을 구분하지 않고 구독 해제 → active 참조 해제 → session
  `QueueFree`만 수행한다. World는 battle mode, `BattleMount`는 visible 상태로 남는다.
- `BattleSession.Completed`는 전투 씬/정적 context 정리가 끝난 뒤 deferred로 정확히 한 번 발행된다. 결과는
  scene-free record라 session/전투 씬 해제 뒤에도 안전하다([[ADR-022-Battle-Session-Lifecycle]]).
- 정상 결과는 `Victory/OpponentsEliminated`, `Defeat/PlayerDefeated`; 설정 실패는
  `Aborted/InvalidSetup`이다. `Retreat`와 round limit는 타입만 있고 현재 발생하지 않는다.
- workspace의 outgame preset은 World=`hub`를 전면 표시하고 TacticBoard/Report를 숨긴다. HubPanels에는 현재
  `전투 시작` 버튼이 있지만 결과 표시 영역은 없다.
- BS-001은 완료 handler 직후 다음 세션을 시작하는 재진입과 seed/상태 격리를 이미 검증했다. BS-003은 이를
  production workspace 버튼 왕복으로 검증한다.

문서와 코드가 다르면 실제 코드와 실행 결과를 최종 사실로 사용한다.

## Target Flow v0

```text
BattleSession.Completed(result)
  -> result를 scene-free 값으로 보존
  -> Completed 구독 해제 + active session 해제 + session QueueFree
  -> BattleMount 숨김
  -> WorkspaceWindowManager.ApplyPreset("outgame")
  -> HubPanels의 최소 결과 텍스트 갱신
  -> 전투 시작 버튼으로 두 번째 BattleSession 시작 가능
```

표시 문구의 v0 의미는 다음 정도로 제한한다.

| Outcome | 사용자 표시 의미 |
| --- | --- |
| `Victory` | 전투 승리 |
| `Defeat` | 전투 패배 |
| `Aborted` | 전투 시작/구성 실패 |
| `Retreat` | 현재 미발생. 향후 발생해도 별도 후퇴 결과로 fail-closed 표시 |

## Scope

- 기존 `LobbyBattleEntry`가 `BattleResult`를 실제로 소비하도록 확장.
- 완료 시 활성 세션 cleanup을 항상 보장.
- `BattleMount` 숨김과 `outgame` preset 복귀.
- World hub에 최근 전투 결과를 보여 주는 최소 read-only 텍스트 영역 추가.
- Victory/Defeat/Aborted를 서로 다른 결과로 표시.
- 새 전투 진입 시 이전 결과 표시를 초기화하거나 진행 중 상태로 전환하는 단순 정책.
- 완료 후 같은 버튼으로 두 번째 전투를 시작하고 독립적으로 완료할 수 있는지 검증.
- headless 상태/wiring/재진입 검증과 main scene GUI 수동 smoke.

## Out of Scope

- 골드, 아이템, XP, 주차, 위험도, WorldState 등 모든 정산.
- HP/마나/ammo의 전투 간 이월.
- Dungeon/Floor/Encounter/PartySnapshot/로스터 조립과 seed 생성 정책.
- 결과 모달, 상세 통계, 유닛별 결과 목록, replay/report 화면.
- 결과 확인 버튼이나 사용자가 머무르는 별도 결과 국면.
- 특별 동료 이벤트, 다이얼로그, 퀘스트/스토리 분기.
- SaveGame, GameLog live 전투 이벤트, TacticBoard 실제 데이터.
- 수동 후퇴, round limit, 전투 중 창 닫기/취소.
- `BattleRequest`/`BattleResult`/`BattleSession` 도메인 계약 변경.
- 전투 밸런스, 캐릭터, 적, BT, 스킬, SubViewport stretch/aspect 고도화.

## Responsibility Split

| 책임 | 소유자 |
| --- | --- |
| 전투 시작·승패·값 결과·전투 씬/정적 context 정리 | 기존 `BattleSession` |
| 결과 수신, mount/preset 전환, 활성 session Node, 재전투 gate | `LobbyBattleEntry` |
| 최소 결과 텍스트 | World hub presentation |
| 보상·성장·WorldState·다음 encounter 결정 | 후속 DemoState/U3 flow |

`BattleSession`에는 UI나 로비 전환 책임을 추가하지 않는다. `WorkspaceShell`과
`WorkspaceWindowManager`에도 게임 결과 해석을 넣지 않는다.

## Proposed Completion Contract

정확한 API와 visibility는 Step 0 설계 리뷰에서 확정한다.

```csharp
public bool IsBattleActive { get; }
public BattleResult? LastResult { get; private set; }
public bool TryEnterBattle(BattleRequest request);
```

- `LastResult`는 첫 완료 전 `null`이고, `Completed`에서만 scene-free 결과로 갱신한다. 새 전투 진입 시 초기화하지
  않으며 session/Article/Node 참조는 보존하지 않는다.
- 완료 cleanup은 결과 표시나 preset 적용 실패와 무관하게 수행되어야 한다.
- 완료 handler 직후 `IsBattleActive == false`, 정적 battle context가 null이어야 한다. 완료 session은 `QueueFree`
  예약 상태일 수 있으며 **다음 process frame 이후** mount 아래에 남지 않아야 한다.
- 로비 복귀 뒤 새 입력은 새 `BattleSession`을 생성해야 하며 이전 session/결과 객체 수명에 의존하지 않는다.

## Open Decisions for Step 0

Step 0 설계 리뷰([[BS-003-Battle-Completion-Lobby-Return-Review]])에서 OD1~6을 권장안으로 확정했다.

### OD1. 결과 표시 형태 — 확정: HubPanels inline `Label` 하나

초기 "최근 전투: 없음", 완료 후 승리/패배/시작 실패. modal/토스트/별도 결과 국면 없음.

### OD2. 완료 전환 시점 — 확정: `Completed` handler에서 즉시 cleanup + outgame 복귀, 추가 deferral 불필요

`BattleSession.Completed`는 teardown(전투 씬 free/정적 null) 후 deferred로 발행되므로 handler는 idle에서 실행되고
`result`는 scene-free다. handler에서 preset/mount/라벨을 동기 수행해도 재진입/tree 수정 위험이 없다(리뷰 Finding 6).
별도 확인 단계/타이머 없음.

### OD3. cleanup ↔ presentation 순서 — 확정: 캡처 → cleanup → mount 숨김 → outgame preset → 결과 라벨

result 값 캡처 → 구독 해제 + `_activeSession=null` + `session.QueueFree()` → `_battleMountVisibility.Visible=false`
(Finding 1: 안 숨기면 빈 `SubViewportContainer`가 HubPanels 입력/렌더를 가림) → `ApplyPreset("outgame")` → 결과 라벨.
presentation/preset은 null-safe이고 실패해도 cleanup을 건너뛰지 않는다(Finding 2).

### OD4. Aborted 정책 — 확정: hub 복귀 + "전투 시작 실패" 표시 + `GD.PushError`

Victory/Defeat와 동일 handler(deferred Completed)로 hub 복귀 + cleanup + "시작 실패" 라벨. 고정 request의 Aborted는
구성 오류 신호라 `GD.PushError` 진단만 남긴다. 자동 retry/상세 노출 없음.

### OD5. 재전투 시점·이전 결과 — 확정: 다음 입력부터 재전투, nullable `LastResult`는 마지막 완료 보존

로비 복귀 후 다음 입력부터 재전투. `LastResult`는 첫 완료 전 `null`이고 `Completed`에서만 갱신한다. 새 진입 시
결과 라벨을 "전투 진행 중"으로 바꾸되 마지막 완료 결과는 보존(진입 시 초기화하지 않음)한다. 최소 계약은 active
session·정적 context 독립성. 동기 재진입 시 완료 session Node가 한 프레임 공존할 수 있으므로 테스트는 raw child
count가 아니라 `IsBattleActive`/active 기준 또는 프레임 대기로 관찰한다(Finding 4).

### OD6. outgame preset 실패 — 확정: cleanup + mount 숨김 유지, 오류 기록, 되살리기 없음

session cleanup과 mount 숨김은 preset 실패와 무관하게 수행하고 오류만 기록한다. 되살리기/자동 새 session 없음.
테스트로 preset 실패가 active/session 누수를 만들지 않는지 확인(Finding 2와 동일 격리).

### 테스트 fixture — 확정: BS-002 자산 재사용

`battle_field.tscn`(정상 결과), `bs001_step3_lethal_start.tscn`(즉시 완료) 재사용 + 신규 `bs003_step1`/`bs003_step2`
테스트 파일. 새 `.tscn` fixture 불필요.

## Step 0: Design Review

Goal:

- `BattleResult` 소비부터 로비 복귀·재전투까지의 최소 수명 주기와 UI 경계를 확정한다.

Scope:

- `LobbyBattleEntry`, `BattleSession.Completed`, workspace preset/mount/HubPanels 실제 코드 대조.
- OD1~6, completion 순서, 결과 표시 문자열/상태, preset 실패 정책, 재진입 관찰점 확정.
- BS-002 테스트를 재사용할지 BS-003 fixture를 분리할지 결정.

Done condition:

- [[Design-Review-Prompt]] 판정이 `Approved` 또는 `Approved after design fixes`다.
- P0/P1 없음, OD1~6와 Step 1/2 경계가 확정된다.
- 제품 코드/`.tscn`/`.tres`를 수정하지 않는다.

판정: **Approved after design fixes** ([[BS-003-Battle-Completion-Lobby-Return-Review]], 2026-07-13). P0/P1 없음.
OD1~6 확정 + Finding 1(완료 시 mount 숨김 필수)/Finding 2(cleanup-first, preset·presentation 실패 격리)/Finding 3
(UI refs null-safe)/Finding 6(Completed 이미 deferred라 추가 deferral 불필요)를 위 OD와 아래 Step에 반영. Step 1
seam은 headless 완결로 착수 blocker 없음.

Verification:

- 코드·씬·Task·ADR 정적 대조.
- deferred Completed와 workspace preset 호출 순서 위험 검토.

## Step 1: Completion and Lobby Return Seam

Goal:

- workspace 실배선과 분리 가능한 completion 소비·cleanup·로비 복귀 seam을 구현한다.

Scope:

- `LobbyBattleEntry.OnBattleCompleted` 결과 소비.
- mount visibility off, outgame preset 적용, 최소 결과 presentation 주입점.
- Victory/Defeat/Aborted와 preset 실패.
- active/session cleanup 및 재호출 가능 상태.

Done condition:

- 세 결과 모두 `IsBattleActive == false`, 완료 session이 해제 예약되고 mount가 숨겨진다(Finding 1: 안 숨기면 빈
  SubViewportContainer가 hub 입력/렌더를 가림).
- 완료 session의 mount 무잔존은 `QueueFree` 처리 뒤인 다음 process frame 이후에 단언한다. handler 직후에는 해제
  예약 Node가 남을 수 있다(Finding 4).
- cleanup(구독 해제/active 해제/session `QueueFree`)을 preset/presentation보다 **먼저·독립적으로** 수행한다
  (Finding 2, OD3). `_resultLabel`/`_windowManager`/`_battleMountVisibility`는 null-safe(Finding 3).
- outgame preset 성공 시 World가 hub mode로 돌아간다.
- 결과 표시 실패/preset 실패가 구독·active/session cleanup을 막지 않는다.
- 첫 턴 lethal처럼 Start 직후 완료 예약된 경로도 동일하게 복귀한다.
- 도메인 `BattleSession`/`BattleResult` 계약을 변경하지 않는다.

Verification:

- 신규 `bs003_step1` headless test: Victory/Defeat/Aborted, mount/preset/result, cleanup, 즉시 완료, 실패 격리.
- `bs002_step1`, `bs001_step1~3` 회귀.
- `dotnet build`, Godot `--import`.

구현 결과(2026-07-13, 코드 리뷰 완료 — 판정: 완료, [[BS-003-Battle-Completion-Lobby-Return-Review]]):

- `LobbyBattleEntry.cs` 확장: `public BattleResult LastResult { get; private set; }`(첫 완료 전 null, Completed에서만
  갱신; CS8632 회피 위해 `?` 없이 nullable 참조), `[Export] Label _resultLabel`(null-safe), `[Export] string
  _outgamePresetId = "outgame"`. `_Ready`가 초기 라벨 "최근 전투: 없음". `TryEnterBattle` 성공 시 라벨 "전투 진행
  중"(LastResult 미변경). `OnBattleCompleted`는 OD3 순서 — result 캡처 → 구독 해제/active 해제/session `QueueFree`
  → `_battleMountVisibility.Visible=false`(Finding 1) → outgame preset(실패 시 `GD.PushError`만, OD6) → 결과 라벨
  (Victory=승리/Defeat=패배/Aborted=전투 시작 실패). Aborted는 `GD.PushError` 진단(OD4). 도메인 계약 무변경.
- 신규 테스트: `bs003_step1_completion_return_test`(30 assert ALL PASS) — [A] Victory 복귀+cleanup+라벨+정적 정리+
  session 무잔존, [F] 재진입 시 LastResult 보존·라벨 "진행 중", [B] Defeat, [C] Aborted, [D] 첫 턴 lethal 즉시
  완료, [E] 완료 시 outgame preset 실패에도 cleanup/mount 숨김/라벨 유지(enter 후 unknown preset manager 주입).
- null-safe refs로 workspace 없이 완결. 실제 outgame preset 적용(World hub 복귀)·결과 문구 화면은 Step 2/GUI.
- 검증: `dotnet build` 경고/오류 0(0 warning), `--import` OK. 회귀 bs002_step1/2·bs001_step1~3 ALL PASS
  (BS-002는 완료 로직 미트리거 + `_resultLabel` 미배선이라 무영향).

## Step 2: Workspace Result and Sequential Re-entry

Goal:

- main workspace에서 전투 완료→결과 표시→로비 복귀→두 번째 전투 시작 흐름을 연결한다.

Scope:

- HubPanels 최소 결과 Label과 `LobbyBattleEntry` export wiring.
- workspace 버튼을 통한 2회 연속 전투.
- 첫 전투와 두 번째 전투의 session/static context/Node 수명 격리.

Done condition:

- 전투 완료 후 World=`hub`, TacticBoard/Report는 outgame preset 상태, `BattleMount.Visible == false`다.
- HubPanels와 전투 버튼이 다시 보이고 결과 문구가 outcome과 일치한다.
- 같은 버튼으로 두 번째 session이 하나만 생성되어 `Running`이 된다.
- 두 번째 완료도 정확히 한 번 처리되고 다시 로비로 복귀한다.
- Aborted에서도 빈 battle 화면에 고착되지 않는다.

Verification:

- 신규 `bs003_step2` workspace headless integration test.
- main scene GUI smoke: 실제 전투 종료 후 로비/결과 표시, 재전투 표시·진행.
- `bs002_step2`, WS-001/WS-002 영향권 회귀.

구현 결과(2026-07-13, 코드 리뷰 완료 — 판정: 완료, [[BS-003-Battle-Completion-Lobby-Return-Review]]):

- `workspace.tscn` 배선: `Windows/WorldWindow/Panels/HubPanels/ResultLabel`(Label, text "최근 전투: 없음") 추가,
  root `LobbyBattleEntry`의 `_resultLabel` export를 그 Label로 배선(node_paths에 추가). `_outgamePresetId`는 기본
  "outgame"이라 별도 배선 불필요.
- 신규 테스트: `bs003_step2_workspace_roundtrip_test`(23 assert ALL PASS) — workspace 로드 → outgame(hub, 라벨
  "없음") → **버튼 → battle preset(World battle) + mount 표시 + 라벨 "진행 중"** → 상대 kill 완료 → **World hub 복귀
  + BattleMount.Visible=false + HubPanels 재노출 + 라벨 "승리" + session 무잔존 + 정적 정리**; **2차 버튼 → 두 번째
  session 하나만 Running → 완료 → 다시 hub 복귀**(seed/정적 context/Node 격리).
- 검증: `dotnet build` 경고/오류 0, `--import` parse/script 에러 0, 회귀 `bs003_step1`·`bs002_step1/2`·
  `ws001_step1`·`ws002_step2`(41)/`step4`(40)·`bs001_step1~3` ALL PASS. ResultLabel 추가가 3-window registry/hub
  패널 테스트에 무영향.
- **남은 GUI 수동 smoke(headless 불가)**: 실제 전투 종료 후 화면에 결과 문구/hub 표시, 같은 버튼으로 재전투 표시·진행.
- 코드 리뷰: P0/P1/P2 발견 없음. workspace 씬 배선, 버튼 기반 왕복, mount/session/static cleanup, 2회 연속
  재진입을 확인했다. TacticBoard/Report outgame 상태는 Step 2 테스트에서 직접 단언하지 않고 기존 preset 회귀로
  보존된다.

## Step 3: Docs and Completion Review

Goal:

- 로비↔전투 v0 왕복 계약과 다음 게임 진행 경계를 Wiki에 고정한다.

Scope:

- [[Battle-Session-System]], [[Workspace-Window-System]], [[Current-State]], [[Open-Tasks]], 본 Task 갱신.
- `50_Reviews/BS-003-Battle-Completion-Lobby-Return-Completion-Review.md` 작성.

Done condition:

- Step 0~2 완료 조건과 검증 결과가 완료 리뷰에서 대조된다.
- P0/P1 없음, 코드와 문서가 일치한다.
- 정산·이월·DungeonRun·스토리가 후속임이 명확하다.

Verification:

- 문서 링크/현재 사실 정적 검토.
- 신규/영향권 회귀 결과 재확인.

구현 결과(2026-07-13, **제품 코드 변경 없음** — 문서/완료 리뷰만):

- [[Workspace-Window-System]] "Lobby Battle Entry (BS-002 / BS-003)" 절에 완료 소비(cleanup-first → mount 숨김 →
  outgame preset → 결과 라벨)·ResultLabel 배선·검증 반영. [[Battle-Session-System]] "Consumers"에 BS-003 왕복 갱신.
  [[Current-State]] Combat에 BS-003 추가, [[Open-Tasks]] 상태 갱신.
- 완료 리뷰 [[BS-003-Battle-Completion-Lobby-Return-Completion-Review]](Step 0~3 대조, Verification Matrix, 판정:
  코드/headless 완료 + GUI smoke 대기).
- 검증 재확인: `dotnet build` 경고/오류 0, `--import` exit 0, `bs003_step1`(30)/`bs003_step2`(23) ALL PASS, 회귀
  bs002·bs001·ws001·ws002 ALL PASS.
- 판정: **코드·headless 계약 충족, P0/P1 없음.** Done condition의 "결과 문구 표시/재전투 화면 전환" 픽셀 렌더만 GUI
  수동 smoke가 남았고 이는 headless 한계다. 오너가 main scene에서 왕복을 확인하면 최종 완료.

## Verification Matrix

필수 신규 검증:

- Victory → 승리 표시 + hub 복귀 + cleanup.
- Defeat → 패배 표시 + hub 복귀 + cleanup.
- Aborted → 시작 실패 표시 + hub 복귀 + cleanup.
- 첫 턴 lethal 즉시 완료 경로도 동일한 전환.
- outgame preset/presentation 실패에도 active/session 누수 없음.
- 버튼 기반 두 번 연속 전투: session 하나씩, 정적 context/결과 격리, 각 완료 정확히 1회.
- 새 전투 진입 시 mount 재표시 + battle preset 재적용.

필수 회귀 후보:

- `bs002_step1_entry_test`, `bs002_step2_workspace_test`
- `bs001_step1_lifecycle_test`, `bs001_step2_session_start_test`, `bs001_step3_completion_test`
- `ws001_step1_registry_test`, `ws001_step2_geometry_test`
- `ws002_step2_master_dock_test`, `ws002_step4_window_diet_test`
- `dotnet build AutoCrawler.sln -c Debug`, Godot headless `--import`

Known Regressions의 SK-001/CB-001 로스터 실패는 [[Open-Tasks]] rebaseline 소관이며 BS-003 회귀와 구분한다.

## Risks

- completion handler에서 preset/presentation을 cleanup보다 먼저 수행하면 예외나 실패가 active/session 누수로 이어질 수 있다.
- mount를 다시 숨기지 않으면 hub 복귀 후 투명/빈 viewport가 HubPanels 입력과 렌더를 가릴 수 있다.
- session `QueueFree`가 프레임 끝까지 남는 동안 재전투를 동기 시작하면 잠깐 두 session Node가 공존할 수 있다.
  제품 재진입은 다음 UI 입력부터 허용하고, 테스트의 Node 무잔존은 다음 process frame 이후 관찰한다(OD5).
- 결과 문자열이 도메인 enum 해석과 섞이면 이후 현지화/상세 결과 확장 시 controller가 비대해질 수 있다. v0 mapping은
  최소화한다.
- production 고정 request가 너무 빨리 끝나 GUI에서 결과 전환을 관찰하기 어려울 수 있다. 자동 계약과 GUI 관찰을
  분리한다.

## Follow-ups

- DS-001 DemoState: 거점 상태/행동/인카운터 선택과 자동 국면 전환.
- DL-001 U3 DungeonRun: PartySnapshot/Encounter, 전투 간 HP·마나·ammo 이월, 보상/XP/주차/층 진행.
- 특별 동료 등장 이벤트와 단순 다이얼로그 연결.
- GameLog live 전투 결과·kill/death 발행.
- 상세 결과/리포트/replay, 후퇴/round limit.

## Related

- [[BS-003-Battle-Completion-Lobby-Return-Review]]
- [[BS-003-Battle-Completion-Lobby-Return-Completion-Review]]
- [[BS-002-Lobby-to-Battle-Entry-Integration]]
- [[BS-002-Lobby-to-Battle-Entry-Integration-Completion-Review]]
- [[BS-001-Battle-Session]]
- [[Battle-Session-System]]
- [[Workspace-Window-System]]
- [[WS-002-Embedded-MDI-Master-Window]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[STEP_REVIEW_WORKFLOW]]


