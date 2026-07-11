---
type: review
task: WS-002-Embedded-MDI-Master-Window
step: 0
status: complete
reviewed: 2026-07-10
tags: [review, workspace, window, mdi, design-review]
---

# Design Review: WS-002 Embedded MDI Master Window (Step 0)

이 문서는 WS-002 Step 0(Design Review)의 결과다. 제품 코드/`.tscn`/`.tres`/`project.godot`는 수정하지
않았고, 리뷰 결론 반영으로 Task 문서만 갱신했다(아래 처리 표시).

## 검토 대상

- Task: [[WS-002-Embedded-MDI-Master-Window]]
- 선행 완료 Task: [[WS-001-Native-Window-Workspace-Shell]](W0 Step 0~3 완료)
- 대조 시스템: [[Workspace-Window-System]], [[GameLog-System]]
- 기획 근거: `GameDesign/기획서/06_UXUI.md` E-2/E-7/E-8, `08_데모_스코프.md` G-6 — repo 밖 외부 기획(대조는
  Task 전사문 기준. E-9/07 수치표와 동일한 외부 문서 리스크).
- 대조 코드:
  - `Assets/Script/UI/Window/WorkspaceWindow.cs`(managed window = `Godot.Window` 직접 상속)
  - `Assets/Script/UI/Window/WorkspaceWindowManager.cs`(registry/preset/gather/persistence, decoration·screen 좌표계)
  - `Assets/Script/UI/Window/WorkspaceLayoutPreset.cs`(현 5창 preset 테이블, offset 단위)
  - `Assets/Script/UI/Window/WorkspaceShell.cs`(`: Node`, root 창은 `GetTree().Root`)
  - `Assets/Scenes/workspace.tscn`(root `Node` + `CanvasLayer` 메뉴 + `Windows` 하위 5 Window)
  - `Assets/Script/Tests/Ws001Step2GeometryTest.cs`(pure geometry + `AddChild(workspace)` 통합 1회)
  - `project.godot`(`window/subwindows/embed_subwindows=false`, viewport 1440×960, stretch 미설정)

## 코드 대조 결과 (Task 현황 서술 검증)

Task의 현황 서술은 대체로 정확하다. 실측으로 확인한 사실:

- `WorkspaceWindow`는 `Godot.Window`를 직접 상속한다(`WorkspaceWindow.cs:9`). 임베디드 subwindow는 여전히
  `Window` 노드이므로, 상속 구조상 임베디드 전환에 추가 계층은 필요 없다 — D2("승격이지 신규 시스템 아님") 전제가 맞다.
- `WorkspaceLayoutStore`/`WorkspaceGeometry`/`WorkspaceLayoutPreset`은 Window/DisplayServer를 모르는 순수
  코드다. 재사용 전제가 코드로 확인된다.
- `WorkspaceWindowManager`는 `GetPositionWithDecorations`/`GetSizeWithDecorations`/`ScreenGetUsableRect`/
  `GetPrimaryScreen`/`GetScreenCount`에 의존한다(`WorkspaceWindowManager.cs:197~359`). 이 좌표계가 임베디드
  전환의 실제 단순화 대상이다 — Task의 "manager 좌표계만 마스터 내부로 단순화" 서술과 일치한다.
- **현재 preset offset은 절대 px가 아니라 "대상 screen work area 원점 기준 offset"이고, 값은 임시값이다**
  (`WorkspaceLayoutPreset.cs:15`, 예: outgame world `(20,20) 640×480`). E-7 개정판 비율(98×79 등)은 아직 코드에
  없다. Step 4 대상 확인.
- `WorkspaceShell : Node`이며(`WorkspaceShell.cs:9`) 메뉴는 `CanvasLayer`에, managed Window 5종은 `Windows`
  Node 아래에 있다(`workspace.tscn`). **managed Window들의 최근접 상위 Viewport는 SceneTree root Window다** —
  이 사실이 Finding 1의 근거다.

## Findings

1. **[P2] Step 1의 `gui_embed_subwindows` 활성화가 "최소 수정"으로 workspace에 국한되지 않는다 (blast radius)**
   - 위치: Task Step 1 작업 범위 "root viewport `gui_embed_subwindows` 활성화 / 기존 창을 최소 수정으로 embedded
     표시". 대조: `WorkspaceShell : Node`(`WorkspaceShell.cs:9`), managed Window는 `Windows` Node 아래
     (`workspace.tscn`), 상위 Viewport는 SceneTree root.
   - 문제: `gui_embed_subwindows`는 Viewport 속성이다. 현 구조에는 workspace 전용 중간 Viewport/Window가 없어,
     "최소 수정" 경로는 사실상 전역 `project.godot`의 `window/subwindows/embed_subwindows`(현재 `false`)를
     켜는 것뿐이다. 이 전역 플래그는 `run/main_scene`인 `battle_field.tscn`을 포함한 **모든 씬**에 적용된다.
     즉 Step 1이 전투 씬 창/서브윈도우 렌더링에 blast radius를 만들 수 있다(OD5의 stretch 우려와 같은 계열).
     workspace에만 국한하려면 셸 root(또는 content host)를 자체 `gui_embed_subwindows`를 가진
     `Window`/`SubViewport`로 만들고 managed Window를 그 아래로 reparent해야 하는데, 이는 "최소 수정"이 아니라
     Step 2 마스터 승격과 겹치는 구조 변경이다.
   - 영향: Step 1 착수 직전에 닫아야 하는 blocker. 방향 미결이면 스파이크가 잘못된 축(전역 vs 국소)에서 관찰된다.
   - 권장: 스파이크 목적(트윈 품질 관찰)에는 전역 플래그로 충분하다. 단 Step 1 완료 조건에 **"전역 embed on
     상태에서 `battle_field.tscn`이 정상 부팅/실행되는지"** 회귀 관찰을 추가하고, workspace 국소화(자체 embedding
     Viewport 도입 + reparent)는 Step 2 마스터 승격 범위로 명시 이관한다. -> Task Step 1/Step 2에 반영함.

2. **[P2] E-7 프리셋 수치(98×79 등)의 단위와 기준 프레임이 정의되지 않았다 (Step 3/4 완료 조건 테스트 불가)**
   - 위치: Task Context/Preset v1/Verification Matrix가 `세계 98×79`, `로그 98×19`, `보드 33×76` 등을 "1920×1080
     기준"으로 인용한다. 값의 합(outgame 79+19≈98 세로, battle 64+33≈97 가로)으로 보아 **마스터 content area의
     백분율(hundredths)**로 읽히지만, Task는 이를 명시하지 않는다. 현 코드 preset은 절대 px(`640×480`)이고 offset은
     screen work area 원점 기준이다(`WorkspaceLayoutPreset.cs:15,50`).
   - 문제: Step 3 완료 조건("slot rect 비율로 resolve", "content rect clamp")과 Step 4 완료 조건("slot rect 비율
     오류" 실패 케이스)은 값의 단위·기준이 고정돼야 단언 가능하다. 백분율/절대/그리드 중 무엇인지, 창 사이 gutter를
     명시적으로 두는지, master content rect가 정확히 무엇(메뉴바·로그도크 제외 rect)인지가 열려 있으면 slot rect
     계산 결과를 테스트로 못 박을 수 없다.
   - 영향: Step 1은 막지 않는다. Step 3(slot snap 순수 함수)·Step 4(preset 테이블 갱신) 착수 전 반드시 닫는다.
   - 권장: Step 3 진입 전에 **preset/slot 좌표 계약을 "master content area 대비 정규화 비율(0~1 또는 백분율) +
     명시 gutter 정책"**으로 고정하고, content area = master rect − (메뉴바 + 하단 로그도크)로 정의한다. E-7 수치는
     이 비율의 소스로만 쓰고, 실제 px는 런타임 content rect에서 resolve한다(native screen 원점 offset 폐기와 정합).
     -> Task Preset v1/Step 3/Step 4에 계약 명시를 반영함.

3. **[P2] Slot Snap 세부 파라미터(OD2)가 Step 3 완료 조건을 테스트 가능하게 만들 만큼 고정되지 않았다**
   - 위치: Task OD2 + Step 3 완료 조건("snap distance 안/밖, tie-break, disabled snap, hidden slot 정책 단언").
     자석 거리 단위(px vs content 비율), 하이라이트 표현, tie-break 규칙, 스냅 무시 조작(modifier key 등), hidden
     slot 포함/제외 정책이 "구현 중 조정 가능"으로만 남아 있다.
   - 문제: 순수 geometry 함수의 완료 조건은 경계 입력이 확정돼야 헤드리스 단언이 성립한다. tie-break가 미정이면
     "같은 거리 stable tie-break" 단언의 기대값이 없다.
   - 영향: Step 3 국한. Step 1/2는 무관.
   - 권장: Step 3 착수 시 최소 기본값을 ADR 없이 Step 내에서 못 박되, Step 0에서 **테스트 가능한 기본 계약**을
     선지정한다: 자석 거리 = content area 짧은 변의 고정 비율(예: 6%), tie-break = slot id의 정규 순서(ADR-017
     정규 순서 재사용 가능하면 그것), hidden slot = 후보에서 제외(명시), 스냅 무시 = disable flag 입력 1개.
     세부 UX(modifier key, 창 가장자리 스냅)는 OD2 권장대로 후속. -> Task Step 3/OD2에 기본 계약을 명시함.

4. **[P3] Step 1의 WS-001 헤드리스 테스트 재실행 완료 조건이 실제로는 embed 불변이라 관찰력이 약하다**
   - 위치: Task Step 1 완료 조건 "`ws001_step1/2/3`를 실행해 결과 기록, 실패 시 임베디드 좌표계 vs 제품 회귀 분리".
   - 관찰: `Ws001Step2GeometryTest.cs`는 pure `WorkspaceGeometry` 함수(명시 Rect2I 입력)와 `AddChild(workspace)`
     통합 1회로 구성된다. 헤드리스 DisplayServer는 screen_count=0이라 manager가 viewport(1440×960)로 fallback하고
     decoration/screen 실측 경로가 애초에 타지 않는다. 따라서 전역 `embed_subwindows`를 켜도 **헤드리스 3종
     결과는 바뀌지 않을 가능성이 높다** — 즉 이 완료 조건은 "통과"를 관찰해도 임베디드 전환을 실증하지 못한다.
   - 영향: 낮음(테스트 유지 자체는 회귀 안전망). 다만 Step 1의 핵심 증거를 헤드리스에 기대면 잘못된 안심을 준다.
   - 권장: Step 1의 트윈/embed 실증은 **GUI probe 전용**으로 명시하고, 헤드리스 3종은 "제품 회귀 없음"의 안전망으로
     역할을 재규정한다. "실패 시 좌표계 vs 회귀 분리"는 헤드리스에서 실패가 안 날 공산이 크므로, 실패가 나면 그것은
     embed 좌표계가 아니라 제품 회귀 신호로 우선 해석한다. -> Task Step 1 완료 조건/검증 방법에 반영함.

5. **[P3] 임베디드 창과 `CanvasLayer` 메뉴/향후 로그 도크의 z-순서·겹침이 Step 1에서 미정의다**
   - 위치: `workspace.tscn`의 메뉴는 `CanvasLayer`, managed Window는 root viewport로 embed된다. 임베디드
     subwindow는 메인 viewport 위에 렌더되어 `CanvasLayer` 메뉴와 겹칠 수 있다.
   - 문제: Step 1(최소 전환)에서 메뉴/창 겹침이 발생하면 "트윈이 어색하다"의 오판 요인이 된다. content area
     clamp(D6)는 Step 2 범위라 Step 1엔 없다.
   - 권장: Step 1 관찰 시 겹침은 "Step 2 content area/clamp로 해소될 알려진 현상"으로 사전 기록해 트윈 판정과
     분리한다. -> Task Step 1에 관찰 주의로 주석.

6. **[P3] 로그 도크 persistence 정책이 Step 2 설계 리뷰로 지연되어 Step 4 version 정책과 순서 의존이 생긴다**
   - 위치: Task Architecture Persistence "LogDock height/dock rect 저장 여부는 Step 2 설계 리뷰에서 확정",
     Step 4 "version bump 또는 old native config fallback 정책 구현".
   - 문제: 저장 스키마(무엇을 저장하는가)가 Step 2에서 늦게 확정되는데 version bump/마이그레이션은 Step 4다.
     LogDock를 저장 대상에 넣기로 Step 2에서 정하면 Step 4 스키마/version이 그에 맞춰 바뀌어야 한다. 순서 자체는
     성립하나 의존을 명시하지 않으면 Step 4에서 스키마 재작업 위험.
   - 권장: Step 2 완료 시 "LogDock 저장 여부" 결정을 Step 4 persistence 스키마의 선행 입력으로 명시. OD6(native
     config = unsupported fallback) 권장은 유지(저장 파일 파괴 없이 기본 preset 복구). -> Task Step 2/Step 4에 의존 주석.

## Open Decisions 소유 배정 (Step 0 완료 조건 #4)

Task의 OD 1~6를 소유 Step 또는 후속으로 배정한다(닫힌 것은 판정 표기).

1. **트윈 판정 실패 fallback -> Step 1 소유(게이트).** 권장 후보 A(네이티브 유지 + 연출 재설계 별도 분리) 유지.
   실패 시 WS-002 구현 중단·보고가 Step 1 완료 조건에 이미 있음. **소유 명확.**
2. **슬롯 스냅 UX 세부 -> Step 3 소유.** 단 Finding 3의 테스트 가능 기본 계약을 Step 0에서 선지정(자석 거리 비율,
   tie-break 정규 순서, hidden slot 제외, disable flag). 창 가장자리 스냅/ modifier UX는 후속. **분할 배정 명확화.**
3. **로그 도크 ↔ GL-001 통합 시점 -> Step 2 소유.** 권장 후보 A(Step 2에서 `LogWindowController` 하단 도크 이식).
   GL-001이 이미 `workspace.tscn` Log 창에 붙어 있어 floating 제거와 동시 통합해야 중복 UI가 안 남음. **소유 명확.**
4. **레거시 `WindowManager.cs`/`GameWindow.cs` 정리 -> Step 5 완료 리뷰에서 참조 근거로 판정 후 후속 cleanup Task.**
   Step 1~4는 건드리지 않음. **후속 처리 명확.**
5. **stretch 도입 -> WS-002 범위 밖(후속 결정), 권장 후보 B.** 단 **마스터 자체 content/log dock rect 재계산은
   Step 2 완료 조건에 이미 포함**되므로 workspace 내부 layout은 stretch와 무관하게 처리된다. `battle_field.tscn`
   stretch는 전역 blast radius로 별도 Task([[Open-Tasks]] 기존 항목). **경계 명확.**
6. **native layout config migration -> Step 4 소유.** 권장 후보 A(WS-001 `workspace_layout.cfg`를
   UnsupportedVersion으로 보고 기본 preset fallback, 파일 파괴 없음). 현 store의 fallback 계약 재사용. **소유 명확.**

## Step Assessment

- **Step 1 (Embedded Spike)**: 입력=WS-001 산출물. 출력=embed 전환 + 트윈 probe + 판정 기록. **선행 blocker 1건**
  (Finding 1: embed 활성화 축 = 전역 vs workspace 국소). 이를 "전역 embed로 스파이크 + battle_field 부팅 회귀
  관찰, 국소화는 Step 2" 로 닫으면 착수 가능. Finding 4/5는 완료 조건 관찰력 보정(문서 반영으로 해소). 트윈 게이트가
  D1대로 하드 게이트인 점은 적절.
- **Step 2 (Master + Log Dock)**: 입력=Step 1 통과. 출력=마스터 승격 + 하단 도크 + 내부 clamp/gather. OD3(도크
  통합)·Finding 1 국소화·Finding 6(도크 저장 여부→Step 4 선행)을 여기서 확정. content/log dock rect 재계산 완료
  조건이 관찰 가능. 선행 조건 명확.
- **Step 3 (Slot Snap)**: 입력=Step 2 content rect + preset slot. **선행 2건**(Finding 2 E-7 단위 계약, Finding 3
  스냅 기본 파라미터). 순수 함수 완료 조건은 이 둘을 닫아야 단언 가능. 닫으면 헤드리스 검증 강함.
- **Step 4 (Window Diet + Preset v1)**: 입력=Step 2/3. 출력=registry 4종화 + World 내부 placeholder 이관 + E-7
  비율 preset + persistence version 정책. **선행**: Finding 2(preset 비율 계약), Finding 6(도크 저장 여부),
  OD6(fallback). D7대로 placeholder 이관을 지금 끝내는 결정은 타당(콘텐츠 장착 전 최저 비용).
- **Step 5 (Review/Docs)**: 정적 검토 + 완료 리뷰. OD4 레거시 정리 판정, 기존 회귀 2건 오분류 방지 기록. 문제 없음.

Step 분해 자체는 적절하다. 스파이크→마스터→스냅→다이어트→리뷰가 독립 검증 가능하고, D1 게이트가 실패 전파를 막는다.
구조 개선(마스터 승격)과 기능(스냅/다이어트)을 Step 사이에 섞지 않는다(워크플로 원칙 7 준수).

## Verification Assessment

- **Step 1**: GUI probe가 유일한 embed/트윈 실증 표면이다(Finding 4). 필요한 관찰 — 같은 `World` 인스턴스에서
  outgame↔battle 비율 트윈이 OS decoration 애니메이션 없이 연속, 임베디드 창이 마스터 밖 OS 창으로 분리되지 않음,
  전역 embed on에서 `battle_field.tscn` 정상 부팅(Finding 1 회귀). 헤드리스 3종은 "제품 회귀 없음" 안전망.
- **Step 2**: manager clamp 헤드리스(창을 content rect 밖 좌표로 강제 → gather/clamp가 메뉴바·로그도크 비침범
  위치로 회수), 마스터 resize 후 content/log dock rect 재계산, GL-001 로그 UI 회귀(통합 시).
- **Step 3**: 순수 함수 헤드리스 — snap distance 안/밖, tie-break 안정성(정규 순서), disabled snap, content rect
  clamp(로그 도크 비겹침), hidden slot 제외, preset 전환 후 후보 갱신. GUI drag/drop smoke.
- **Step 4**: registry set(3 floating + dock), preset visibility/content_mode/slot rect(E-7 비율), World 인스턴스
  불변, old/unsupported config fallback(파일 비파괴). 제거된 창 메뉴 비노출.
- **빠진 실패 시나리오(현 계획 보완 권장)**: (a) 전역 embed on에서 battle_field 회귀(Finding 1) — Step 1에 추가.
  (b) content area가 0/음수(메뉴바+도크가 마스터보다 큰 극단 resize) 시 clamp 방어 — Step 2 Verification Matrix에
  "content rect 0 또는 작은 창"으로 이미 있음, 테스트로 승격 권장. (c) 기존 회귀 2건(`cb001_step4`,
  `sk001_step6`)을 WS-002 실패로 오분류 금지 — Step 5에 이미 명시.
- **외부 기획 대조 한계**: E-2/E-7/E-8 원문이 repo 밖이라 이 리뷰는 Task 전사문 기준이다. 기획 개정 시 Task/코드가
  자동으로 따라가지 않는다(GL-001/SK-001과 동일 리스크).

## Verdict

**Approved after design fixes.**

설계는 WS-001 검증 계약을 재사용하고 D1 게이트로 스파이크 실패 전파를 막는 견고한 구조다. P0/P1 없음. Step 분해·
경계·완료 조건이 대체로 탄탄하다. 아래 문서 수정(이 세션 반영)으로 Step 0 완료 조건(트윈 판정 관찰 가능, Step 2~4가
Step 1 통과 전제, registry/도크/스냅/persistence 완료 조건 테스트 가능, OD 1~6 소유 명확)을 충족한다:

- OD 1~6 소유 Step 배정(위 표) — Step 0 완료 조건 #4.
- Finding 1: Step 1 embed 활성화 = 전역 스파이크 + `battle_field.tscn` 부팅 회귀 관찰, workspace 국소화는 Step 2.
- Finding 2: preset/slot 좌표 = master content area 정규화 비율 + 명시 gutter 계약(Step 3/4 선행).
- Finding 3: slot snap 테스트 가능 기본 계약 선지정(Step 3).
- Finding 4: Step 1 embed/트윈 실증은 GUI probe 전용, 헤드리스 3종은 회귀 안전망으로 역할 재규정.
- Finding 6: Step 2 "LogDock 저장 여부"를 Step 4 persistence 스키마 선행 입력으로 명시.

**착수 순서**: Step 1은 Finding 1(embed 축 결정)만 닫으면 즉시 착수 가능하다 — Finding 2/3은 Step 3/4 게이트이므로
Step 1을 막지 않는다. Step 3/4는 Finding 2·3 계약을 닫기 전에는 착수하지 않는다.

## Related

- [[WS-002-Embedded-MDI-Master-Window]]
- [[WS-001-Native-Window-Workspace-Shell]]
- [[Workspace-Window-System]]
- [[GameLog-System]]
- [[STEP_REVIEW_WORKFLOW]]
