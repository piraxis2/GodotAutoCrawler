---
type: system
project: AutoCrawler
system: Workspace
updated: 2026-07-09
tags: [system, workspace, ui, window, outgame]
---

# Workspace Window System

아웃게임 UI의 기반이다. 단일 메뉴 화면이 아니라 **독립 Godot `Window`들의 작업대**로, 각 기능 창이
독립적으로 열리고 닫히며 프리셋으로 정렬되고 배치가 저장/복원된다.

**WS-001 W0(Step 1~3) 완료**: native window skeleton + preset apply/gather + layout persistence
([[WS-001-Native-Window-Workspace-Shell]], [[WS-001-Native-Window-Workspace-Shell-Review]] 판정 완료).
W0는 각 창의 실제 콘텐츠가 아니라 창 lifecycle·preset·persistence **계약**을 세운다. 창 내용은 placeholder다.

## 위치

- `Assets/Scenes/workspace.tscn` — 셸 entry scene. `godot --path . res://Assets/Scenes/workspace.tscn`으로 직접 실행한다. `run/main_scene`은 여전히 `battle_field.tscn`이다.
- `Assets/Script/UI/Window/WorkspaceShell.cs` — root/main window 역할. 창 toggle 버튼, preset 버튼, `창 모아오기`/`배치 저장`/`배치 복원` 버튼을 만든다.
- `Assets/Script/UI/Window/WorkspaceWindowManager.cs` — managed window registry + preset/gather/persistence 오케스트레이션.
- `Assets/Script/UI/Window/WorkspaceWindow.cs` — managed window 공통 기반(`Godot.Window` 직접 상속).
- `Assets/Script/UI/Window/WorkspaceGeometry.cs` — Window를 모르는 순수 geometry 로직(D7).
- `Assets/Script/UI/Window/WorkspaceLayoutPreset.cs` — preset 테이블 + `WindowLayout`/`WorldContentMode`.
- `Assets/Script/UI/Window/WorkspaceLayoutStore.cs` — Window를 모르는 순수 저장 로직(`ConfigFile` 래퍼).

## Managed Window Set (v0)

`World`, `Situation`, `WeeklyAction`, `Calendar`, `Log` 5종. 전부 `WorkspaceWindow`(placeholder Label)다.
Root/Main window(셸)는 managed set에 포함하지 않는다. `World` 창만 `content_mode`(`hub`/`battle`/`replay`)를
가진다 — Godot `Window.ModeEnum`과 다른 개념이라 별도 이름을 쓴다(D4).

## Window Lifecycle

- **등록**: `WorkspaceWindowManager._Ready`가 `_windowRoot`의 `WorkspaceWindow` 자식을 `WindowId`로 자동 등록한다. 빈 id·중복 id·freed 인스턴스는 fail-closed(등록 거부 + `push_error`).
- **표시/숨김**: `ShowWindow`/`HideWindow`/`ToggleWindow`. 이미 보이는 창에 `ShowWindow`를 불러도 position/size를 되돌리지 않는다(duplicate open).
- **닫기 = hide**: `WorkspaceWindow`는 `close_requested`(OS 닫기 버튼 포함)를 `Hide()`로 처리한다. queue_free하지 않으므로 registry가 유지되고 메뉴에서 다시 부른다. 셸의 toggle 버튼은 창의 `VisibilityChanged`를 구독해, 어떤 경로로 숨겨져도 버튼 상태가 창을 따라간다.
- **stale 방어**: `TryGetWindow`/`PruneStaleEntries`가 `IsInstanceValid` + `IsQueuedForDeletion`으로 freed entry를 제거한다. 같은 id로 새 인스턴스를 다시 등록할 수 있다.
- **최소화 동기화**: 신규 manager가 자기 registry만 보고 수행한다. root 최소화 직전에 **보이던** 창만 기억했다가 복원하므로, 사용자가 닫아둔 창은 복원에서 다시 뜨지 않는다. Godot에 root 최소화 signal이 없어 `_Process` 폴링을 쓴다. managed window는 기존 `WindowManager` autoload의 `sub_windows` 그룹에 등록하지 않아 `Mode`/`previous_mode` meta 경합이 없다. headless는 root를 Minimized로 보고하므로 폴링을 끈다.

## Preset (D3)

`outgame`/`battle`/`analysis` 3종. preset은 고정 화면이 아니라 각 창에 `{visible, offset, size, content_mode}`를
적용하는 명령이다. `ApplyPreset(id)`:

- unknown preset id는 아무것도 바꾸지 않고 fail-closed. preset이 아직 없는 창을 참조하면 그 창만 건너뛴다.
- `World` 창은 새로 만들지 않고 같은 인스턴스의 `content_mode`/position/size만 바꾼다.
- 창을 먼저 `Show`한 뒤 좌표를 적용한다(native window가 있어야 실제 decoration 크기를 읽는다). 좌표 적용 시 `InitialPosition = Absolute`로 바꿔 다음 `Show`가 좌표를 덮어쓰지 않게 한다.
- **preset offset은 절대 좌표가 아니라 대상 screen work area 원점 기준 오프셋이다.** W0 좌표는 임시값이다(OD4).

## Safe Work Area / Gather (D5)

`GatherWindows()`는 화면 밖으로 밀린 창을 회수한다. 계산은 전부 `WorkspaceGeometry` 순수 함수다:

- **모든 판정·clamp는 decoration 포함 rect 기준이다.** Godot `Window.Position`은 client 좌표라, 화면 안으로 clamp할 때 엔진은 client rect만 보고 decoration(타이틀바)을 무시한다 — 그러면 타이틀바가 화면 밖에 남아 창을 잡을 수 없다(WS-001 Step 1 실측 버그).
- **대상 screen**은 decoration rect 중심이 포함된 screen이고, 없으면 primary다. screen 원점이 `(0,0)`이라고 가정하지 않는다(실측: `screen[0] pos=(0,542)`).
- **잡을 수 있음(reachable)** = 타이틀바 띠(32px)가 세로로 온전히 work area 안 + 가로로 최소 64px 겹침.
- 회수 시 work area보다 큰 창은 먼저 축소한 뒤 clamp한다. 이미 잡을 수 있는 창은 건드리지 않는다(gather는 멱등).
- headless는 screen이 0개라 usable rect가 비므로, work area를 프로젝트 viewport(1440x960)로 fallback한다.

## Persistence (D5, OD2 후보 B)

- 저장 파일 `user://workspace_layout.cfg`. 전역 `user://settings.ini`와 분리 — 손상 시 이 파일만 폐기해도 다른 설정을 잃지 않는다.
- 창별 `position`/`size`/`visible`/`content_mode`를 저장한다. `[meta] version` 키를 항상 기록한다.
- `SaveLayout()`은 registry 스냅샷을 저장, `LoadLayout()`은 복원 후 **항상 gather**를 돌려 저장 당시와 화면 구성이 달라 밖으로 나간 창을 회수한다.
- **fallback**(모두 기본 preset `outgame`으로 복구, 파일 파괴 안 함): 파일 없음 = FirstRun / 파싱 실패 = Corrupt / version 누락·미지 = UnsupportedVersion. 개별 창 값이 깨지면(타입 오류·키 누락·size ≤ 0) 그 창만 건너뛰고 나머지는 복원한다.
- **자동 복원/저장은 entry scene에서만** 켠다(`WorkspaceShell._restoreLayoutOnReady`). 시작 시 `LoadLayout`(첫 실행이면 기본 preset), 종료 시 root `CloseRequested`로 `SaveLayout` 후 quit(`AutoAcceptQuit=false`). 테스트는 트리 진입 전에 이 플래그를 꺼서 raw registry를 관찰한다.

## 설계 경계 (W0 범위 밖)

- custom preset override 저장(OD7), 창 가장자리 스냅 UX, `run/main_scene` 전환. 각각 후속 Task.
- 각 창의 실제 콘텐츠(DemoState/BattleSession/TacticBoard/Report 등)는 이후 자기 Window에 장착된다.
- root window resizable 이후 `battle_field.tscn` stretch 정책은 별도 후속 결정이다([[Open-Tasks]]).

## 검증

헤드리스 테스트 3종(`Assets/Script/Tests/ws001_step1~3`):

- `ws001_step1_registry_test`(62): registry 등록/중복/stale, close=hide, duplicate open, 최소화 동기화, 실제 씬 배선.
- `ws001_step2_geometry_test`(66): 순수 geometry(대상 screen·reachable·clamp·tiny screen·gather 멱등), preset show/hide, World 인스턴스 유지, off-screen 회수. Step 1 타이틀바 버그 회귀 포함.
- `ws001_step3_persistence_test`(40): store round-trip, first-run/corrupt/version/타입 fallback, manager save→새 인스턴스 load 복원, load 후 gather 회수.

진단 전용: `ws001_window_geometry_probe`(GUI 실행에서 client/decoration/screen rect를 텍스트 출력, gather 계약 검증용).

## Related

- [[WS-001-Native-Window-Workspace-Shell]]
- [[WS-001-Native-Window-Workspace-Shell-Review]]
- [[Current-State]]
- [[Open-Tasks]]
