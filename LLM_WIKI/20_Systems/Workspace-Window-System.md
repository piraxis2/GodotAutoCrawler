---
type: system
project: AutoCrawler
system: Workspace
updated: 2026-07-10
tags: [system, workspace, ui, window, mdi, outgame]
---

# Workspace Window System

아웃게임 UI의 기반이다. 현재 기본 동작은 **native transient child-window workspace**다. `workspace.tscn`이 마스터/메뉴/로그 도크 역할을 하고, 기능 창은 Godot `Window` OS 창으로 떠서 마스터 윈도우 밖으로 이동할 수 있지만 마스터에 종속된 transient 하위 창으로 유지된다. 따라서 하위 창은 마스터 위 z-order를 유지하고 별도 작업표시줄 항목을 만들지 않는 계약이다. 마스터는 좌측 메뉴, 하단 로그 도크, 창 배치 프리셋, 슬롯 스냅, 배치 저장/복원을 소유한다.

**WS-002 W0(Step 0~5) 완료**: WS-001 native window skeleton을 임베디드 MDI로 전환하고, 마스터 윈도우 + 하단 로그 도크 + 슬롯 스냅 + 창 다이어트 + E-7 프리셋 v1을 적용했다([[WS-002-Embedded-MDI-Master-Window]], [[WS-002-Embedded-MDI-Master-Window-Completion-Review]]). W0는 창 시스템과 placeholder 구조를 세우며, 실제 콘텐츠 장착은 후속이다.

## 위치

- `Assets/Scenes/workspace.tscn` — workspace entry scene. `godot --path . res://Assets/Scenes/workspace.tscn`으로 직접 실행한다. `run/main_scene`은 여전히 `battle_field.tscn`이다.
- `Assets/Script/UI/Window/WorkspaceShell.cs` — 마스터 메뉴를 구성한다. 창 toggle, preset, `창 모아오기`, `배치 저장`, `배치 복원` 버튼을 만든다.
- `Assets/Script/UI/Window/WorkspaceWindowManager.cs` — managed window registry, 마스터 content rect, preset apply, gather, 슬롯 스냅, persistence 오케스트레이션.
- `Assets/Script/UI/Window/WorkspaceWindow.cs` — managed window 공통 기반(`Godot.Window` 직접 상속). content mode 라벨과 hub 전용 패널 gating을 처리한다.
- `Assets/Script/UI/Window/WorkspaceGeometry.cs` — Window를 모르는 순수 geometry 로직. native 회귀용 work area 함수와 embedded content/slot 함수가 함께 있다.
- `Assets/Script/UI/Window/WorkspaceLayoutPreset.cs` — E-7 프리셋 v1, 정규화 rect resolve, `WorldContentMode`, `TacticBoardContentMode`.
- `Assets/Script/UI/Window/WorkspaceLayoutStore.cs` — Window를 모르는 순수 저장 로직(`ConfigFile` 래퍼).

## Managed Window Set

Floating managed window는 3종이다.

| id | 창 | 역할 |
| --- | --- | --- |
| `world` | World | `hub`/`battle`/`replay` content mode. hub 모드에는 Calendar/Danger/Action placeholder 패널이 보이고, battle/replay에서는 숨긴다. |
| `tacticboard` | TacticBoard | 전투 `read`, 분석 `edit` placeholder content mode. |
| `report` | Report | 분석 placeholder 창. |

`Log`는 floating window가 아니라 마스터 하단 `CanvasLayer/LogDock/LogView`에 상주하며 `LogWindowController`가 붙어 있다. WS-001의 `Situation`/`WeeklyAction`/`Calendar` 독립 창은 제거됐고, 해당 placeholder 성격은 World hub 내부 패널로 이관됐다. Root/Main(`WorkspaceShell`)은 managed set에 없다.

## Master Layout

- 전역 `display/window/subwindows/embed_subwindows=false`로 Godot subwindow를 OS native window로 분리한다. 각 managed `WorkspaceWindow`는 GUI 실행에서 `Transient=true`로 설정되어 마스터 밖 이동은 가능하지만 마스터 종속 하위 창으로 동작한다.
- 마스터 content area는 `master rect - 좌측 메뉴바(200px) - 하단 로그 도크(마스터 높이 20%) - 상단 타이틀바 inset(32px)`이다.
- `WorkspaceGeometry.ComputeContentRect`는 마스터가 메뉴/도크보다 작아도 content rect의 시작·끝이 마스터 경계 안에 남도록 clamp한다.
- `GatherIntoContentArea()`는 수동 `창 모아오기` 경로에서 창의 보이는 영역(decoration rect)을 content area 안으로 회수한다. 기본 드래그/저장 복원 경로에서는 native pop-out 좌표를 보존한다.
- embedded/headless mode일 때만 root `SizeChanged` 발생 시 자동 content gather를 수행한다. native transient mode에서는 마스터 밖 배치를 보존하되, 복원 좌표가 마스터가 있는 모니터 작업영역 밖이면 같은 모니터 안으로 회수한다.
- 로그 도크 rect는 마스터 크기에서 재계산되는 파생값이므로 저장 대상이 아니다.

## Window Lifecycle

- **등록**: `WorkspaceWindowManager._Ready`가 `_windowRoot`의 `WorkspaceWindow` 자식을 `WindowId`로 자동 등록한다. 빈 id·중복 id·freed 인스턴스는 fail-closed다.
- **표시/숨김**: `ShowWindow`/`HideWindow`/`ToggleWindow`. 이미 보이는 창에 `ShowWindow`를 불러도 position/size를 되돌리지 않는다.
- **닫기 = hide**: `WorkspaceWindow`는 `CloseRequested`를 `Hide()`로 처리한다. queue_free하지 않으므로 registry가 유지되고 메뉴에서 다시 부른다.
- **stale 방어**: `TryGetWindow`/`PruneStaleEntries`가 freed entry를 제거한다.
- **최소화 동기화**: root 최소화 직전에 보이던 창만 기억했다가 복원한다. headless에서는 root가 Minimized로 보이므로 폴링을 끈다.

## Preset And Slot Snap

`outgame`/`battle`/`analysis` 3종이다. preset은 각 창에 `{visible, normalized rect, content_mode}`를 적용한다. 정규화 rect는 마스터 content area 대비 비율이고, 실제 px는 런타임 content rect에서 resolve한다.

- `outgame`: World `hub`가 content를 채운다. TacticBoard/Report는 숨김. 로그는 도크.
- `battle`: World `battle` 64%, TacticBoard `read` 33%. Report 숨김. 로그는 도크.
- `analysis`: Report 34%, TacticBoard `edit` 36%, World `replay` 26%. 로그는 도크.

slot snap은 현재 preset의 visible slot을 후보로 쓴다. 자석 거리는 content area 짧은 변의 6%다. tie-break는 슬롯 중심 Y -> X -> id ordinal 순서이고, hidden slot은 후보에서 제외된다. Alt 입력은 스냅 disable flag다. 드래그 중 `SlotHighlight`가 후보 slot을 표시하고, drop 시 창의 보이는 영역이 slot rect에 맞춰진다.

## Persistence

- 저장 파일은 `user://workspace_layout.cfg`다. 전역 `settings.ini`와 분리한다.
- 현재 version은 `2`다. WS-001 native 좌표/구 창 세트로 저장된 v1 파일은 `UnsupportedVersion`으로 처리하고 기본 preset으로 fallback한다. 파일은 파괴하지 않는다.
- floating managed 창 3종의 `position`/`size`/`visible`/`content_mode`만 저장한다. 로그 도크는 저장하지 않는다.
- 파일 없음/파싱 실패/미지 version/개별 창 타입 오류는 모두 fail-closed다. `LoadLayout()`은 복원 후 항상 content gather를 돌린다.
- 자동 복원/저장은 entry scene에서만 켠다(`WorkspaceShell._restoreLayoutOnReady`).

## Legacy Boundary

- `Assets/Script/WindowManager.cs` autoload와 `Assets/Script/UI/Window/GameWindow.cs`는 WS-001/WS-002 workspace 경로가 쓰지 않는 레거시 실험 코드다.
- `WorkspaceWindowManager.GatherWindows()`와 native decoration/screen work area 함수는 WS-001 회귀 테스트 보존용으로 남아 있다. 현재 native transient production 경로는 OS window 좌표를 허용하되, preset/수동 gather/slot 계산에는 content area 기준 함수(`GatherIntoContentArea`, `ResolveSlots`, `FindSlotSnap`)를 재사용한다.
- 레거시 삭제/정리는 WS-002 범위 밖 후속 cleanup으로 분리한다.

## 검증

주요 헤드리스 검증:

- `ws001_step1_registry_test`: registry/close=hide/toggle/stale/minimize, 현재 3 floating window 씬 배선.
- `ws001_step2_geometry_test`: native geometry 회귀 + embedded content-relative preset/gather.
- `ws001_step3_persistence_test`: store round-trip, fallback, v2 persistence.
- `ws002_embedded_tween_probe`: embed 상태, World 98x79 -> 64x76 크기 tween 구조 probe.
- `ws002_step2_master_dock_test`: content rect, 로그 도크, content gather, resize hook, 3창 비겹침.
- `ws002_step3_slot_snap_test`: slot resolve, 거리/tie-break/disable, preview/commit.
- `ws002_step4_window_diet_test`: registry 3종, World hub panel gating, E-7 비율, old config fallback.

GUI 사인오프 항목은 화면 관찰이 필요하다: 창이 OS 창으로 분리되어 마스터 밖으로 드래그되는지, 하위 창이 마스터 위에 유지되고 작업표시줄 항목이 1개인지, 로그 도크 전폭 상주, 국면별 3창 병치.

## 설계 경계

- World hub 실물화, BattleSession/전장 scene 장착, TacticBoard/Report 실제 콘텐츠, DemoState와 자동 국면 전환은 후속이다.
- retro OS skin, 창 애니메이션 아트, pop-out option은 W0 범위 밖이다.
- `battle_field.tscn` stretch 정책과 `run/main_scene` 전환은 별도 결정이다([[Open-Tasks]]).

## Related

- [[WS-002-Embedded-MDI-Master-Window]]
- [[WS-002-Embedded-MDI-Master-Window-Completion-Review]]
- [[WS-001-Native-Window-Workspace-Shell]]
- [[Current-State]]
- [[Open-Tasks]]


