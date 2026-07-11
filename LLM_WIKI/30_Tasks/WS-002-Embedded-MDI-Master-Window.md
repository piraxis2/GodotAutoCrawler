---
id: WS-002
type: task
status: complete
system: Workspace
created: 2026-07-10
tags: [task, workspace, ui, window, mdi, outgame]
---

# Embedded MDI Master Window

## Goal

WS-001이 세운 native `Window` 기반 Workspace Shell을 **임베디드 MDI 마스터 윈도우**로 전환할 수 있는지 실물로 검증하고, 통과하면 개정판 E-2의 "창 다이어트"를 코드 구조에 반영한다.

플레이어는 OS 창 여러 개를 관리하는 것이 아니라, 게임의 단일 OS 창인 **마스터 윈도우** 안에서 세계/택틱 보드/리포트 창을 배치하고, 하단 로그 도크를 항상 보며, 국면별 프리셋으로 질서 있게 돌아올 수 있어야 한다. 아웃게임은 창 4개짜리 대시보드가 아니라 세계 창 거점 모드의 고정 패널로 단일 화면화한다.

핵심 방향:

- Step 1은 반드시 임베디드 전환 스파이크다. `gui_embed_subwindows`를 켜고 기존 `workspace.tscn`의 창들을 마스터 내부 창처럼 띄운다.
- 세계 창 크기 트윈(거점 98×79 ↔ 전장 64×76)이 부드럽지 않으면 구현을 중단하고 보고한다. 이 경우 "네이티브 유지 + 출정 연출 재설계"는 별도 결정으로 분리한다.
- 스파이크가 통과하면 Root/Main을 마스터 윈도우로 승격한다. WS-001의 메뉴바/프리셋/창 모아오기/저장 기반은 재사용하고, 하단 로그 도크와 레이아웃 슬롯 스냅을 추가한다.
- WS-001의 managed 창 5종 중 `Situation`/`WeeklyAction`/`Calendar`는 독립 창 registry에서 제거하고, `World` 거점 모드 내부 고정 패널로 이관한다.

## User Outcome

- 사용자는 아웃게임에서 세계 창 거점 모드 하나와 하단 로그 도크만 본다. 캘린더, 위험 게이지/가호/D-day, 행동 카드는 세계 창 안의 고정 패널이므로 주간 루프 중 창 관리가 필요 없다.
- 전투 국면에서는 세계 창이 같은 인스턴스에서 전장 모드 크기로 트윈되고, 택틱 보드가 보조 창으로 등장하며, 로그 도크는 하단에 유지된다.
- 분석 국면에서는 리포트, 택틱 보드, 세계 리플레이, 로그 도크가 4창 예산 안에서 병치된다.
- 창을 자유롭게 옮겨도 현재 국면의 프리셋 슬롯 근처로 끌면 하이라이트가 뜨고, 놓으면 슬롯 크기로 정렬된다.
- 창은 마스터 밖으로 사라지지 않는다. 멀티모니터 decoration, OS title bar, 화면 밖 분실 문제는 임베디드 전환으로 구조적으로 제거된다.

## Context

- 기획서 `06_UXUI.md` E-2는 2026-07-10 "창 다이어트"로 개정됐다. 독립 창은 `세계`/`택틱 보드`/`리포트`/`로그` 4종이고, 동시 표시 상한은 4창이다. `Situation`/`WeeklyAction`/`Calendar` 성격의 정보는 세계 창 거점 모드의 고정 패널로 흡수된다.
- E-2는 마스터 윈도우를 "모든 게임 창의 부모가 되는 단일 컨테이너"로 정의한다. 마스터의 책임은 스냅 호스트, 메뉴바, 하단 로그 도크, 경계 보장, 레이아웃 저장/복원의 주체다.
- E-2 §5는 기본 임베디드 MDI를 사실상 확정으로 둔다. 핵심 근거는 E-8 출정/귀환 연출에서 세계 창 자체가 98×79 거점 크기에서 64×76 전장 크기로 자연스럽게 변해야 한다는 점이다. 네이티브 OS 창 리사이즈 애니메이션은 이 요구와 맞지 않을 가능성이 높다.
- E-7 개정판 레이아웃은 1920×1080 기준으로 아웃게임 `세계 98×79 + 로그 98×19`, 전투 `세계 64×76 + 보드 33×76 + 로그 98×18`, 분석 `리포트 34×76 + 보드 36×76 + 세계 26×76 + 로그 98×18`을 요구한다.
- E-8은 세계 창 거점 모드를 실제 게임 씬으로 정의한다. 다만 데모 스코프에서는 PC 1인이 거점에 서 있는 것까지만 후속에서 실물화한다. WS-002는 그 콘텐츠 실물화가 아니라 창 구조 전환이 목표다.
- `08_데모_스코프.md` G-6은 W0의 첫 구현 단위를 마스터 윈도우 + 창 표준 + E-7 프리셋 + 자동 전환 훅으로 둔다. 골드플레이팅 금지 항목은 레트로 스킨 아트, 창 애니메이션, pop-out 옵션이다.
- WS-001은 W0의 1차 검증을 native OS `Window` 방식으로 완료했다. 셸 entry scene은 `Assets/Scenes/workspace.tscn`이며, `run/main_scene`은 여전히 `Assets/Scenes/Map/battle_field.tscn`이다. main_scene 변경은 별도 승인 대상이다.
- WS-001 산출물:
  - `Assets/Script/UI/Window/WorkspaceWindow.cs`
  - `Assets/Script/UI/Window/WorkspaceWindowManager.cs`
  - `Assets/Script/UI/Window/WorkspaceGeometry.cs`
  - `Assets/Script/UI/Window/WorkspaceLayoutPreset.cs`
  - `Assets/Script/UI/Window/WorkspaceLayoutStore.cs`
  - `Assets/Script/UI/Window/WorkspaceShell.cs`
  - `Assets/Scenes/workspace.tscn`
- WS-001 검증은 헤드리스 테스트 3종 168 assertions ALL PASS다: registry 62, geometry 66, persistence 40. `dotnet build` 경고/오류 0, Godot `--import` exit 0, GUI probe로 native 멀티모니터 preset/gather/save-load round-trip을 확인했다.
- `WorkspaceGeometry`, `WorkspaceLayoutPreset`, `WorkspaceLayoutStore`는 창 방식과 무관한 순수 로직이다. 임베디드 전환 시 재사용하고, manager 좌표계만 가상 데스크톱 절대좌표에서 마스터 내부 좌표로 단순화한다.
- 구 실험 코드 `Assets/Script/WindowManager.cs`(autoload)와 `Assets/Script/UI/Window/GameWindow.cs`는 WS-001이 의도적으로 우회한 레거시다. 마스터 윈도우 정착 시 정리 여부를 Open Decision으로 다룬다.
- 로그 창 콘텐츠는 GL-001 `LogWindowController`가 진행해 현재 `workspace.tscn`의 Log 창 placeholder를 read-only 로그 UI로 교체한 상태다. 하단 로그 도크 전환 시 이 컨트롤러를 언제 통합할지 결정해야 한다.
- 알려진 별도 회귀: `cb001_step4_determinism_test`의 `D.deaths_recorded`, `sk001_step6_data_pack_test`의 `D.ChainHitsThree`는 WS-001 이전부터 존재하거나 로스터 rebaseline이 필요한 별도 버그다. WS-002 범위가 아니다.

## Scope

- 임베디드 MDI 전환 스파이크:
  - 루트 뷰포트 `gui_embed_subwindows` 활성화.
  - 기존 `workspace.tscn`의 managed window를 임베디드 상태로 실행.
  - 세계 창 크기 트윈의 시각 품질, WS-001 헤드리스 회귀, decoration/멀티모니터 복잡도 제거 여부 판정.
- 마스터 윈도우 승격:
  - Root/Main을 마스터로 정의하고, 창 좌표계를 마스터 내부 좌표로 정리.
  - WS-001의 메뉴바, 프리셋 버튼, 창 모아오기, 배치 저장/복원 기반 재사용.
  - 로그를 떠다니는 managed window에서 마스터 하단 도크 영역으로 전환.
  - 레이아웃 슬롯 스냅 추가: 현재 국면 프리셋 타일 근처에서 하이라이트, drop 시 슬롯 rect로 정렬.
  - 슬롯 스냅 판정은 `WorkspaceGeometry` 순수 함수로 추가해 헤드리스 테스트 가능하게 유지.
- 창 다이어트 적용:
  - registry의 managed window set을 개정판 4종 구조에 맞춘다.
  - `Situation`/`WeeklyAction`/`Calendar` 독립 창을 제거하고, 세계 창 거점 모드의 placeholder 고정 패널로 이관한다.
  - E-7 개정판 프리셋 테이블로 갱신한다.
  - 로그는 managed floating window가 아니라 마스터 하단 도크로 취급하되, E-2의 독립 창 목록에는 "상주 로그 창/도크"로 남는 개념 차이를 문서와 코드 계약에서 명확히 한다.
- 기존 WS-001 테스트를 임베디드/마스터 구조에 맞춰 rebaseline한다.
- 완료 후 Workspace system 문서, Current-State/Open-Tasks, Review를 갱신한다.

## Out of Scope

- 세계 창 콘텐츠 실물화: 거점 씬 PC 1인 배치, 전장 씬 장착, 출정/귀환 연출 구현.
- 택틱 보드 창 콘텐츠, 리포트 창 콘텐츠.
- 실제 DemoState, BattleSession, 루프 조립, save/load UI.
- 레트로 OS 스킨, 창 애니메이션 아트, pop-out 옵션. G-6 W0 골드플레이팅 금지.
- 네이티브 Window와 임베디드 MDI를 동시에 장기 지원하는 추상화. Step 1 판정이 통과하면 WS-002는 임베디드 기준으로 단순화한다.
- custom preset override의 완성형 UX. WS-002는 국면 기본 프리셋 + 슬롯 스냅 + 기존 저장 round-trip까지를 우선한다.
- 기존 회귀 2건: `cb001_step4_determinism_test`의 `D.deaths_recorded`, `sk001_step6_data_pack_test`의 `D.ChainHitsThree`.
- `run/main_scene`을 `workspace.tscn`으로 바꾸는 작업. main_scene 변경은 별도 승인 대상이다.

## Design Principles

### D1. 스파이크가 게이트다

임베디드 MDI는 기획상 사실상 확정이지만, WS-002의 첫 산출물은 믿음이 아니라 관찰이다. 세계 창 크기 트윈이 어색하면 다음 Step으로 가지 않는다. 이 경우 네이티브 유지와 연출 재설계는 별도 결정/태스크로 분리한다.

### D2. 마스터는 새 시스템이 아니라 WS-001의 승격이다

`WorkspaceWindowManager`, `WorkspaceShell`, `WorkspaceGeometry`, `WorkspaceLayoutPreset`, `WorkspaceLayoutStore`의 검증된 계약을 버리지 않는다. 바뀌는 것은 OS 창들의 절대 가상 데스크톱 좌표가 아니라, 마스터 내부에서 움직이는 subwindow 좌표를 다룬다는 점이다.

### D3. 창 예산은 게임 문법이다

정보 종류마다 창을 만들지 않는다. 동시에 봐야 하는 것만 창이 된다. `Situation`/`WeeklyAction`/`Calendar`는 창이 아니라 세계 창 거점 모드의 고정 영역이다. 이 구조가 아웃게임의 조조전식 단순함을 만든다.

### D4. 로그는 상주하지만 떠다니지 않는다

로그는 모든 국면에서 보이는 중요한 관찰 표면이다. 하지만 E-2 개정판의 하단 전폭 보장을 위해 WS-002에서는 floating managed window가 아니라 마스터 하단 도크가 된다. `LogWindowController`의 내용은 재사용하되, 소유/배치 계약은 마스터가 가진다.

### D5. 슬롯 스냅은 프리셋과 자유 이동의 접점이다

프리셋은 강제 고정 화면이 아니고, 자유 이동은 무질서가 아니다. 창을 끌 때 현재 국면의 슬롯 rect가 하이라이트되고, 놓으면 그 슬롯 크기로 맞춰지는 경로를 제공한다. 판정은 순수 geometry 함수로 둬서 D7 패턴처럼 헤드리스 테스트한다.

### D6. 경계 보장은 OS가 아니라 마스터가 맡는다

임베디드 전환 후 창 분실 방지는 decoration-aware 멀티모니터 gather가 아니라 마스터 내부 clamp 문제로 단순화된다. 창은 마스터의 content area 밖으로 나가지 않아야 하며, 로그 도크와 메뉴바 영역을 침범하지 않아야 한다.

### D7. Placeholder 이관은 지금 끝낸다

`Situation`/`WeeklyAction`/`Calendar`는 현재 placeholder Label 수준이라 독립 창 제거와 세계 창 내부 패널 이관 비용이 가장 낮다. 실제 DemoState/UI 장착 전에 창 다이어트를 끝내야 후속 콘텐츠가 낡은 창 계약에 묶이지 않는다.

## Architecture

### Embedded MDI Spike

첫 구현 Step은 `workspace.tscn`의 root viewport에서 `gui_embed_subwindows`를 활성화하고, 기존 managed `WorkspaceWindow`들을 embedded subwindow로 실행한다. 이 Step은 구조 전환의 proof다.

판정 항목:

- 세계 창이 `outgame` 크기(98×79)에서 `battle` 크기(64×76)로 같은 인스턴스에서 트윈될 때, OS decoration이 끼어들지 않고 연속적으로 보이는가.
- WS-001 테스트 3종(registry 62, geometry 66, persistence 40)이 그대로 통과하거나, 임베디드 좌표계 변경에 맞춘 rebaseline 후 동등한 실패/경계 조건을 보존하는가.
- native에서 필요했던 decoration 포함 rect, screen 원점, 멀티모니터 clamp 복잡도가 실제로 사라지는가.

스파이크는 제품 코드 전환의 게이트다. 실패 시 마스터/창 다이어트 구현에 들어가지 않는다.

### Master Window

Root/Main은 게임의 유일한 OS 창이며 마스터다. 기존 `WorkspaceShell`은 다음 책임을 가진다.

- 메뉴바/toolbar: 창 재호출, 프리셋 복귀, 창 모아오기.
- content area: 임베디드 창들이 움직이는 영역.
- bottom log dock: `LogWindowController`를 담는 하단 고정 영역.
- slot overlay: 창 drag 중 현재 프리셋 슬롯 하이라이트 표시.
- layout owner: 저장/복원, 국면 프리셋 적용, gather/clamp.

마스터의 content area는 메뉴바와 로그 도크를 제외한 rect다. 슬롯 스냅과 창 clamp는 이 rect를 기준으로 한다.

### Window Set v1

| 요소 | WS-002 배치 | 비고 |
| --- | --- | --- |
| World | 임베디드 managed window | `hub`/`battle`/`replay` content_mode 유지. 거점 모드에 캘린더/상황/행동 placeholder 패널 포함 |
| TacticBoard | 임베디드 managed window | 내용 placeholder. 전투=읽기 모드, 분석=편집 모드 표기만 가능 |
| Report | 임베디드 managed window | 내용 placeholder. 분석 국면에서 표시 |
| Log | 마스터 하단 도크 | GL-001 `LogWindowController` 통합 대상. floating registry에서는 제거 |

WS-001의 `Situation`, `WeeklyAction`, `Calendar`는 독립 `WorkspaceWindow` registry에서 제거한다. 이들의 placeholder 텍스트/영역은 `World` 거점 모드 내부에 남겨 후속 DemoState UI가 꽂힐 자리를 보존한다.

### Preset v1

E-7 개정판 기준:

- `outgame`: `World(hub)` 98×79, 하단 `LogDock` 98×19. `TacticBoard`/`Report` 숨김.
- `battle`: `World(battle)` 64×76, `TacticBoard(read)` 33×76, 하단 `LogDock` 98×18. `Report` 숨김.
- `analysis`: `Report` 34×76, `TacticBoard(edit)` 36×76, `World(replay)` 26×76, 하단 `LogDock` 98×18.

프리셋은 마스터 content area 기준의 비율/slot rect로 resolve한다. native screen work area 원점 기준 offset이 아니라 마스터 내부 rect 기준이다.

**좌표 계약(Step 0 리뷰 Finding 2 반영, Step 3/4 선행)**: E-7 수치(98×79 등)는 절대 px가 아니라 **master content area 대비 정규화 비율**(0~1 또는 백분율)의 소스로만 쓴다. 실제 px는 런타임 content rect에서 resolve한다. `content area = master rect − (메뉴바 + 하단 로그 도크)`로 정의하고, 창 사이 gutter는 명시 정책으로 둔다(값 합이 100에 못 미치는 여백을 gutter로 해석). 현 `WorkspaceLayoutPreset`의 절대 px offset/size는 Step 4에서 이 비율 계약으로 교체한다.

### Slot Snap

`WorkspaceGeometry`에 순수 함수로 슬롯 스냅 판정을 추가한다.

예상 입력:

- dragged window rect
- pointer/drop point 또는 dragged rect center
- current phase preset slots
- snap distance
- ignored window id 또는 snap disable flag
- master content rect

예상 출력:

- snap candidate 없음/있음
- 후보 slot id/window id
- highlight rect
- applied rect

**테스트 가능 기본 계약(Step 0 리뷰 Finding 3 반영)**: 자석 거리 = content area 짧은 변의 고정 비율(예: 6%), tie-break = slot id의 정규 순서(가능하면 ADR-017 정규 순서 재사용), hidden slot = 후보에서 제외(명시), 스냅 무시 = disable flag 입력 1개. modifier key/창 가장자리 스냅 등 세부 UX는 OD2대로 후속. 이 기본값을 고정해야 아래 경계 단언의 기대값이 성립한다.

완료 조건은 구체 구현 중 조정할 수 있으나, 최소한 다음 경계를 테스트한다.

- 거리 안이면 가장 가까운 슬롯 후보 반환.
- 거리 밖이면 후보 없음.
- 같은 거리에서는 stable tie-break.
- 숨겨진 창 슬롯은 필요 시 후보에서 제외하거나, 명시적으로 포함 정책을 테스트한다.
- 스냅 무시 조작이 켜져 있으면 후보 없음.
- applied rect는 마스터 content area를 벗어나지 않고 로그 도크와 겹치지 않는다.

### Persistence

`WorkspaceLayoutStore`는 계속 재사용한다. 저장 대상은 floating window만이 아니라 마스터 레이아웃 상태다.

- `World`/`TacticBoard`/`Report`의 rect/visible/content_mode 저장.
- `LogDock` height 또는 preset-resolved dock rect 정책 저장 여부는 Step 2 설계 리뷰에서 확정한다.
- 기존 `user://workspace_layout.cfg` version은 bump 또는 migration 정책을 둔다. WS-001 native 좌표 파일을 임베디드 좌표로 그대로 해석하면 안 된다.
- unsupported/old version은 기본 preset fallback하며 파일을 즉시 파괴하지 않는다.

## Open Decisions

> **소유 배정(Step 0 리뷰 종결, [[WS-002-Embedded-MDI-Master-Window-Review]]):** OD1→Step 1(게이트), OD2→Step 3(기본 계약은 Step 0 선지정), OD3→Step 2, OD4→Step 5 완료 리뷰 판정 후 후속 cleanup Task, OD5→WS-002 범위 밖 후속(마스터 내부 layout은 Step 2가 stretch와 무관하게 처리), OD6→Step 4.

1. **트윈 판정 실패 시 fallback**
   - 후보 A: 네이티브 Window 유지 + 출정/귀환 연출 재설계.
   - 후보 B: 트윈 품질 기준을 낮추고 임베디드 전환 계속 진행.
   - 권장: 후보 A. Step 1에서 세계 창 크기 트윈이 어색하면 중단하고 보고한다.

2. **슬롯 스냅 UX 세부**
   - 결정 필요: 자석 거리(px 또는 슬롯 크기 비율), 하이라이트 표현, 같은 거리 tie-break, 스냅 무시 조작(예: modifier key), 창 가장자리 스냅과 슬롯 스냅 우선순위.
   - 권장: 첫 구현은 슬롯 스냅만 독립 검증하고, 기존 `GameWindow`식 다른 창/마스터 가장자리 스냅은 레거시 정리 결정 후 확장한다.

3. **로그 도크 ↔ GL-001 LogWindow 통합 시점**
   - 후보 A: Step 2에서 `LogWindowController`를 바로 하단 도크에 이식.
   - 후보 B: Step 2는 placeholder dock으로 구조만 검증하고, GL-001 통합은 후속.
   - 권장: 후보 A를 우선 검토한다. 이미 GL-001 Step 2가 `workspace.tscn` Log 창에 붙어 있으므로, floating window 제거와 함께 통합해야 중복 UI가 남지 않는다.

4. **레거시 `WindowManager.cs`/`GameWindow.cs` 정리 시점**
   - 후보 A: 임베디드 마스터 정착 Step에서 삭제/비활성화.
   - 후보 B: WS-002 완료 후 cleanup Task로 분리.
   - 권장: Step 1~3 동안은 건드리지 않고, 완료 리뷰에서 실제 참조 여부를 근거로 cleanup Task를 연다. 제품 경로에 혼동이 생기면 같은 Step에서 제거한다.

5. **stretch 설정 도입 여부**
   - WS-001 OD6 후속이다. `battle_field.tscn`은 root window 리사이즈 시 빈 공간이 생긴다.
   - 후보 A: WS-002 마스터 전환과 함께 `display/window/stretch/*` 정책을 도입한다.
   - 후보 B: workspace 전용 scene에서만 마스터 크기 기준 layout을 처리하고, 전투 main_scene stretch는 별도 결정으로 둔다.
   - 권장: 후보 B. WS-002의 핵심은 workspace 구조 전환이며, `battle_field.tscn` stretch는 전역 blast radius가 있다.

6. **native layout config migration**
   - 후보 A: WS-001 `user://workspace_layout.cfg`를 unsupported old version으로 보고 기본 preset fallback.
   - 후보 B: native 좌표를 마스터 내부 비율로 변환하는 migration 작성.
   - 권장: 후보 A. W0 사용 데이터가 아직 placeholder 중심이고, 잘못된 좌표 변환보다 기본 preset 복구가 안전하다.

## Step Plan

### Step 0: Design Review

**완료(2026-07-10, 판정: Approved after design fixes).** 결과·근거는 [[WS-002-Embedded-MDI-Master-Window-Review]]. 리뷰가 지적한 설계 수정(embed blast radius, E-7 좌표 단위 계약, slot snap 기본 계약, GUI-only 트윈 실증, LogDock 저장 선행, OD 1~6 소유 배정)은 위 각 Step/Preset/OD 절에 반영했다. Step 1은 Finding 1(embed 축 결정)만 닫으면 착수 가능하고, Step 3/4는 Finding 2·3 계약 확정을 선행으로 둔다.

목표:
- WS-002가 WS-001 완료 사실, E-2/E-7/E-8 개정 요구, G-6 W0 정의를 충돌 없이 구현 가능한 Step으로 나눴는지 검토한다.

작업 범위:
- 이 Task 문서 검토.
- `WorkspaceWindowSystem` 현재 사실, WS-001 산출물, GL-001 로그 창 현재 상태, `project.godot` display/stretch 설정 대조.
- 임베디드 전환 실패 시 중단 기준과 후속 결정 경로 확인.
- 필요하면 Task 문서만 수정.

제외 범위:
- 제품 코드, `.tscn`, `.tres`, `project.godot` 변경.

완료 조건:
- Step 1의 트윈 판정 기준이 관찰 가능하다.
- Step 2~4가 Step 1 통과를 전제로만 진행됨이 명확하다.
- registry 변경, 로그 도크, 슬롯 스냅, persistence version 정책의 완료 조건이 테스트 가능한 수준이다.
- Open Decision 1~6의 소유 Step 또는 후속 처리 방식이 명확하다.

검증 방법:
- 설계 리뷰 문서 작성.
- 판정: Approved / Approved after design fixes / Rework required.

### Step 1: Embedded MDI Transition Spike

**구조 판정 PASS(2026-07-10). D1 트윈 게이트 최종 판정 = 사람 GUI 스모크 사인오프 대기.** headless probe가 게이트의 구조 계약(embedded → OS 애니메이션 경로 없음, 동일 인스턴스, 연속 크기 보간)까지 자동 단언했으나, "시각적으로 부드러운가"는 headless로 관찰 불가다. **Step 2 착수 전에 GUI 스모크 1회가 필요하다**: `godot --path . res://Assets/Script/Tests/ws002_embedded_tween_probe.tscn`를 비-headless로 실행해 세계 창 크기 tween이 시각적으로 연속·부드러운지 사람이 확인한다. 어색하면 D1대로 구현을 중단하고 Open Decision 1로 보고한다.

구현:
- `project.godot` `window/subwindows/embed_subwindows=false → true`(전역 스파이크, Finding 1 경로).
- probe 신규: `Assets/Script/Tests/Ws002EmbeddedTweenProbe.cs` + `ws002_embedded_tween_probe.tscn`(self-checking, exit 0/1).

검증 결과:
- **embed 전환**: probe가 registry 전체를 순회해 **모든 managed 창이 `IsEmbedded()==true`인지 단언한다**(하나라도 비embedded면 FAIL). 5종(world/situation/weekly_action/calendar/log) 모두 embedded, root viewport `GuiEmbedSubwindows==true`. 즉 마스터 밖 OS 창 분리 없음, native decoration/멀티모니터 clamp 경로가 이 창들에서 비활성.
- **세계 창 트윈**: 같은 `World` 인스턴스(id 고정)에서 거점 비율(1411×758, 98×79%) ↔ 전장 비율(922×730, 64×76%)로 tween. 두 구간 모두 인스턴스 재생성 없음, **width·height 두 축 각각 중간값 통과**(hub→battle 23/22, battle→hub 55/54 샘플), 목표 크기 정확 도달, tween 후에도 embedded 유지. probe ALL PASS, exit 0.
- **WS-001 회귀(Finding 4 확인)**: `ws001_step1`(62)/`ws001_step2`(66)/`ws001_step3`(40) 전부 ALL PASS, exit 0 — embed 플래그에 불변(pure geometry + headless viewport fallback). 헤드리스는 "제품 회귀 없음" 안전망으로 작동.
- **battle_field blast radius(Finding 1)**: 전역 embed on에서 `battle_field.tscn` 300프레임 부팅 clean(SCRIPT/Parse 에러 0, exit 0). `cb001_step1_turn_effect_test` ALL PASS(전투 결정론 경로 무영향).
- `dotnet build AutoCrawler.sln -c Debug` 경고 0/오류 0. Godot `--headless --path . --import` exit 0.

남은 위험 / 후속:
- **시각적 "부드러움" 최종 사인오프는 사람 GUI 실행 몫**이다. probe는 구조 계약(embedded=OS 애니메이션 경로 없음, 동일 인스턴스, 연속 크기 보간)까지 자동 단언했고, 이는 게이트의 핵심(“OS decoration이 끼어들지 않고 연속”)을 충족한다. 헤드리스 환경 제약으로 실제 화면 관찰은 미실행.
- World 창은 min_size 320×240이라 그 이하로는 크기 클램프된다(Step 4 전장 비율 slot rect가 이보다 큰지 확인 필요). headless live `root.Size`는 프로젝트 viewport(1440×960)와 다르니(작은 OS 기본값) 비율 소스로 쓰지 않는다.
- 임베디드 창과 `CanvasLayer` 메뉴 z-순서/겹침은 Step 2 content area/clamp(D6)에서 처리(Finding 5, 알려진 현상).

목표:
- 기존 `workspace.tscn`을 임베디드 MDI 모드로 실행하고, WS-002를 계속할 수 있는지 판정한다.

작업 범위:
- root viewport `gui_embed_subwindows` 활성화. **(Step 0 리뷰 Finding 1)** 현 구조는 `WorkspaceShell : Node` + managed Window가 SceneTree root viewport 하위라, workspace 전용 embedding viewport가 없다. 스파이크는 전역 `project.godot`의 `window/subwindows/embed_subwindows`를 켜서 관찰한다. workspace 국소화(자체 embedding Viewport 도입 + reparent)는 Step 2 마스터 승격 범위로 이관한다.
- 기존 `WorkspaceWindow` 5종을 가능한 한 최소 수정으로 embedded subwindow로 표시. 임베디드 창이 `CanvasLayer` 메뉴와 겹치는 현상은 Step 2 content area/clamp(D6)에서 해소될 알려진 현상으로 기록하고 트윈 판정과 분리한다(Finding 5).
- 세계 창 크기 트윈 probe 추가: `World`를 같은 인스턴스에서 outgame slot 크기와 battle slot 크기 사이로 tween.
- native 좌표계 의존 지점 조사: decoration rect, screen work area, `GetPositionWithDecorations`, gather.
- WS-001 헤드리스 테스트 3종을 실행하고, 임베디드 좌표계 차이 때문에 실패하는 경우 동등 계약을 보존하는 rebaseline 후보를 문서화한다.

제외 범위:
- registry 창 다이어트.
- 로그 도크 전환.
- 슬롯 스냅 구현.
- 실제 세계/보드/리포트 콘텐츠.

완료 조건:
- GUI probe에서 세계 창이 `World` 인스턴스 유지 상태로 거점 비율(98×79) ↔ 전장 비율(64×76)을 tween하며, OS decoration 애니메이션 없이 마스터 내부에서 연속적으로 보인다.
- 트윈 판정이 "통과/실패"로 기록된다. 실패면 WS-002 구현을 중단하고 Open Decision 1로 보고한다.
- `ws001_step1_registry_test`, `ws001_step2_geometry_test`, `ws001_step3_persistence_test`를 실행해 결과를 기록한다. **(Step 0 리뷰 Finding 4)** 이 3종은 pure geometry + headless(screen_count=0 → viewport fallback) 구성이라 embed 플래그에 사실상 불변이다. 따라서 embed/트윈 실증은 헤드리스가 아니라 GUI probe 전용으로 관찰하고, 헤드리스 3종은 "제품 회귀 없음" 안전망으로 둔다. 헤드리스에서 실패가 나면 임베디드 좌표계가 아니라 제품 회귀 신호로 우선 해석한다.
- **전역 embed on 상태에서 `battle_field.tscn`(현 `run/main_scene`)이 정상 부팅/실행되는지 회귀 관찰한다(Finding 1 blast radius).**
- `dotnet build AutoCrawler.sln -c Debug` 경고 0, 오류 0.
- Godot `--headless --path . --import` exit 0.
- GUI probe에서 embedded 창이 마스터 밖 OS 화면으로 분리되지 않으며, native decoration/멀티모니터 clamp가 더 이상 핵심 경로가 아님을 관찰한다.

검증 방법:
- Godot GUI smoke/probe.
- WS-001 헤드리스 테스트 3종.
- `dotnet build`.
- Godot `--import`.

### Step 2: Master Window and Log Dock

**구조/헤드리스 판정 PASS(2026-07-10). GUI 사인오프 대기(도크 시각/드래그/live resize).**

**Design Deviation(사용자 승인).** Step 0 Finding 1 주석은 "workspace 국소 embedding viewport(SubViewport) 도입"이었으나, 이 환경에서 SubViewport 내부 임베디드 창의 입력 포워딩(드래그)을 GUI로만 검증할 수 있어 관찰 불가 → 깨져도 못 잡을 위험. 사용자와 상의해 **content-rect 방식(A)**으로 확정: 전역 embed 유지(Step 1), 마스터 = 메뉴바(좌측) + content rect + 하단 전폭 로그 도크. 창은 root viewport에 embed되고 manager가 content rect로 clamp/gather한다. 전부 순수 geometry라 headless 검증 가능. battle_field는 별도 씬이라 무영향(Step 1 확인). 국소 embedding viewport는 필요 시 후속으로 재검토.

구현:
- `project.godot`: (Step 1의 전역 embed 유지).
- `WorkspaceGeometry`: content-rect 순수 함수 추가 — `ComputeContentRect(master, menuWidth, dockHeight, topInset)`, `IsInside`, `GatherIntoContent`. `ComputeContentRect`는 마스터가 메뉴/도크보다 작아도 content rect가 **마스터 경계 안에서 시작·종료**하도록 position까지 clamp한다(코드 리뷰 P2). 기존 WS-001 screen/decoration 함수는 회귀 커버용으로 유지.
- `WorkspaceWindowManager`: 상수(`MenuBarWidth=200`, `LogDockHeightRatio=0.2`, `ContentTopInset=32`) + `GetMasterSize()`(GUI=live root.Size, headless=project viewport) + `GetContentRect()`/`GetLogDockRect()` + `GatherIntoContentArea()`. `ApplyPreset`/`LoadLayout` 좌표계를 screen work area → 마스터 content rect로 단순화. **창 placement는 decoration(임베디드 타이틀바) 포함 '보이는 영역' rect 기준**이다: preset/슬롯/gather 대상 rect를 visible footprint로 보고 `GetDecorationRect`/`ApplyDecorationRect`(타이틀바 inset 반영, headless fallback (8,32)/(16,40))로 client 좌표를 계산한다. (GUI 회귀 수정: 초기엔 client rect로만 배치해 아래 창 타이틀바가 위 창 content를 덮었다 — 사용자 GUI 리포트.) **마스터 리사이즈 자동 회수**: `_Ready`에서 `root.SizeChanged`를 구독해 `GatherIntoContentArea()`를 돌리고 `_ExitTree`에서 해제한다(코드 리뷰 P2 — preset/load/버튼 외 리사이즈 hook 누락 보완).
- `WorkspaceLayoutPreset`: 3 preset에서 `log` 항목 제거(도크로 이관). E-7 비율 테이블 교체는 Step 4.
- `workspace.tscn`: `LogWindow`(floating) 제거, 메뉴를 좌측 스트립으로, 하단 전폭 `CanvasLayer/LogDock` PanelContainer 추가하고 `LogView`(LogWindowController)를 그 안으로 이관.
- `WorkspaceShell`: "창 모아오기" → `GatherIntoContentArea()`.

결정(소유 Step에서 확정):
- **OD3(로그 통합 시점) → 후보 A 확정**: GL-001 `LogWindowController`를 하단 도크에 바로 이식(placeholder 도크 없이). floating Log 제거와 동시 통합해 중복 UI 없음.
- **Finding 6(도크 저장 여부) → 도크는 persistence 저장 대상 아님**: 로그 도크 rect는 마스터 크기에서 재계산되는 파생값이라 저장하지 않는다. registry에 `log`가 없어 `SaveLayout`도 도크를 기록하지 않는다. Step 4 persistence 스키마는 floating 3종(world/tacticboard/report)만 저장하면 된다.

검증 결과:
- **ws002_step2_master_dock_test 신규(43 assertions) ALL PASS**: content rect가 메뉴/도크/inset 제외·작은 마스터에서도 마스터 경계 내부, 도크 하단 전폭·content와 비겹침, content gather 완전 포함·축소·멱등, registry 4종·log 미등록, `CanvasLayer/LogDock/LogView`=LogWindowController·floating Log 없음, content 밖 창 회수(메뉴/도크 비침범, 보이는 영역 기준), preset visible 창 content 내부, 마스터 리사이즈 content 재계산 + **`root.SizeChanged` hook 발화 시 창 자동 회수**, **preset visible 창들의 보이는 영역(타이틀바 포함) 상호 비겹침(GUI 오버랩 버그 회귀)**.
- **WS-001 회귀 rebaseline 후 ALL PASS**: `ws001_step1`(4창·log 미등록·메뉴 4버튼), `ws001_step2`(AllIds 4종·preset 좌표 content 기준·`GetContentRect`), `ws001_step3`(4창·load 후 content gather 회수). 순수 store round-trip(A/E)은 key-agnostic이라 무변경.
- **GL-001 회귀 ALL PASS**: `gl001_step1/2/3`. `gl001_step2` LogView 경로 `Windows/LogWindow/LogView` → `CanvasLayer/LogDock/LogView`로 갱신, 로그 탭/필터/seed/rowcount smoke 유지.
- **battle_field blast radius**: 전역 embed on에서 300프레임 부팅 clean(exit 0), `cb001_step1` ALL PASS.
- `dotnet build` 경고 0/오류 0. Godot `--import` exit 0.

남은 위험 / GUI 사인오프 대기:
- 하단 로그 도크의 실제 **시각 렌더**(전폭 상주), 임베디드 창을 마우스로 끌어 메뉴/도크 근처로 옮겼다가 "창 모아오기"로 회수되는 **드래그 UX**, live 마스터 리사이즈 시 재배치의 **시각적 결과**는 GUI 실행으로만 확인 가능(헤드리스에서 구조·좌표 계약 + 리사이즈 hook 발화까지 자동 단언). 리사이즈 자동 회수 로직 자체는 hook 배선 + 단위 테스트로 고정됐다. GUI 스모크: `godot --path . res://Assets/Scenes/workspace.tscn`.
- 임베디드 창은 content rect로 clamp되지만 드래그 중에는 잠깐 메뉴/도크 위로 넘어갈 수 있다(A 방식의 알려진 특성, "창 모아오기"/gather로 회수). 물리적 벽(SubViewport)은 후속 재검토 대상.
- WS-001 native screen gather(`GatherWindows`/`GetWorkArea`/`GetDecorationRect`)는 production 마스터 경로에서 미사용이나 `ws001_step2`[L]/[G] 회귀가 계속 커버한다. 레거시 정리는 OD4(Step 5 리뷰 후).

목표:
- Root/Main을 마스터 윈도우로 승격하고, 로그를 floating window에서 하단 도크로 전환한다.

작업 범위:
- `WorkspaceShell` layout을 메뉴바 + content area + bottom log dock 구조로 정리.
- `WorkspaceWindowManager`의 좌표계를 마스터 content rect 기준으로 단순화. **(Step 0 리뷰 Finding 1)** Step 1에서 전역 embed로 관찰한 뒤, 여기서 workspace 국소 embedding viewport 도입 + managed Window reparent를 확정한다.
- `Log`를 managed floating registry에서 제거하거나, 도크 전용 id로 별도 취급한다. **(Step 0 리뷰 Finding 6)** "LogDock를 persistence 저장 대상에 넣을지" 결정을 Step 2에서 확정하고, 이를 Step 4 persistence 스키마/version의 선행 입력으로 넘긴다.
- GL-001 `LogWindowController`를 하단 도크에 장착하거나, Open Decision 3에 따라 placeholder dock으로 구조를 먼저 검증한다.
- 마스터 내부 clamp/gather 구현: 창이 메뉴바/로그 도크/content rect 밖으로 나가지 않게 한다.

제외 범위:
- `Situation`/`WeeklyAction`/`Calendar` 이관.
- 슬롯 스냅.
- 로그 도크의 최종 visual skin.

완료 조건:
- `workspace.tscn` 실행 시 OS 창은 마스터 하나이고, 하단 로그 도크가 항상 마스터 내부 하단 전폭에 보인다.
- `Log` floating window는 메뉴에서 별도 창으로 열리지 않는다. 로그 내용은 하단 도크에서만 표시된다.
- GL-001 통합을 선택한 경우 기존 로그 탭/필터/read-only 표시 smoke가 도크에서 동작한다. placeholder를 선택한 경우 후속 통합 결정과 테스트 공백이 문서화된다.
- 창을 강제로 content rect 밖 좌표로 이동시킨 뒤 gather/clamp를 호출하면 메뉴바와 로그 도크를 침범하지 않는 위치로 회수된다.
- 마스터 resize 후 content area와 log dock rect가 재계산되고, visible 창들이 content area 안에 남는다.
- `dotnet build` 경고 0, 오류 0.
- Godot `--import` exit 0.
- 기존 registry/persistence 테스트는 새 log dock 계약에 맞춰 통과한다.

검증 방법:
- Godot GUI smoke.
- manager/clamp 헤드리스 테스트.
- GL-001 로그 UI 회귀 테스트(통합 시).
- `dotnet build`.
- Godot `--import`.

### Step 3: Layout Slot Snap

**구조/헤드리스 판정 PASS(2026-07-10). GUI 사인오프 대기(드래그 하이라이트/드롭 정렬/Alt disable).**

구현:
- `WorkspaceLayoutPreset.ResolveSlots(presetId, contentRect)`: 현재 국면 preset의 **visible 창만** 슬롯 rect로 resolve(hidden slot 제외), content 원점 기준 + content 안으로 clamp. E-7 비율 테이블 교체는 Step 4지만 이 계약은 유지.
- `WorkspaceGeometry.FindSlotSnap(draggedRect, slots, snapDistance, contentRect, snapDisabled)` + `SlotSnapResult`(HasCandidate/SlotId/HighlightRect/AppliedRect): 드래그 중심이 슬롯 중심에서 거리 안이면 가장 가까운 슬롯 후보. tie-break = 정규 순서(중심 Y→X→id ordinal). applied rect = 슬롯을 content 안으로 clamp. disable/빈 슬롯이면 후보 없음.
- `WorkspaceWindowManager`: `SlotSnapRatio=0.06`, `_currentPresetId`(ApplyPreset에서 갱신) + `CurrentPresetId`/`GetCurrentSlots()`/`GetSnapDistance()`(content 짧은 변×6%) + `PreviewSlotSnap`(하이라이트, 창 안 옮김)/`CommitSlotSnap`(슬롯 rect로 정렬). GUI 드래그 driver(`_Process`, GUI 전용): 위치 변화로 드래그 감지 → PreviewSlotSnap → 하이라이트 표시, 마우스 놓으면 CommitSlotSnap. **Alt = 스냅 무시**(Finding 3 disable flag).
- `workspace.tscn`: `CanvasLayer/SlotHighlight`(ColorRect, 반투명, mouse_filter=ignore, hidden) 추가 + manager `_slotHighlight` 배선.

기본 계약(Step 0 리뷰 Finding 3): 자석 거리 = content 짧은 변 6%, tie-break = 정규 순서, hidden slot 후보 제외, disable flag 1개(Alt). 창 가장자리/마스터 가장자리 스냅·modifier 세부는 OD2대로 후속.

검증 결과:
- **ws002_step3_slot_snap_test 신규(31 assertions) ALL PASS**: ResolveSlots visible-only/content-relative/hidden 제외/unknown 빈 dict, 거리 안/밖/경계(inclusive), nearest, tie-break(작은 Y·작은 id), disable 후보 없음, applied rect content clamp, manager preview/commit(world의 보이는 영역이 weekly 슬롯 rect로 정렬)·disable no-op, preset 전환 시 슬롯 후보 4→1→2 변화. (슬롯·드래그·commit 모두 decoration 포함 '보이는 영역' 좌표로 일관.)
- **전체 회귀 ALL PASS**: WS-001 ×3, GL-001 ×3, ws002 tween probe, ws002_step2(36), cb001_step1, battle_field 부팅(exit 0).
- `dotnet build` 경고 0/오류 0. Godot `--import` exit 0.

남은 위험 / GUI 사인오프 대기:
- 드래그 중 슬롯 하이라이트 렌더, drop 시 슬롯 rect 정렬, Alt 스냅 무시의 **시각적 결과**는 GUI 실행으로만 확인 가능. snap 판정·resolve·commit **로직은 순수 함수 + manager API로 전부 headless 단언**. GUI 드래그 감지(위치 폴링 + 마우스 상태)는 GUI 전용 글루라 미관찰. GUI 스모크: `godot --path . res://Assets/Scenes/workspace.tscn`.
- 슬롯 소스는 현재 W0 placeholder preset 좌표다. E-7 개정판 비율 테이블 교체는 Step 4(ResolveSlots 계약은 불변).

목표:
- 현재 국면 프리셋의 타일 슬롯을 창 드래그 중 스냅 대상으로 제공한다.

작업 범위:
- E-7 프리셋 slot rect를 `WorkspaceLayoutPreset`에서 순수하게 resolve.
- `WorkspaceGeometry`에 슬롯 후보 판정/적용 rect 순수 함수 추가.
- drag 중 slot highlight 표시.
- drop 시 후보가 있으면 창 rect를 슬롯 크기/위치로 적용.
- 스냅 무시 조작 또는 disable path 추가.

제외 범위:
- 레트로 OS 스타일 하이라이트 아트.
- 다른 창 가장자리 스냅/마스터 가장자리 스냅의 완성형 UX.
- 커스텀 슬롯 편집 UI.

완료 조건:
- 헤드리스 테스트에서 snap distance 안/밖, tie-break, disabled snap, content rect clamp, hidden slot 정책이 모두 단언된다.
- GUI smoke에서 창을 현재 프리셋 슬롯 근처로 끌면 하이라이트가 뜨고, 놓으면 슬롯 rect로 정렬된다.
- 스냅 무시 조작을 사용하면 같은 위치에서도 슬롯 정렬이 발생하지 않는다.
- 프리셋 전환 후 슬롯 후보가 새 국면 기준으로 바뀐다.
- `dotnet build` 경고 0, 오류 0.
- Godot `--import` exit 0.

검증 방법:
- `WorkspaceGeometry` 순수 함수 헤드리스 테스트.
- Godot GUI drag/drop smoke.
- `dotnet build`.
- Godot `--import`.

### Step 4: Window Diet and Preset v1

**구조/헤드리스 판정 PASS(2026-07-10). GUI 사인오프 대기(국면 전환 시각 배치).**

구현:
- **창 다이어트(scene)**: `SituationWindow`/`WeeklyActionWindow`/`CalendarWindow` 제거. floating managed 창은 `World`/`TacticBoard`/`Report` 3종. `Situation`/`WeeklyAction`/`Calendar`의 placeholder 텍스트는 `World` 거점 모드 내부 패널(`Windows/WorldWindow/Panels`: CalendarStrip·DangerGauge·ActionCards + ContentMode)로 이관. `TacticBoard`/`Report` placeholder window 신규(TacticBoard는 content_mode read/edit 라벨).
- **프리셋 v1(E-7 비율)**: `WindowLayout`을 절대 px offset/size → **content area 대비 정규화 `Rect2`(0~1)**로 전환(Step 0 Finding 2 계약). `WorkspaceLayoutPreset.ResolveRect(normRect, content)`가 런타임 content px로 변환. 테이블(E-7 원 비율): outgame=World(hub) 전면 / battle=World(battle) 64% + TacticBoard(read) 33% / analysis=Report 34% + TacticBoard(edit) 36% + World(replay) 26%. 로그 도크는 마스터가 별도 소유(preset 밖).
- **거점 패널 content-mode 게이팅**: `WorkspaceWindow`에 `_modeGatedPanels`(+`_gatedModeId`=hub) 추가. World의 hub 전용 패널(`Panels/HubPanels`: 캘린더/위험/행동 카드)은 content_mode==hub일 때만 보이고 battle/replay에서 숨는다(코드 리뷰 P2). ContentMode 라벨은 항상 표시.
- `WorkspaceWindowManager.ApplyPreset`이 `ResolveRect`로 정규화 비율을 resolve해 decoration-aware 배치 후, **min_size가 작은 슬롯(analysis World 26%)보다 크면 content 밖으로 삐져나가므로 배치 후 `GatherIntoContentArea()`로 회수**한다.
- **persistence version bump 1→2**(OD6 후보 A): WS-001 native 좌표/구 창 세트로 저장된 v1 파일은 `UnsupportedVersion` → 기본 preset fallback, 파일 파괴 없음.

결정:
- **TacticBoard content_mode = read(battle)/edit(analysis)** 구분을 둔다(World와 동형, `_contentModeLabel` 표시). Step 4 작업 범위의 미결 항목 확정.

검증 결과:
- **ws002_step4_window_diet_test 신규(40 assertions) ALL PASS**: registry 3종·제거된 4창(log/situation/weekly/calendar) 미등록, World 내부 hub 패널 존재, 국면별 visibility + content_mode(World hub/battle/replay, TacticBoard read/edit), World 인스턴스 불변, **E-7 비율 resolve(battle World 64%·Board 33% width·좌→우·content 내부)**, **거점 패널이 hub에서만 보이고 battle/replay에서 숨김·hub 복귀 시 복원**, v1 config UnsupportedVersion fallback·파일 보존·situation 미등록.
- **전체 회귀 rebaseline 후 ALL PASS**: WS-001 ×3(registry 3종·preset visibility/mode·정규화 resolve), ws002_step2(41)·step3(31, 슬롯 카운트 outgame1/battle2/analysis3·analysis에서 preview/commit·3창 비겹침), GL-001 ×3, ws002 tween probe, cb001_step1, battle_field 부팅(exit 0).
- `dotnet build` 경고 0/오류 0. Godot `--import` exit 0.

남은 위험 / GUI 사인오프 대기:
- 국면 전환 시 3창 병치/World content_mode 전환/TacticBoard read·edit 표시의 **시각 결과**는 GUI 실행으로만 확인 가능(구조·비율·visibility·mode·persistence는 headless 단언). 메뉴에 Situation/WeeklyAction/Calendar/floating Log 토글이 안 뜨는지도 GUI에서 재확인.
- 메뉴바가 좌측 스트립이라 E-7의 "full-width world 98%"와 달리 world는 content(=master−menu−dock) 대비 비율이다(Finding 2 계약). 메뉴 위치(좌측 vs 상단)는 후속 UX 조정 여지.

목표:
- E-2 창 다이어트를 registry와 프리셋 테이블에 반영한다.

작업 범위:
- `Situation`, `WeeklyAction`, `Calendar` managed window 제거.
- `World` 거점 모드 내부에 캘린더 스트립, 위험 게이지/가호/D-day, 행동 카드 6계열 placeholder 영역 추가.
- 신규 placeholder window `TacticBoard`, `Report` 추가.
- `WorkspaceLayoutPreset`을 E-7 개정판 비율로 갱신: outgame/battle/analysis.
- `World` content mode를 `hub`/`battle`/`replay`로 유지하고, `TacticBoard` mode는 읽기/편집 placeholder 구분을 둘지 결정한다.
- persistence version bump 또는 old native config fallback 정책 구현. **(Step 0 리뷰 Finding 6)** Step 2에서 확정한 "LogDock 저장 여부"를 스키마에 반영한다. OD6 권장(WS-001 native `workspace_layout.cfg`를 UnsupportedVersion으로 보고 기본 preset fallback, 파일 파괴 없음)을 따른다.

제외 범위:
- 실제 DemoState 행동 규칙.
- 택틱 보드 편집 기능.
- 리포트 데이터.
- 세계 창 전장 씬 장착.

완료 조건:
- registry에 floating managed 창으로 `World`, `TacticBoard`, `Report`만 남고, 로그는 하단 도크로 표시된다.
- 메뉴/창 재호출 목록에 `Situation`, `WeeklyAction`, `Calendar`, floating `Log`가 나타나지 않는다.
- outgame preset은 `World(hub)`와 log dock만 보이게 한다. 거점 내부 placeholder 3종이 세계 창 안에서 보인다.
- battle preset은 `World(battle)`, `TacticBoard(read placeholder)`, log dock을 표시하고 `Report`를 숨긴다.
- analysis preset은 `Report`, `TacticBoard(edit placeholder)`, `World(replay)`, log dock을 표시한다.
- `World`는 프리셋 전환 중 재생성되지 않는다. 같은 instance id에서 content_mode/rect만 바뀐다.
- old/unsupported `workspace_layout.cfg`는 기본 preset으로 fallback하고 파일을 즉시 파괴하지 않는다.
- 헤드리스 테스트에서 registry set, preset visibility/mode/slot rect, persistence fallback이 단언된다.
- `dotnet build` 경고 0, 오류 0.
- Godot `--import` exit 0.

검증 방법:
- registry/preset/persistence 헤드리스 테스트.
- Godot GUI smoke.
- `dotnet build`.
- Godot `--import`.

### Step 5: Review, Regression, and Documentation

**완료(2026-07-10, 판정: 완료).** 결과·근거는 [[WS-002-Embedded-MDI-Master-Window-Completion-Review]].
Step 1~4 완료 조건과 검증 결과를 완료 리뷰에서 대조했고, P0/P1은 남아 있지 않다. Workspace system 문서,
Current-State, Open-Tasks를 WS-002 현재 사실과 후속 기준으로 갱신했다. 레거시 정리(OD4), stretch 결정(OD5),
실제 콘텐츠 장착은 후속으로 분리했다.
목표:
- WS-002 완료 사실과 남은 후속을 Wiki에 정리하고, Step별 완료 조건을 리뷰로 대조한다.

작업 범위:
- `LLM_WIKI/20_Systems/Workspace-Window-System.md` 갱신.
- `LLM_WIKI/00_Index/Current-State.md` Workspace 절 갱신.
- `LLM_WIKI/00_Index/Open-Tasks.md`에서 WS-002 후속 정리.
- 이 Task 문서의 구현 결과/검증 결과/status 갱신.
- `LLM_WIKI/50_Reviews`에 WS-002 완료 리뷰 작성.

제외 범위:
- 제품 코드 추가 구현.
- WS-002 범위 밖 콘텐츠 장착.

완료 조건:
- Step 1~4 완료 조건과 실제 검증 결과가 리뷰 문서에서 대조되어 있다.
- P0/P1 리뷰 지적이 남아 있지 않다.
- WS-002 이후 실제 콘텐츠 장착 태스크(World hub 실물화, BattleSession, TacticBoard, Report, DemoState)가 Open-Tasks에 남아 있다.
- 기존 별도 회귀 2건은 WS-002 결과로 오인되지 않게 계속 범위 밖으로 기록된다.

검증 방법:
- 문서 링크/상태 정적 검토.
- 최종 리뷰 판정: 완료 / 수정 후 완료 / 미완료.


## Post-Completion Adjustments

2026-07-11 GUI 검증과 후속 UX 조정으로 다음 현재 사실이 추가됐다. 완료 리뷰의 Step 0~5 판정은 유지하되, 시스템 문서의 현재 사실은 [[Workspace-Window-System]]을 기준으로 한다.

- **native owned child-window 확정**: 임베디드 MDI 전제는 GUI 요구(하위 창이 마스터 밖으로 나갈 수 있음, 마스터 위 z-order 유지, 작업표시줄 1개 유지, 마스터 입력 차단 없음, 마스터 최소화 시 동기화)에 맞지 않았다. 현재는 `gui_embed_subwindows=false`, managed 창은 OS native `Window`로 뜨되 `Transient=true`, `Exclusive=false`, `TransientToFocused=false`로 설정한다. Windows에서는 `WorkspaceNativeWindowOwner`가 Win32 `GWLP_HWNDPARENT` owner 관계를 보강한다. 반복 `DisplayServer.WindowSetTransient(child, parent)` 호출은 Godot Windows backend가 중복 parent 에러 로그를 내므로 제거했다.
- **좌측 메뉴 → 상단 dropdown**: 좌측 `MenuPanel`은 제거됐고 `TopMenuBar` 아래 `WindowMenu`/`PresetMenu` `MenuButton`으로 이동했다. content rect는 좌측 200px 여백을 더 이상 제외하지 않고, 상단 메뉴바 32px + 창 타이틀 inset 32px + 하단 로그 도크 20%를 제외한다.
- **레트로 UI Theme 리소스화**: 빠른 실험용 C# Theme 생성은 `Assets/UI/Theme/RetroWin98Theme.tres`로 이동했다. `RetroPanelContainer`는 Theme fallback load만 수행하고, `RetroVBoxPanel`은 Theme 색상을 읽어 bevel draw만 담당한다. 색/여백/버튼/PopupMenu/PanelContainer 값은 `.tres`에서 수정한다.
- **최근 검증**: `dotnet build` 경고/오류 0, `ws001_step1_registry_test` ALL PASS, `ws002_step2_master_dock_test` 41 passed, `ws002_step4_window_diet_test` 40 passed, `gl001_step2_log_window_test` ALL PASS.
## Verification Matrix

| 영역 | 정상 | 실패/경계 |
| --- | --- | --- |
| Embedded spike | `gui_embed_subwindows`에서 기존 workspace 창 표시, 세계 창 크기 트윈 연속 | 트윈 어색함, 창이 OS 창으로 분리됨, WS-001 회귀 원인 불명 |
| Master bounds | 창이 content area 안에서 이동/resize, resize 후 재clamp | 메뉴바/로그 도크 침범, 마스터 밖 이동, content rect 0 또는 작은 창 |
| Log dock | 하단 전폭 도크 상주, GL-001 UI 표시 | floating Log 중복, 도크 resize 깨짐, controller 통합 누락 |
| Slot snap | 거리 안 후보/하이라이트/drop 정렬, 프리셋 전환 후 후보 갱신 | 거리 밖 스냅, tie-break 불안정, disable 무시, 로그 도크 겹침 |
| Window diet | registry에서 3개 floating + log dock, World 내부 고정 패널 | 제거된 창 메뉴 노출, World 재생성, placeholder 이관 누락 |
| Preset v1 | outgame/battle/analysis E-7 비율 적용 | 숨겨야 할 창 표시, content_mode 불일치, slot rect 비율 오류 |
| Persistence | 새 version 저장/복원, old native config fallback | native 절대좌표를 임베디드 좌표로 오해, corrupt config 파괴 |
| Regression | WS-001 동등 계약 유지, `dotnet build` 0경고/0오류, `--import` exit 0 | 기존 회귀 2건을 WS-002 실패로 오분류 |
| Scope boundary | placeholder 구조만, 콘텐츠 장착 후속 분리 | DemoState/BattleSession/TacticBoard/Report 구현이 섞임 |

## Related

- [[WS-001-Native-Window-Workspace-Shell]]
- [[Workspace-Window-System]]
- [[Open-Tasks]]
- [[Current-State]]
- [[STEP_REVIEW_WORKFLOW]]
- `GameDesign/기획서/06_UXUI.md`
- `GameDesign/기획서/08_데모_스코프.md`


