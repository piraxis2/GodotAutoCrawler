---
id: WS-001
type: task
status: complete
system: Workspace
created: 2026-07-08
updated: 2026-07-09
tags: [task, workspace, ui, window, outgame, mockup]
---

# Native Window Workspace Shell

## Goal

아웃게임 UI의 기반을 처음부터 **독립 `Window`들의 작업대**로 구성한다.

플레이어는 단일 메뉴 화면을 오가는 것이 아니라, 필요한 창을 띄워놓고 배치하면서 AI 운용/성장/전투 관찰 작업을 채워나간다. 따라서 W0의 목표는 예쁜 최종 UI가 아니라, 각 기능 창이 독립적으로 열리고 닫히며, 프리셋으로 다시 정렬되고, 배치가 저장/복원되는 작업대 셸을 세우는 것이다.

핵심 방향:

- `세계 창`, `상황판 창`, `행동 창`, `캘린더 창`, `로그 창`은 처음부터 별도 Godot `Window`로 분리한다.
- Main/Root window는 게임 콘텐츠 화면이 아니라 `WorkspaceWindowManager`, 메뉴바, 프리셋 전환, 창 재호출/회수의 중심이다.
- 기본 프리셋 배치는 사용자가 나중에 조정할 수 있게 하되, 시스템은 창별 position/size/visible/content_mode를 저장하고 복원할 수 있어야 한다.
- 임베디드 MDI/가짜 패널 방식은 1차 방향에서 제외한다. 네이티브 Window를 우선 검증한다.

## User Outcome

- 사용자는 상황판을 보면서 행동 창에서 주간 행동을 선택하고, 필요하면 세계 창이나 택틱 보드 창을 옆에 띄워 작업할 수 있다.
- 창을 닫아도 메뉴에서 다시 부를 수 있고, 화면 밖으로 밀린 창은 "창 모아오기"로 회수할 수 있다.
- 아웃게임/전투/분석 프리셋은 특정 UI 화면이 아니라, 독립 창들의 위치와 표시 상태를 정렬하는 명령으로 동작한다.
- 이후 DemoState, BattleSession, 택틱 보드, 리포트 등은 각자 자기 Window에 장착되며 확장된다.

## Context

- 기획서 `06_UXUI.md` E-2/E-7은 PC 데스크톱 + 멀티윈도우 워크스페이스를 확정 방향으로 둔다.
- 기획서 `08_데모_스코프.md` G-6은 W0 워크스페이스 셸을 최우선 조립 단위로 제안한다.
- 현재 프로젝트 사실:
  - `project.godot`의 `run/main_scene`은 `uid://dv7r53qteumxi`이며, 이는 `Assets/Scenes/Map/battle_field.tscn`이다.
  - `main.tscn`은 존재하지만 현재 main scene이 아니다. WS-001 구현자는 임의로 main scene을 교체하거나 `battle_field.tscn`에 셸을 섞어 넣지 않는다. Step 1에서 셸 entry scene/launch path를 명시적으로 결정한다.
  - `project.godot` display 설정은 (Step 1 착수 전 기준) `resizable=false`, `minimize_disabled=true`, `maximize_disabled=true`다. 이 설정 그대로는 메인 창 최소화/복원 동기화를 관찰할 수 없다. Open Decision 6에 따라 Step 1에서 변경한다.
  - `project.godot`에 stretch mode 설정이 없다(`display/window/stretch/*` 미지정). 따라서 root window를 resizable로 바꾸면 크기 변경이 viewport 표시에 직접 노출된다. 기존 씬의 회귀 확인 대상이다.
  - `Assets/Script/WindowManager.cs`는 현재 autoload이며, 메인 창 최소화 시 `sub_windows` 그룹을 함께 최소화/복원하려는 기존 실험 코드다. 다만 위 display 설정 때문에 최소화 분기는 사실상 도달하지 않는다.
  - `Assets/Script/UI/Window/GameWindow.cs`는 기존 스냅 실험 코드다. `_snapTarget` null 미검사, main window id 0 기준 좌표, position 변경 알림 중 `Position` 재대입으로 preset/gather와 경합할 수 있다.
  - `Assets/UI/Window/field_window.tscn`은 `Window` 루트가 아니라 `Node` 루트이며 자기 자신을 `ext_resource`로 재귀 인스턴스한다. WS-001의 Window 자산으로 재사용하지 않는다.
  - `Assets/UI/Window/inspector.tscn`은 실제 `Window` 루트지만 `popup_window=true`, `min_size == max_size == 400x600`, `unresizable=true`인 고정 inspector 실험 자산이다. workspace 창의 기반 scene으로 재사용하지 않는다.
  - `Assets/Script/gds/config_file_handler.gd`는 autoload에 등록되어 있지 않고 `video` 섹션 중심의 `user://settings.ini` helper다. workspace layout 저장에는 신규 config wrapper를 둔다.

## Scope

- 독립 `Window` 기반 Workspace Shell 설계.
- Window registry/manager 책임 정의.
- 창 종류와 최소 placeholder 내용 정의.
- 프리셋 적용/창 재호출/창 모아오기/배치 저장·복원 정책 정의.
- W0 구현을 독립 Step으로 나눌 수 있는 Task 계획.
- 디자인 리뷰 이후 구현할 검증 방법 정의.

## Out of Scope

- 주간 행동 규칙, DemoState, WorldState schema 구현.
- 전투 세션 함수화, 전투 결과 정산.
- 택틱 보드 편집/BT 컴파일.
- 리포트 원인 분석, 리플레이 타임라인.
- 최종 레트로 OS 스킨, 아이콘 아트, 애니메이션.
- 네이티브 Window pop-out과 임베디드 MDI를 동시에 지원하는 추상화.
- 사용자별/상황별 custom preset override 저장 정책. W0은 기본 preset과 마지막 창 배치 저장까지만 다룬다.
- 스냅 UX. W0은 preset/gather/persistence 기반을 먼저 세우며, 창 가장자리 스냅은 후속으로 둔다.
- Steam Deck/모바일 대응. 단, 작은 화면 리스크는 후속으로 기록한다.

## Design Principles

### D1. 창이 기능 소유의 기본 단위다

상황판, 행동, 캘린더, 로그, 세계 뷰는 하나의 큰 Control 아래 배치되는 패널이 아니라 독립 `Window`다. 각 기능은 자기 Window 안에서 UI와 입력을 키워나간다.

### D2. Root window는 작업대 관리자다

Root/Main window는 콘텐츠를 직접 담는 주 화면이 아니다. 메뉴바, 창 목록, 프리셋 버튼, 창 모아오기, 저장/복원 같은 작업대 제어를 담당한다.

### D3. 프리셋은 레이아웃이 아니라 명령이다

아웃게임/전투/분석 프리셋은 고정 화면이 아니라 Window들의 `{visible, position, size, content_mode}`를 적용하는 명령이다. 사용자가 프리셋 이후 창을 옮기면 그 배치를 저장할 수 있어야 한다. 상황별 custom preset override는 W0 범위 밖이다.

### D4. 세계 창은 하나만 유지한다

`세계 창`은 Hub/Battle/Replay content mode가 바뀌는 주인공 창이다. 전환 때 새 창을 만들기보다 같은 Window의 content를 바꾸고, 프리셋은 position/size를 조정한다. Godot `Window.ModeEnum`과 충돌하지 않도록 이 값은 `content_mode`라고 부른다.

### D5. 창 손실 방지가 W0의 핵심 UX다

네이티브 Window는 멀티모니터 작업에 강하지만, 화면 밖 이동/최소화/닫힘/메인 창과 수명 주기 어긋남 위험이 있다. 따라서 W0에서 `창 모아오기`, 닫은 창 재호출, 메인 창 최소화/복원 동기화, 배치 저장/복원을 먼저 검증한다. 메인 창 최소화/복원 동기화는 Open Decision 6(후보 A) 결정으로 Step 1에서 display 설정을 변경해 실제로 관찰한다.

### D6. Placeholder는 얇게, 창 계약은 단단하게

W0의 각 창 내용은 더미 텍스트/간단한 컨트롤로 충분하다. 대신 Window identity, registry, preset application, persistence, lifecycle은 이후 기능이 의존할 수 있게 명확히 만든다.

### D7. Geometry와 preset resolve는 순수 로직으로 분리한다

Window 객체를 직접 만지는 코드와 별개로, `{window_id, visible, position, size, content_mode}` 입력을 받아 검증된 layout 결과를 반환하는 순수 C# 로직을 둔다. 이렇게 하면 기존 `Assets/Script/Tests/*` 패턴처럼 Godot headless에서 Window 표시 없이도 off-screen clamp, unknown id, corrupt config, preset merge 같은 실패/경계 조건을 검증할 수 있다.

## Window Set v0

| Window | 역할 | W0 placeholder |
| --- | --- | --- |
| World | 거점/전투/리플레이 모드의 주인공 창 | 거점 모드 placeholder, PC 1인 표시 또는 텍스트 |
| Situation | 위험 게이지, 용의 가호, 다음 판정 | 3지역 위험 게이지 더미 |
| WeeklyAction | 주간 행동 6계열 | 버튼 6개, 비용/효과 더미 |
| Calendar | 48주 스트립 | 현재 주/이벤트 더미 |
| Log | 시스템/전투/대화 아카이브 | append-only text 더미 |

Root/Main은 managed workspace window 수에 포함하지 않는다. Root/Main은 셸 entry와 메뉴/manager host이며, Step 1의 "Window 5종"은 위 managed windows 5개를 뜻한다.

후속 Window 후보:

- TacticBoard
- BattleInfo
- Report
- UnitDetail
- Save/Load

## Proposed Architecture

### WorkspaceWindowManager

책임:

- Window id별 registry 보관.
- `show_window(id)`, `hide_window(id)`, `toggle_window(id)` 제공.
- `apply_preset(preset_id)`로 창별 visible/position/size/content_mode 적용.
- `gather_windows()`로 대상 screen의 safe work area 안으로 창 회수.
- `user://workspace_layout.cfg` 저장/복원(version 키 포함).
- Root/Main window 수명 주기와 managed window hide/show 동기화.

주의:

- 기존 `WindowManager` autoload와 이름/책임이 겹치지 않게 한다. WS-001 Step 1에서는 기존 autoload를 확장하지 않고, 신규 `WorkspaceWindowManager`를 셸 entry scene 하위 노드로 둔다. 기존 autoload는 현재 전투/실험 경로 보존을 위해 변경하지 않는다. WS-001 완료 후 중복 제거 여부를 별도 cleanup으로 판단한다.
- `Window.ModeEnum`과 World content mode가 충돌하지 않도록 World 쪽은 `content_mode`(`&"hub"`, `&"battle"`, `&"replay"`)라고 부른다.

### WorkspaceWindow

기존 `GameWindow`를 상속하지 않는 신규 공통 Window 기반을 권장한다.

책임:

- stable `WindowId`.
- 닫기 버튼/OS close 요청 시 queue_free가 아니라 hide 정책 우선.
- 위치/크기 변경 이벤트를 manager에 보고.
- 최소 크기와 화면 밖 방지 정책.

기존 `GameWindow`는 `_snapTarget` null 미검사, window id 0 기준 좌표, position 변경 알림 중 재배치 위험이 있어 WS-001 preset/gather의 foundation으로 쓰지 않는다. 스냅 UX는 W0 범위 밖이다. 필요한 경우 WS-001 이후 별도 Step에서 preset/gather와 충돌하지 않게 설계한다.

### Safe Work Area and Gather Contract

`gather_windows()`는 Godot `Window.Position`/`Size`의 client rect만 clamp하지 않는다.

계약:

- 입력 geometry는 decoration 포함 rect 기준으로 resolve한다.
- 가능하면 `Window.GetPositionWithDecorations()`와 decoration-aware size를 사용한다.
- 대상 screen은 decoration 포함 rect의 중심점이 포함된 screen이다. 어떤 screen에도 중심점이 포함되지 않으면 `DisplayServer.SCREEN_PRIMARY`를 사용한다.
- **좌표는 절대 가상 데스크톱 좌표다. screen 원점이 `(0, 0)`이라고 가정하면 안 된다.** Step 1 실측(3 모니터): `screen[0] pos=(0, 542)`, `screen[1] pos=(1920, 774)`, `screen[2] pos=(3840, 0)`. preset 좌표는 하드코딩 절대값이 아니라 대상 screen usable rect 기준 오프셋으로 계산한다.
- **Godot은 창을 화면 안으로 클램프할 때 client rect만 본다. decoration은 클램프에서 빠진다.** Step 1 실측: `position=(40, 80)`(빈 가상 좌표)을 준 창이 `pos=(40, 542)`로 클램프됐지만 `decoPos=(32, 511)`이라 title bar가 화면 밖에 남아 드래그·닫기가 불가능했다. gather는 반드시 decoration 포함 rect로 판정·clamp해야 한다.
- 화면 bounds는 `DisplayServer.ScreenGetUsableRect(screen)` 또는 동등한 work area를 기준으로 한다.
- 최소 교차 임계치: decoration 포함 rect가 work area와 가로 64px 이상, 세로 title-bar 추정 높이 32px 이상 교차해야 한다.
- 회수 규칙: 임계치 미만이면 decoration 포함 rect를 대상 screen usable rect 안으로 clamp한다. 창이 usable rect보다 크면 먼저 usable rect 이하로 축소한다.
- 최소 보장: title bar를 포함한 drag 가능한 영역이 work area 안에 남아야 한다.
- decoration 정보를 얻지 못하는 headless/pure test에서는 conservative top margin을 둔 pure geometry fallback을 사용한다.

### Layout Preset Data

초기에는 코드 상수 또는 간단한 Resource/Dictionary로 시작한다.

예상 shape:

```gdscript
{
  "outgame": {
    "world": { "visible": true, "position": Vector2i(...), "size": Vector2i(...), "content_mode": &"hub" },
    "situation": { "visible": true, "position": Vector2i(...), "size": Vector2i(...) },
    "weekly_action": { "visible": true, "position": Vector2i(...), "size": Vector2i(...) },
    "calendar": { "visible": true, "position": Vector2i(...), "size": Vector2i(...) },
    "log": { "visible": true, "position": Vector2i(...), "size": Vector2i(...) }
  }
}
```

## Open Decisions

1. **Implementation language**
   - 후보 A: C# 중심(`WorkspaceWindowManager.cs`, `WorkspaceWindow.cs`)
   - 후보 B: GDScript 중심
   - 권장: 기존 Window 실험 코드가 C#이고 검증도 C# headless 패턴에 맞추기 쉬우므로 W0 Shell은 C# 중심. 각 창 내부의 placeholder UI는 Godot scene/Control로 구성.

2. **배치 저장 위치**
   - 후보 A: 기존 `Assets/Script/gds/config_file_handler.gd` 활용
   - 후보 B: 새 Workspace 전용 `ConfigFile` wrapper(`user://workspace_layout.cfg`, version 키)
   - 권장: 후보 B. 기존 `config_file_handler.gd`는 autoload가 아니며 `video` 섹션 중심 helper라 workspace layout과 책임이 맞지 않는다.

3. **OS close 동작**
   - 후보 A: 닫기 = hide, manager registry 유지
   - 후보 B: 닫기 = free, 재호출 시 instantiate
   - 권장: W0은 hide 우선. 기능 창의 작업 상태를 잃지 않고, 재호출이 빠르다.

4. **프리셋 기본 배치**
   - 사용자가 나중에 직접 구성할 예정이므로, W0에서는 "동작 확인 가능한 임시 배치"만 둔다.
   - 프리셋 시스템은 배치 값보다 적용/저장/복원 동작이 중요하다.

5. **셸 entry scene / launch path**
   - 후보 A: 신규 workspace shell scene을 만들고 `project.godot` main_scene을 바꾼다.
   - 후보 B: `battle_field.tscn`을 유지하고 테스트/개발 전용 launch scene에서 shell을 검증한다.
   - 후보 C: `battle_field.tscn`에 shell host를 삽입한다.
   - 결정: 후보 B. 신규 `Assets/Scenes/workspace.tscn`을 셸 entry scene으로 만들고 `godot --path . res://Assets/Scenes/workspace.tscn`으로 직접 실행해 검증한다. `run/main_scene`은 `battle_field.tscn`으로 유지하며, main_scene 교체는 별도 승인 대상 후속으로 남긴다. CB-001 결정론 씬 경로를 흔들지 않는다.

6. **Root/Main resizable/minimize 설정**
   - 현재 `project.godot`은 `resizable=false`, `minimize_disabled=true`, `maximize_disabled=true`다.
   - 후보 A: Step 1에서 display 설정을 바꿔 최소화/복원 동기화를 실제로 검증한다.
   - 후보 B: 현재 설정을 유지하고 최소화/복원 완료 조건을 W0 범위에서 제외한다.
   - 결정: 후보 A. Step 1에서 `resizable=true`, `minimize_disabled=false`, `maximize_disabled=false`로 바꾼다. 이 변경은 프로젝트 전역이므로 blast radius가 있다. 따라서 Step 1은 (a) display 설정 변경을 기능 구현과 별도 커밋으로 분리하고, (b) 기존 main_scene `battle_field.tscn`을 실행해 창 크기 변경/최대화 시 전투 화면이 깨지지 않는지 회귀 확인하며(현재 `project.godot`에 stretch mode 설정이 없어 root window 크기 변경이 viewport 표시에 직접 노출된다), (c) 회귀가 발견되면 stretch 설정 도입 여부를 후속 결정으로 올린다.
   - 이 결정으로 메인 창 최소화/복원 시 managed window 동기화가 Step 1의 관찰 가능한 완료 조건이 된다(D5).

7. **상황별 custom preset override 저장**
   - 후보 A: W0 Step 3에서 상황별 custom override까지 저장한다.
   - 후보 B: W0에서는 마지막 창 배치와 기본 preset round-trip만 저장하고, 상황별 custom override는 후속 Task로 둔다.
   - 결정: 후보 B. custom override는 UX 정책과 충돌/우선순위 규칙이 필요하므로 W0 범위를 부풀린다. Step 3은 first-run 기본 preset, save/load round-trip, corrupt/unknown version fallback에 집중한다.

## Step Plan

### Step 0: Design Review

목표:
- 독립 `Window` 기반 Workspace Shell 방향이 현재 코드와 충돌하지 않는지 검토한다.

작업 범위:
- 이 Task 문서 검토.
- `project.godot`, `WindowManager.cs`, `GameWindow.cs`, 기존 Window scene, `config_file_handler.gd`, 현재 main_scene(`battle_field.tscn`) 대조.
- 필요하면 Task 문서만 수정.

제외 범위:
- 제품 코드, `.tscn`, `.tres` 구현 변경.

완료 조건:
- Window lifecycle, close/hide, preset, persistence, gather 정책이 구현 가능한 수준이다.
- Step 1~3의 완료 조건이 관찰 가능하다.
- 구현 전 결정해야 할 Open Decision이 정리되어 있다.

검증 방법:
- 설계 리뷰 문서 작성.
- 판정: Approved / Approved after design fixes / Rework required.

## Step 0 Design Review Result

판정: **Approved after design fixes**.

반영한 design fixes:

- P1: `project.godot` display 설정상 메인 창 최소화/복원은 현재 관찰 불가임을 Context/Open Decisions/Step 1에 반영했다.
- P1: 현재 main scene이 `main.tscn`이 아니라 `Assets/Scenes/Map/battle_field.tscn`임을 반영하고, WS-001 구현자가 임의로 main_scene을 바꾸거나 전투 씬에 셸을 섞지 않도록 entry scene 결정을 Open Decision으로 추가했다.
- P1: `gather_windows()`의 safe area 계약을 decoration 포함 rect(`GetPositionWithDecorations()` 우선) 기준으로 명시했다.
- P2: `field_window.tscn`/`inspector.tscn`/`config_file_handler.gd`/`GameWindow.cs`의 현재 한계를 Context와 Architecture에 반영했다.
- P2: 기존 `WindowManager` autoload와 신규 manager 책임 중복, World `content_mode`와 `Window.ModeEnum` 용어 충돌을 정리했다.
- P2: Root/Main은 managed window set에서 제외하고, Step 1의 Window 5종은 `World`/`Situation`/`WeeklyAction`/`Calendar`/`Log`임을 명확히 했다.
- P2: geometry/preset resolve 순수 로직 분리 원칙(D7)을 추가했다.

추가 반영:

- custom preset override는 W0 Out of Scope/Open Decision 7로 분리했다.
- target screen/교차 임계치/gather clamp 규칙을 명확히 했다.
- first-run/unknown version fallback을 Step 3 완료 조건에 추가했다.
- 기존 `WindowManager` autoload 보존 정책을 명시했다.
- snap UX를 W0 범위 밖으로 분리했다.
- registry stale/duplicate 조건을 Step 1 완료 조건에 추가했다.
- workspace managed Window는 기존 `WindowManager` autoload의 `sub_windows` 그룹에 등록하지 않는다는 충돌 가드를 추가했다.
- OD7은 custom preset override를 W0 범위 밖으로 두는 결정으로 확정했다.

Step 1 착수 게이트 결정(2026-07-08, 사용자 승인):

- OD5 = 후보 B. 셸 entry scene은 신규 `Assets/Scenes/workspace.tscn`이고 직접 실행으로 검증한다. `run/main_scene`은 `battle_field.tscn`으로 유지한다.
- OD6 = 후보 A. Step 1에서 `project.godot` display 설정을 `resizable=true`, `minimize_disabled=false`, `maximize_disabled=false`로 변경한다. 전역 blast radius가 있으므로 별도 커밋 + `battle_field.tscn` 회귀 확인을 Step 1 완료 조건에 포함한다.
- 이 결정으로 D5의 메인 창 최소화/복원 동기화가 Step 1의 관찰 가능한 완료 조건이 되었다.

### Step 1: Native Window Skeleton

목표:
- 실제 Godot 실행에서 W0 v0 창 세트가 독립 Window로 뜬다.

작업 범위:
- Workspace window manager/registry 최소 구현.
- 신규 `WorkspaceWindowManager`는 셸 entry scene 하위 노드로 두고 기존 `WindowManager` autoload는 변경하지 않는다.
- workspace managed Window는 기존 `WindowManager` autoload와 충돌하지 않도록 `sub_windows` 그룹에 등록하지 않는다. 최소화 동기화는 신규 `WorkspaceWindowManager`가 자기 registry를 기준으로 수행한다.
- Root/Main 메뉴 또는 버튼에서 `World`, `Situation`, `WeeklyAction`, `Calendar`, `Log` 창 열기/숨기기.
- 각 창은 placeholder Control만 포함한다.
- 닫기 요청은 hide로 처리한다.
- 셸 entry scene은 신규 `Assets/Scenes/workspace.tscn`이다(OD5 후보 B). `run/main_scene`은 `battle_field.tscn`으로 유지한다.
- `project.godot` display 설정을 `resizable=true`, `minimize_disabled=false`, `maximize_disabled=false`로 변경한다(OD6 후보 A). 이 변경은 기능 구현과 **별도 커밋**으로 분리한다.

제외 범위:
- 배치 저장/복원.
- 아웃게임 실제 상태/행동 로직.
- 택틱 보드/리포트 창.

완료 조건:
- 앱 실행 후 managed Window 5종(`World`, `Situation`, `WeeklyAction`, `Calendar`, `Log`)을 독립적으로 열 수 있다.
- 각 Window는 독립적으로 이동/크기 변경 가능하다.
- OS close 또는 창 닫기 시 앱이 종료되거나 registry가 깨지지 않고, 메뉴에서 다시 표시된다.
- `show_window(id)`를 이미 보이는 창에 호출해도 창이 중복 생성되거나 위치가 프리셋으로 리셋되지 않는다.
- repeated registration은 같은 id의 중복 entry를 만들지 않고 fail-closed/report한다.
- registry가 freed/stale window를 만나면 `IsInstanceValid` 확인 후 stale entry를 제거하거나 재생성 가능한 상태로 복구한다.
- 메인 창을 최소화하면 managed window가 함께 최소화되고, 복원하면 최소화 직전 상태(숨겨진 창은 숨긴 채로)로 돌아온다.
- display 설정 변경 후 기존 main_scene `battle_field.tscn`이 창 크기 변경/최대화에서 실행 가능하며, 회귀가 있으면 그 사실을 보고한다(수정은 후속).

검증 방법:
- `godot --path . res://Assets/Scenes/workspace.tscn` 실행 smoke(창 5종 open/hide/show/move/resize, OS close, 메인 창 최소화/복원).
- `godot --path . res://Assets/Scenes/Map/battle_field.tscn` 실행 smoke(display 설정 변경 회귀).
- headless에서 manager registry/close-hide/duplicate/stale 정책 단위 테스트.
- `dotnet build` 경고/오류 0.
- Godot `--import` exit 0.

## Step 1 구현 결과

변경 파일:

- `Assets/Script/UI/Window/WorkspaceWindow.cs`(신규): managed window 공통 기반. `WindowId` export, `close_requested` → `Hide()`.
- `Assets/Script/UI/Window/WorkspaceWindowManager.cs`(신규): registry, `ShowWindow`/`HideWindow`/`ToggleWindow`/`TryGetWindow`/`PruneStaleEntries`, 최소화 동기화.
- `Assets/Script/UI/Window/WorkspaceShell.cs`(신규): registry 기준 toggle 버튼 생성 + 창 `VisibilityChanged` → 버튼 상태 역동기화.
- `Assets/Scenes/workspace.tscn`(신규): 셸 entry scene. `WorkspaceWindowManager` + 5종 `WorkspaceWindow`(placeholder Label) + 메뉴. 창은 `initial_position = 2`(CenterMainWindowScreen)를 쓴다. 실제 배치는 Step 2 preset 몫이다.
- `Assets/Script/Tests/Ws001WindowGeometryProbe.cs`, `ws001_window_geometry_probe.tscn`(신규, 진단 전용): 실제 GUI 실행에서 창의 client rect / decoration 포함 rect / screen usable rect를 텍스트로 출력한다. 헤드리스로는 decoration 크기를 알 수 없어 Step 2 gather 계약 검증에 필요하다.
- `project.godot`: display 설정에서 `resizable=false`/`minimize_disabled=true`/`maximize_disabled=true` 제거(엔진 기본값 = 활성).
- `Assets/Script/Tests/Ws001Step1RegistryTest.cs`, `ws001_step1_registry_test.tscn`(신규): 헤드리스 검증 11 케이스 / 62 assertions.

구현 내용:

- 닫기 = hide 정책. registry는 유지되고 메뉴에서 재표시된다. OS close로 창을 닫아도 toggle 버튼이 함께 풀려 한 번 눌러 다시 연다.
- `ShowWindow`는 이미 보이는 창의 position/size를 건드리지 않는다(duplicate open).
- 빈 id/중복 id는 `GD.PushError` 후 fail-closed. 같은 인스턴스 재등록은 중복 entry를 만들지 않는다.
- freed/queued-for-deletion window는 lookup/prune 시 registry에서 제거되고, 같은 id로 새 인스턴스를 다시 등록할 수 있다.
- 최소화 동기화는 신규 manager가 자기 registry만 보고 수행한다(managed window는 `sub_windows` 그룹 미등록, 기존 `WindowManager` autoload 무변경). 최소화 직전에 **보이던** 창만 기억했다가 복원하므로, 사용자가 닫아둔 창은 복원에서 다시 뜨지 않는다.
- 최소화 동기화는 `Window.ModeEnum`이 아니라 visible 토글로 구현했다. preset(Step 2)이 `visible`을 권위로 다루므로 `Mode`/`previous_mode` meta 경합을 만들지 않는다.
- headless DisplayServer는 root window를 `Minimized`로 보고하므로(기존 autoload도 같은 로그 출력) manager는 headless에서 최소화 폴링을 끈다.

검증:

- `dotnet build AutoCrawler.sln -c Debug`: 경고 0, 오류 0.
- `--headless --import`: exit 0.
- `ws001_step1_registry_test`: ALL PASS(62 assertions). A 자동 등록 / B 빈 id / C 중복 id / D 동일 인스턴스 재등록 / E unknown id fail-closed / F show·hide·toggle / G duplicate open position 보존 / H close=hide + 재표시 / I freed prune + 재등록 / J 최소화·복원(숨긴 창 유지, 재진입) / K 실제 `workspace.tscn` 배선.
- `--quit-after` 헤드리스 부팅 smoke: `workspace.tscn` exit 0, `battle_field.tscn` exit 0(display 설정 변경 후 스크립트 에러 없음).
- CB-001 회귀: step1/2/3 ALL PASS. **step4는 실패한다(`D.deaths_recorded`)**. `project.godot` 변경을 stash한 baseline에서도 동일하게 실패하므로 WS-001 이전부터 존재하던 회귀다(별도 Task).

수동 GUI smoke에서 발견·수정한 버그:

- 최초 구현은 `workspace.tscn`에 절대좌표(`position = Vector2i(40, 80)` 등)를 하드코딩했다. 사용자 데스크톱(모니터 3개, `screen[0] pos=(0, 542)`)에서 이 좌표는 어느 screen에도 속하지 않는 빈 영역이라, Godot이 **client rect만** 화면 안으로 클램프하고 decoration은 남겨 두었다. 결과적으로 5창 중 4창의 title bar가 화면 밖에 있어 이동·닫기가 불가능했다(`world`: `pos=(40,542)`인데 `decoPos=(32,511)`). 유일하게 `y=600`이던 `calendar`만 정상이었고, `situation`은 `(700,80)` → `(1920,774)`로 다른 모니터에 튕겼다.
- 수정: 창 좌표 하드코딩을 제거하고 `initial_position = 2`(CenterMainWindowScreen)로 바꿨다. Step 2 gather를 앞당겨 끌어오지 않으면서 title bar가 항상 usable rect 안에 들어온다. 재확인(probe): 5창 모두 main window의 screen(usable `(1920,774)~(3840,1806)`) 안, 최상단 `log`의 `decoPos.y = 1003`.
- 이 실측 두 가지(screen 원점 ≠ (0,0), 클램프가 decoration 무시)를 `Safe Work Area and Gather Contract`에 근거로 남겼다.

display 설정 변경 회귀(수동 확인 결과):

- `battle_field.tscn`에서 창을 키우거나 최대화하면 전투 화면이 따라 커지지 않고 원래 크기 그대로 남고 **빈 공간만 생긴다**. 예상된 결과다. 프로젝트에 `display/window/stretch/*` 설정이 없어 root window 크기 변경이 viewport 표시에 직접 노출된다.
- 판정: 크래시·스크립트 에러 없음. OD6의 "회귀가 발견되면 stretch 설정 도입 여부를 후속 결정으로 올린다" 조항에 따라 **후속 결정**으로 올린다(Step 1은 보고까지가 완료 조건). 자세한 내용은 [[Open-Tasks]].

남은 위험:

- 메인 창 최소화/복원 동기화의 **실제 OS 동작**(멀티모니터, 작업표시줄)은 수동 smoke 대상이다. 헤드리스 테스트는 manager API 수준만 단언한다.
- 5창이 main window의 screen 중앙에 겹쳐 뜬다. Step 2 preset이 실제 배치를 정할 때까지는 사용자가 직접 옮겨야 한다.
- `WorkspaceWindow`의 "위치/크기 변경 이벤트를 manager에 보고"는 Step 2/3에서 필요해질 때 추가한다(Step 1 완료 조건 아님).
- `project.godot` display 변경은 기능 커밋과 분리해서 커밋해야 한다(아직 커밋하지 않음).

### Step 2: Preset Apply and Gather Windows

목표:
- 프리셋 버튼이 독립 Window들의 visible/position/size/content_mode를 적용한다.

작업 범위:
- `outgame`, `battle`, `analysis` preset id 추가.
- W0에서는 임시 좌표만 사용한다.
- `gather_windows()` 구현: 대상 screen 선택 규칙과 decoration 포함 rect 기준으로 모든 managed window를 work area 안으로 회수.
- World window `content_mode` field(`hub`/`battle`/`replay`) placeholder 반영.

제외 범위:
- 사용자가 조정한 상황별 custom preset override 저장.
- 실제 전투 씬 장착.

완료 조건:
- 프리셋 전환 시 필요한 창은 보이고 불필요한 창은 숨겨진다.
- World window는 새로 생성되지 않고 `content_mode`/position/size만 바뀐다.
- 화면 밖 좌표를 강제로 넣어도 `gather_windows()` 후 다시 보이는 영역으로 돌아온다.
- title bar를 포함한 drag 가능한 영역이 화면 밖에 남지 않는다.
- 다중 모니터/화면 밖 좌표 판정은 decoration rect 중심이 포함된 screen을 우선하고, 없으면 primary screen으로 fallback한다.

검증 방법:
- Godot 실행 smoke.
- preset/gather pure geometry 단위 테스트.
- `dotnet build`.
- Godot `--import` exit 0.

## Step 2 구현 결과

변경 파일:

- `Assets/Script/UI/Window/WorkspaceGeometry.cs`(신규): D7 순수 geometry. `ResolveTargetScreen`, `IsReachable`, `ClampIntoWorkArea`, `Gather`. 입력·출력이 전부 decoration 포함 `Rect2I`다.
- `Assets/Script/UI/Window/WorkspaceLayoutPreset.cs`(신규): `outgame`/`battle`/`analysis` preset 테이블, `WindowLayout`(visible/offset/size/content_mode), `WorldContentMode`(hub/battle/replay). unknown preset은 `TryGetPreset`에서 fail-closed.
- `Assets/Script/UI/Window/WorkspaceWindowManager.cs`: `ApplyPreset`, `GatherWindows`, `GetWorkArea`, `GetDecorationRect` 추가.
- `Assets/Script/UI/Window/WorkspaceWindow.cs`: `ContentMode` 속성(+placeholder Label 갱신).
- `Assets/Script/UI/Window/WorkspaceShell.cs`, `Assets/Scenes/workspace.tscn`: preset 버튼 3종 + `창 모아오기` 버튼, World 창의 content_mode Label 배선.
- `Assets/Script/Tests/Ws001Step2GeometryTest.cs`, `ws001_step2_geometry_test.tscn`(신규): 12 케이스 / 66 assertions.

주요 설계 판단:

- **preset offset은 대상 screen work area 원점 기준**이다. 절대 좌표가 아니다(Step 1 실측 근거).
- **decoration 포함 rect가 유일한 좌표계다.** `GetDecorationRect()`가 client rect ↔ decoration rect 변환을 한 곳에서 담당하고, 판정·clamp는 전부 순수 로직이 한다.
- `IsReachable`은 "타이틀바 띠(32px)가 세로로 온전히 work area 안에 있고, 가로로 최소 64px 겹친다"로 정의했다. 창 아래쪽만 걸친 상태는 잡을 수 없으므로 false다.
- `ApplyDecorationRect`는 `InitialPosition`을 `Absolute`로 바꾼다. 그러지 않으면 다음 `Show()`가 Godot의 `initial_position`(Step 1에서 넣은 CenterMainWindowScreen) 규칙으로 좌표를 덮어쓴다. 구현 중 실제로 밟은 버그다.
- preset은 창을 **먼저 Show한 뒤** 좌표를 적용한다. native window가 있어야 실제 decoration 크기를 읽을 수 있고, 순서를 뒤집으면 `Show()`가 좌표를 덮어쓴다.
- **headless DisplayServer는 screen이 0개**다(실측 `screen_count = 0`, root 64x64). usable rect를 그대로 쓰면 preset이 음수 좌표로 무너지므로, work area를 프로젝트 viewport 크기(1440x960)로 fallback한다(`SanitizeWorkArea`).

검증:

- `dotnet build`: 경고 0, 오류 0. `--import`: exit 0.
- `ws001_step2_geometry_test`: ALL PASS(66 assertions). A 대상 screen 중심 판정 / B primary fallback·screen 없음 / C 타이틀바 위·아래·좌우 잘림, 최소 grab 폭 경계 / D clamp 좌상단·우하단 / E tiny screen 축소·낮은 창 / F gather 멱등·도달 가능 창 무변경 / G **Step 1 타이틀바 버그 회귀** / H preset show·hide / I World 창 재생성 없음(InstanceId 동일, content_mode hub→battle→replay) / J unknown preset·null fail-closed(상태 불변) + preset이 미등록 창 참조 시 나머지 적용 / K preset offset이 work area 상대 / L 강제 off-screen 회수 + 2회차 gather 무변경.
- 실제 GUI probe(멀티모니터 3대, work area `(1920,774,1920,1032)`): outgame preset이 5창을 원점+오프셋에 배치하고 전부 `reachable=true`. `world`를 `(-5000,-5000)`, `log`를 `(60000,60000)`로 강제한 뒤 `GatherWindows()`가 **정확히 2창만** 회수했다(`world` → `(1920,774)`, `log` → `(3464,1207)` = 우하단 clamp). 나머지 3창 좌표 불변.
- 회귀: `ws001_step1_registry_test`, CB-001 step1~3, ST-001 step1 ALL PASS. workspace 부팅 exit 0.
- **기존 회귀 2건**(WS-001과 무관, `project.godot` 되돌린 baseline에서도 동일 재현): `cb001_step4_determinism_test`(`D.deaths_recorded`), `sk001_step6_data_pack_test`(`D.ChainHitsThree` got 1 expected 3).

남은 위험:

- 실제 다중 모니터에서 **모니터 구성이 바뀐 뒤**(해제/추가) gather 동작은 확인하지 못했다. Step 3의 stale monitor coordinates와 함께 다룬다.
- 숨겨진 창의 decoration 크기는 native window가 없어 fallback inset을 쓴다. 표시 시점에 실제 값으로 다시 계산되므로 preset 직후 좌표는 정확하지만, 숨긴 채 저장하면 오차가 남을 수 있다(Step 3에서 확인).
- preset 좌표는 임시값이다(OD4). 실제 배치는 사용자 조정 후 Step 3 저장으로 확정된다.

### Step 3: Layout Persistence

목표:
- 창 배치가 설정 파일에 저장되고 다음 실행에서 복원된다.

작업 범위:
- 창별 position/size/visible/content_mode 저장.
- `user://workspace_layout.cfg` first-run 생성 또는 기본 preset fallback.
- 저장 데이터가 손상되거나 화면 해상도/monitor 구성이 바뀌었을 때 안전 fallback.
- unknown future version 또는 unsupported version은 기본 배치로 fallback하고 기존 파일을 즉시 파괴하지 않는다.

제외 범위:
- SaveGame 슬롯 저장과의 통합. 이것은 유저 설정(config)이다.
- 상황별 custom preset override 저장.
- 클라우드/프로필별 설정.

완료 조건:
- 설정 파일이 없는 첫 실행은 기본 preset으로 시작하고 config 생성/저장 경로가 준비된다.
- 창을 옮기고 종료/재시작하면 배치가 복원된다.
- 설정 파일에 잘못된 값이 있어도 실행이 깨지지 않고 기본 배치 또는 gather fallback으로 복구된다.
- unknown future version 또는 unsupported version은 기본 배치로 fallback한다.
- 다중 모니터에서 저장된 좌표가 대상 screen usable rect 밖이면 gather 계약에 따라 회수된다.

검증 방법:
- 수동 실행 smoke.
- config round-trip/first-run/corrupt/unknown-version 테스트.
- `dotnet build`.
- Godot `--import` exit 0.

## Step 3 구현 결과

변경 파일:

- `Assets/Script/UI/Window/WorkspaceLayoutStore.cs`(신규): D7 순수 저장 로직. `ConfigFile`을 읽고 쓰지만 Window/DisplayServer는 안 만진다. `Save`/`Load`, `WindowSnapshot`, `LayoutLoadStatus`(Loaded/FirstRun/Corrupt/UnsupportedVersion), version 정책·손상 처리·타입 검증.
- `Assets/Script/UI/Window/WorkspaceWindowManager.cs`: `SaveLayout`/`LoadLayout`/`UseLayoutStorePath` 추가. registry ↔ 스냅샷 변환 + gather fallback.
- `Assets/Script/UI/Window/WorkspaceShell.cs`, `Assets/Scenes/workspace.tscn`: `배치 저장`/`배치 복원` 버튼, `_restoreLayoutOnReady` export(entry scene에서만 true). 자동 복원 + 종료 시 자동 저장.
- `Assets/Script/Tests/Ws001Step3PersistenceTest.cs`, `ws001_step3_persistence_test.tscn`(신규): 9 케이스 / 40 assertions.
- Step 1/2 테스트 fixture: workspace.tscn 인스턴스화 시 `_restoreLayoutOnReady`를 트리 진입 전에 false로 설정(자동 복원이 raw registry 관찰을 방해하지 않도록).

주요 설계 판단:

- **저장 파일은 `user://workspace_layout.cfg`**(OD2 후보 B). `user://settings.ini`(전역 video 설정)와 분리해, 손상 시 이 파일만 폐기해도 다른 설정을 잃지 않는다.
- `[meta] version` 키를 항상 기록한다. **누락·미지 version은 UnsupportedVersion으로 fallback하되 파일을 파괴하지 않는다**(OD의 "미지 버전 = 기본 배치" 규칙).
- **타입 검증은 창 단위 fail-closed다.** 한 창의 값이 깨져도(타입 오류, 키 누락, size ≤ 0) 그 창만 건너뛰고 나머지는 복원한다.
- **자동 복원은 entry scene에서만** 켠다(`_restoreLayoutOnReady`). 켜지면 시작 시 `LoadLayout`(첫 실행이면 기본 preset), 종료 시 `AutoAcceptQuit=false` + root `CloseRequested`로 `SaveLayout` 후 quit.
- **복원 후 항상 gather를 돌린다.** 저장 당시와 모니터 구성/해상도가 달라 좌표가 화면 밖이면 decoration 포함 rect 기준으로 회수한다.
- `LoadLayout`은 창을 먼저 Show/Hide한 뒤 좌표를 적용하고 `InitialPosition = Absolute`로 둔다(Step 2와 같은 이유: Show가 좌표를 덮어쓰지 않게).

검증:

- `dotnet build`: 경고 0, 오류 0. `--import`: exit 0.
- `ws001_step3_persistence_test`: ALL PASS(40 assertions). A store round-trip / B first-run / C 파싱 불가 안전 fallback(파일 보존) / D 미지·누락 version fallback(파일 보존) / E 타입 깨진 창만 skip·나머지 보존 / F **manager save → 새 인스턴스 load 복원**(재시작 시뮬, position/size/visible 일치, 숨긴 창 유지) / G 첫 실행 기본 preset 5창 표시 / H 저장 좌표가 화면 밖이면 load 후 gather 회수 / I size ≤ 0 skip.
- 실제 GUI probe(멀티모니터): `SaveLayout=Ok`로 world 좌표 `(1928,805)` 저장 → `(100,100)`로 흩뜨림 → `LoadLayout` 후 `(1928,805)` 정확 복원, 저장 당시 visible이던 `situation`은 숨긴 뒤에도 다시 표시, 5창 모두 `reachable=true`.
- C# 발견: **C# `ConfigFile.Load`는 GDScript와 달리 잘못된 토큰(`version = @@@`)에서도 대부분 관대하게 처리**하지만, 닫히지 않은 표현식이 섞이면 ParseError(Corrupt 경로)를 낸다. 두 경로 모두 기본 배치 fallback이라 완료 조건은 충족된다.
- 회귀: `ws001_step1`(62)·`ws001_step2`(66), CB-001 step1~3, ST-001 step1 ALL PASS. workspace 부팅 exit 0.

남은 위험:

- 종료 시 자동 저장(root `CloseRequested` → `SaveLayout`)의 **실제 OS 창 닫기 동작**은 수동 smoke 대상이다. 헤드리스/probe는 `SaveLayout`/`LoadLayout` 직접 호출로만 검증했다.
- 숨긴 창을 저장하면 decoration 크기를 fallback inset으로 추정한 좌표가 저장된다. 표시 시점에 gather가 재보정하므로 도달 불가 상태는 안 되지만, 몇 px 오차는 남을 수 있다.
- 실제 다중 모니터 **구성 변경 후 재시작**(저장한 모니터 해제)은 probe로 강제 off-screen만 확인했다. 실물 모니터 해제는 미확인.

### Step 4: Documentation and Completion Review

목표:
- W0 Shell의 현재 사실과 후속 작업을 Wiki에 정리한다.

작업 범위:
- `20_Systems`에 Workspace/Window system 문서 추가 또는 관련 문서 갱신.
- `Current-State`, `Open-Tasks`, 이 Task 문서 갱신.
- Review 문서 작성.

제외 범위:
- 새 기능 구현.

완료 조건:
- WS-001 완료 조건과 검증 결과가 리뷰에 대조되어 있다.
- DemoState/BattleSession/TacticBoard 등 후속 창 장착 작업이 Open-Tasks에 남아 있다.

검증 방법:
- 문서 링크/상태 정적 검토.

## Step 4 구현 결과

제품 코드 변경 없음(문서 단계). 갱신한 문서:

- `20_Systems/Workspace-Window-System.md`(신규): W0의 현재 사실을 System 문서로 보존([[Workspace-Window-System]]).
- `00_Index/Current-State.md`: Workspace 절 추가.
- `00_Index/Open-Tasks.md`: WS-001을 W0 완료로 갱신, 후속 창 장착 작업 분리.
- `00_Index/Home.md`: 새 System/Review 링크 추가.
- 이 Task 문서: `status: complete`, Step 1~3 구현 결과 기록.
- `50_Reviews/WS-001-Native-Window-Workspace-Shell-Review.md`: Step 4 문서화 + 전체 완료 조건 대조 + 최종 판정.

전체 완료 조건 대조와 최종 판정은 [[WS-001-Native-Window-Workspace-Shell-Review]]에 있다.

## WS-001 완료 요약

- W0(Step 0~3) 완료. 설계 리뷰 → Step 1 skeleton → Step 2 preset/gather → Step 3 persistence → Step 4 문서.
- 헤드리스 3종 168 assertions(62+66+40) ALL PASS, `dotnet build` 경고/오류 0, `--import` exit 0, 멀티모니터 GUI probe 확인.
- 남은 것: 수동 GUI smoke 3건(OS close 저장·모니터 해제·최소화 작업표시줄)과 W0 범위 밖 후속(콘텐츠 장착·custom override·스냅·stretch). 모두 [[Open-Tasks]].
- `project.godot` display 설정 변경은 기능 커밋과 분리해서 커밋한다(설계 결정 OD6).

## Verification Matrix

| 영역 | 정상 | 실패/경계 |
| --- | --- | --- |
| Window lifecycle | open/hide/show/move/resize | OS close, duplicate open without position reset, main minimize/restore(숨긴 창 유지) |
| Registry | stable id lookup | missing scene, freed/stale window, repeated registration |
| Preset | visible/position/size/content_mode 적용 | unknown preset, unknown window id |
| Gather | 대상 screen + decoration 포함 rect를 work area로 회수 | off-screen, title bar off-screen, tiny screen, monitor 변경, no screen contains center |
| Persistence | config save/load round-trip | first-run missing file, corrupt config, invalid type, unknown version, stale monitor coordinates |
| Pure geometry | Window 없이 preset/geometry resolve | invalid rect, unknown mode, corrupt config merge |
| Scope boundary | placeholder content only | gameplay state/SaveGame/BT compile 미연결 |

## Related

- [[Open-Tasks]]
- [[Current-State]]
- [[STEP_REVIEW_WORKFLOW]]
- [[BT-001-BehaviorTree-Graph-Editor-Debugger]]
- [[SG-003-SaveSlot-UI-Host-Integration]]

