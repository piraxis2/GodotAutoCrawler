---
type: system
project: AutoCrawler
system: Workspace
updated: 2026-07-13
tags: [system, workspace, ui, window, mdi, outgame]
---

# Workspace Window System

아웃게임 UI의 기반이다. 현재 기본 동작은 **native owned child-window workspace**다. `workspace.tscn`이 마스터 윈도우/상단 드롭다운 메뉴바/하단 로그 도크를 소유하고, 기능 창은 Godot `Window` OS 창으로 떠서 마스터 밖으로 이동할 수 있다. 하위 창은 Windows native owner 관계로 마스터에 종속되어 마스터 위 z-order를 유지하고, 작업표시줄 스택은 마스터 1개에 가깝게 유지한다. `exclusive` modal 입력 잠금은 쓰지 않는다.

**WS-002 W0(Step 0~5) 완료**: WS-001 native window skeleton을 마스터 작업대로 승격하고, 하단 로그 도크 + 슬롯 스냅 + 창 다이어트 + E-7 프리셋 v1을 적용했다([[WS-002-Embedded-MDI-Master-Window]], [[WS-002-Embedded-MDI-Master-Window-Completion-Review]]). 이후 GUI 검증 중 임베디드 전제는 native owned child-window 방식으로 조정됐다. W0는 창 시스템과 placeholder 구조를 세우며, 실제 콘텐츠 장착은 후속이다.

## 위치

- `Assets/Scenes/workspace.tscn` — workspace entry scene. `godot --path . res://Assets/Scenes/workspace.tscn`으로 직접 실행한다. `run/main_scene`도 `workspace.tscn`(`uid://caplqnfi4j3ys`)이다.
- `Assets/Script/UI/Window/WorkspaceShell.cs` — 상단 `MenuButton` 드롭다운을 구성한다. `Windows` 메뉴는 managed 창 toggle, `Presets` 메뉴는 preset/`창 모아오기`/`배치 저장`/`배치 복원`을 제공한다.
- `Assets/Script/UI/Window/WorkspaceWindowManager.cs` — managed window registry, 마스터 content rect, preset apply, gather, 슬롯 스냅, persistence 오케스트레이션.
- `Assets/Script/UI/Window/WorkspaceWindow.cs` — managed window 공통 기반(`Godot.Window` 직접 상속). content mode 라벨과 hub 전용 패널 gating을 처리한다.
- `Assets/Script/UI/Window/WorkspaceNativeWindowOwner.cs` — Windows native pop-out 창을 마스터의 owned window로 묶는다. Godot `exclusive`는 끄고, Win32 `GWLP_HWNDPARENT`로 owner 관계를 보강한다.
- `Assets/Script/UI/Window/WorkspaceGeometry.cs` — Window를 모르는 순수 geometry 로직. native 회귀용 work area 함수와 content/slot 함수가 함께 있다.
- `Assets/Script/UI/Window/WorkspaceLayoutPreset.cs` — E-7 프리셋 v1, 정규화 rect resolve, `WorldContentMode`, `TacticBoardContentMode`.
- `Assets/Script/UI/Window/WorkspaceLayoutStore.cs` — Window를 모르는 순수 저장 로직(`ConfigFile` 래퍼).
- `Assets/UI/Theme/RetroWin98Theme.tres` — workspace 레트로/Win98 계열 Theme 리소스. 버튼, 메뉴, popup, 패널, label, `RetroVBoxPanel` bevel 색상은 여기서 수정한다.
- `Assets/Script/UI/Retro/RetroPanelContainer.cs`, `RetroVBoxPanel.cs`, `RetroWin98Style.cs` — Theme 리소스를 적용하고, `RetroVBoxPanel`의 2px bevel만 커스텀 draw한다.

## Managed Window Set

Floating managed window는 3종이다.

| id | 창 | 역할 |
| --- | --- | --- |
| `world` | World | `hub`/`battle`/`replay` content mode. hub 모드에는 Calendar/Danger/Action placeholder 패널이 보이고, battle/replay에서는 숨긴다. |
| `tacticboard` | TacticBoard | 전투 `read`, 분석 `edit` placeholder content mode. |
| `report` | Report | 분석 placeholder 창. |

`Log`는 floating window가 아니라 마스터 하단 `CanvasLayer/LogDock/LogView`에 상주하며 `LogWindowController`가 붙어 있다. WS-001의 `Situation`/`WeeklyAction`/`Calendar` 독립 창은 제거됐고, 해당 placeholder 성격은 World hub 내부 패널로 이관됐다. Root/Main(`WorkspaceShell`)은 managed set에 없다.

## Master Layout

- 전역 `display/window/subwindows/embed_subwindows=false`로 Godot subwindow를 OS native window로 분리한다.
- 각 managed `WorkspaceWindow`는 GUI 실행에서 `Transient=true`, `TransientToFocused=false`, `Exclusive=false`, `AlwaysOnTop=false`로 설정된다.
- Windows에서는 `WorkspaceNativeWindowOwner`가 visible 창에 대해 Win32 owner 관계를 보강한다. 이 경로가 하위 창의 마스터 위 z-order, 작업표시줄 단일화, 마스터 최소화 동기화에 가장 가깝다. Windows Snap Assist 대상성은 owned window 특성상 약해질 수 있다.
- 마스터 content area는 `master rect - 상단 메뉴바(32px) - 창 타이틀바 inset(32px) - 하단 로그 도크(마스터 높이 20%)`이다. 좌측 메뉴바는 제거되어 `MenuBarWidth=0`이다.
- `WorkspaceGeometry.ComputeContentRect`는 마스터가 메뉴/도크보다 작아도 content rect의 시작·끝이 마스터 경계 안에 남도록 clamp한다.
- `GatherIntoContentArea()`는 수동 `창 모아오기` 경로에서 창의 보이는 영역(decoration rect)을 content area 안으로 회수한다. 기본 드래그/저장 복원 경로에서는 native pop-out 좌표를 보존한다.
- embedded/headless mode일 때만 root `SizeChanged` 발생 시 자동 content gather를 수행한다. native owned mode에서는 마스터 밖 배치를 보존하되, 복원 좌표가 마스터가 있는 모니터 작업영역 밖이면 같은 모니터 안으로 회수한다.
- 로그 도크 rect는 마스터 크기에서 재계산되는 파생값이므로 저장 대상이 아니다.

## Window Lifecycle

- **등록**: `WorkspaceWindowManager._Ready`가 `_windowRoot`의 `WorkspaceWindow` 자식을 `WindowId`로 자동 등록한다. 빈 id·중복 id·freed 인스턴스는 fail-closed다.
- **표시/숨김**: `ShowWindow`/`HideWindow`/`ToggleWindow`. 이미 보이는 창에 `ShowWindow`를 불러도 position/size를 되돌리지 않는다.
- **닫기 = hide**: `WorkspaceWindow`는 `CloseRequested`를 `Hide()`로 처리한다. queue_free하지 않으므로 registry가 유지되고 메뉴에서 다시 부른다.
- **상단 메뉴**: `WorkspaceShell`은 `Windows` popup item을 check 상태로 동기화한다. OS close나 최소화 동기화로 창이 숨겨지면 메뉴 체크도 풀린다.
- **stale 방어**: `TryGetWindow`/`PruneStaleEntries`가 freed entry를 제거한다.
- **최소화 동기화**: root 최소화 직전에 보이던 창만 기억했다가 복원한다. headless에서는 root가 Minimized로 보이므로 폴링을 끈다.

## Preset And Slot Snap

`outgame`/`battle`/`analysis` 3종이다. preset은 각 창에 `{visible, normalized rect, content_mode}`를 적용한다. 정규화 rect는 마스터 content area 대비 비율이고, 실제 px는 런타임 content rect에서 resolve한다.

- `outgame`: World `hub`가 content를 채운다. TacticBoard/Report는 숨김. 로그는 도크.
- `battle`: World `battle` 64%, TacticBoard `read` 33%. Report 숨김. 로그는 도크.
- `analysis`: Report 34%, TacticBoard `edit` 36%, World `replay` 26%. 로그는 도크.

slot snap은 현재 preset의 visible slot을 후보로 쓴다. 자석 거리는 content area 짧은 변의 6%다. tie-break는 슬롯 중심 Y -> X -> id ordinal 순서이고, hidden slot은 후보에서 제외된다. Alt 입력은 스냅 disable flag다. 드래그 중 `SlotHighlight`가 후보 slot을 표시하고, drop 시 창의 보이는 영역이 slot rect에 맞춰진다.

## Lobby Battle Entry (BS-002)

World hub를 게임 로비로 보고, 로비의 `전투 시작` 버튼에서 기존 `BattleSession`으로 전투를 시작하는 단방향 진입을
연결했다([[BS-002-Lobby-to-Battle-Entry-Integration]], [[Battle-Session-System]]).

- `Assets/Script/UI/Window/LobbyBattleEntry.cs`(root `LobbyBattleEntry` 노드): 고정 v0 `BattleRequest`
  (export `PackedScene`/`PlayerPath`/`seed`)로 활성 `BattleSession` 하나를 생성·장착·시작한다. `TryEnterBattle`은
  active 중복·null request·mount 미배선·preset 실패를 fail-closed(false)하고, 완료(Victory/Defeat/Aborted)는
  구독 해제 → active 해제 → session `QueueFree`만 한다(결과 표시/로비 복귀 없음, ADR-022).
- World 창의 전투 표시 영역은 `Windows/WorldWindow/BattleMount`(`SubViewportContainer`, stretch, 기본 `visible=false`)
  + `BattleViewport`(`SubViewport`)다. 세션은 이 SubViewport 아래에 장착돼 UI 좌표계와 격리된 채 렌더된다. mount
  가시성은 entry controller가 소유한다(`TryEnterBattle`에서 표시).
- `전투 시작` 버튼은 `WorldWindow/Panels/HubPanels/EnterBattleButton`이다. hub content mode에서만 보이며, 진입 시
  entry가 `ApplyPreset("battle")`(World `battle` 64% + TacticBoard `read` 33%, HubPanels 숨김)를 적용한다.
- 검증: `bs002_step1_entry_test`(seam), `bs002_step2_workspace_test`(버튼→preset→session Running + mount 배선 +
  중복 차단). 실제 전투가 World client에 픽셀로 표시되고 자동 턴이 진행되는지는 GUI 수동 smoke 사인오프다.

## Theme

Workspace의 레트로/Win98 계열 스타일은 코드 생성 Theme가 아니라 `Assets/UI/Theme/RetroWin98Theme.tres` 리소스가 소유한다.

- Theme에서 수정하는 것: Button/CheckButton/MenuButton/PopupMenu/PanelContainer/Label 색상과 stylebox, `VBoxContainer` separation, `RetroVBoxPanel` bevel 색상.
- 코드가 유지하는 것: `RetroPanelContainer`는 Theme fallback load만 수행한다. `RetroVBoxPanel`은 Theme 색상을 읽어 2px bevel 선을 그린다. 즉 색과 여백 값은 `.tres`, 특수 드로잉 로직은 C#에 남는다.
- `workspace.tscn`은 `RetroWin98Theme.tres`를 ext_resource로 연결하고, `TopMenuBar`/`LogDock`/각 창 내부 패널에 `theme = ExtResource("7_retro_theme")`를 지정한다.

## Persistence

- 저장 파일은 `user://workspace_layout.cfg`다. 전역 `settings.ini`와 분리한다.
- 현재 version은 `2`다. WS-001 native 좌표/구 창 세트로 저장된 v1 파일은 `UnsupportedVersion`으로 처리하고 기본 preset으로 fallback한다. 파일은 파괴하지 않는다.
- floating managed 창 3종의 `position`/`size`/`visible`/`content_mode`만 저장한다. 로그 도크는 저장하지 않는다.
- 파일 없음/파싱 실패/미지 version/개별 창 타입 오류는 모두 fail-closed다.
- 자동 복원/저장은 entry scene에서만 켠다(`WorkspaceShell._restoreLayoutOnReady`).

## Legacy Boundary

- `Assets/Script/WindowManager.cs` autoload와 `Assets/Script/UI/Window/GameWindow.cs`는 WS-001/WS-002 workspace 경로가 쓰지 않는 레거시 실험 코드다.
- `WorkspaceWindowManager.GatherWindows()`와 native decoration/screen work area 함수는 WS-001 회귀 테스트 보존용으로 남아 있다. 현재 native owned production 경로는 OS window 좌표를 허용하되, preset/수동 gather/slot 계산에는 content area 기준 함수(`GatherIntoContentArea`, `ResolveSlots`, `FindSlotSnap`)를 재사용한다.
- 레거시 삭제/정리는 WS-002 범위 밖 후속 cleanup으로 분리한다.

## 검증

주요 헤드리스 검증:

- `ws001_step1_registry_test`: registry/close=hide/toggle/stale/minimize, 현재 3 floating window 씬 배선, 상단 dropdown 메뉴 wiring.
- `ws001_step2_geometry_test`: native geometry 회귀 + content-relative preset/gather.
- `ws001_step3_persistence_test`: store round-trip, fallback, v2 persistence.
- `ws002_embedded_tween_probe`: embed 상태, World 98x79 -> 64x76 크기 tween 구조 probe.
- `ws002_step2_master_dock_test`: content rect, 로그 도크, content gather, resize hook, 3창 비겹침.
- `ws002_step3_slot_snap_test`: slot resolve, 거리/tie-break/disable, preview/commit.
- `ws002_step4_window_diet_test`: registry 3종, World hub panel gating, E-7 비율, old config fallback.
- `gl001_step2_log_window_test`: Theme 전환 후 로그 도크 UI 회귀.

최근 검증(2026-07-11): `dotnet build` 경고/오류 0, `ws001_step1_registry_test` ALL PASS, `ws002_step2_master_dock_test` 41 passed, `ws002_step4_window_diet_test` 40 passed, `gl001_step2_log_window_test` ALL PASS.

GUI 사인오프 항목은 화면 관찰이 필요하다: 창이 OS 창으로 분리되어 마스터 밖으로 드래그되는지, 하위 창이 마스터 위에 유지되고 작업표시줄 항목이 1개인지, 상단 dropdown 메뉴/하단 로그 도크/Theme 시각 결과가 의도와 맞는지.

## 설계 경계

- BattleSession/전장 scene의 로비→전투 단방향 진입은 BS-002가 연결했다(위 Lobby Battle Entry). 결과 표시·정산·로비
  복귀·재전투·전투 창 resize/aspect/input 고도화, World hub 실물화, TacticBoard/Report 실제 콘텐츠, DemoState/자동
  국면 전환은 후속이다.
- 레트로 Theme는 W0 첫 패스다. 실제 조조전풍 컴포넌트 세트, 폰트, 아이콘, 상세 여백/밀도 조정은 후속이다.
- Windows Snap Assist와 native owned child-window 요구는 trade-off가 있다. OS Snap이 필요해지면 자체 슬롯 스냅 강화 또는 owner 관계 정책 재검토가 필요하다.
- `battle_field.tscn` stretch/aspect 정책은 별도 결정이다. `run/main_scene`은 이미 `workspace.tscn`으로 전환됐다([[Open-Tasks]]).

## Related

- [[WS-002-Embedded-MDI-Master-Window]]
- [[WS-002-Embedded-MDI-Master-Window-Completion-Review]]
- [[WS-001-Native-Window-Workspace-Shell]]
- [[BS-002-Lobby-to-Battle-Entry-Integration]]
- [[Battle-Session-System]]
- [[Current-State]]
- [[Open-Tasks]]
