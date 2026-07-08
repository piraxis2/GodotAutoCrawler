---
id: WS-001-Review
type: review
task: WS-001
status: complete
date: 2026-07-09
---

# WS-001 Native Window Workspace Shell Review

## Step 0 설계 리뷰

검토 범위: [[WS-001-Native-Window-Workspace-Shell]], `project.godot`,
`Assets/Script/WindowManager.cs`, `Assets/Script/UI/Window/GameWindow.cs`,
`Assets/Script/gds/config_file_handler.gd`, `main.tscn`,
`Assets/UI/Window/inspector.tscn`, `Assets/UI/Window/field_window.tscn`,
`Assets/Script/Tests/*`(테스트 대역 패턴), [[STEP_REVIEW_WORKFLOW]].

제품 코드/`.tscn`/`.tres` 변경 없음. 이 문서만 추가했다.

## Findings

### [P1] 현재 `project.godot` display 설정이 Step 1 완료 조건을 관찰 불가능하게 만든다

- 조건: 앱을 그대로 실행할 때.
- 실제 코드: `project.godot:45-47`이 `window/size/resizable=false`,
  `window/size/minimize_disabled=true`, `window/size/maximize_disabled=true`다.
- 영향:
  - Step 1 완료 조건 "메인 창 최소화/복원 시 sub window 동기화가 유지된다"는 사용자가 root window를
    최소화할 수 없으므로 수동 smoke로 관찰할 수 없다. `WindowManager._Process`
    (`WindowManager.cs:21`)의 `Root.Mode == Minimized` 분기는 사실상 도달하지 않는다.
  - D2("Root window는 작업대 관리자다")와 달리 root window는 크기 조절/최대화가 막혀 있어
    메뉴바 셸로 쓰기 어렵다.
- 권장: Step 1 작업 범위에 `project.godot` display 설정 변경(`resizable=true`,
  `minimize_disabled=false`, `maximize_disabled=false`)을 **명시적으로** 포함하고,
  이 변경이 `battle_field.tscn` 등 1440x960 고정 뷰포트를 전제한 기존 씬에 주는 영향을
  Step 1 검증 항목으로 넣는다. `window/subwindows/embed_subwindows=false`
  (`project.godot:48`)는 이미 네이티브 Window 방향이므로 유지한다.

### [P1] 셸을 어느 씬이 호스팅하는지 미정이고, main scene은 `main.tscn`이 아니다

- 조건: Step 1 "앱 실행 후 Window 5종을 독립적으로 열 수 있다"를 구현할 때.
- 실제 코드: `project.godot:18` `run/main_scene="uid://dv7r53qteumxi"`는
  `Assets/Scenes/Map/battle_field.tscn`이다. `main.tscn`(uid `cgcwqpybenfsh`)은 main scene이 아니다.
  Task Context(35~41행)는 `main.tscn`을 윈도우 실험 씬으로만 서술하고 main scene 사실을 적지 않았다.
- 영향: 구현자가 (a) main_scene을 임의로 교체하거나 (b) `battle_field.tscn`에 셸을 끼워 넣는
  선택을 하게 된다. (a)는 CB-001 결정론 회귀 테스트의 수동 실행 경로를, (b)는 결정론 씬을 오염시킨다.
  둘 다 Out of Scope("전투 세션 함수화") 경계를 넘는다.
- 권장: Open Decision을 하나 추가한다. 권장안 = W0은 신규 `Assets/Scenes/workspace.tscn`을 만들고
  `godot --path . res://Assets/Scenes/workspace.tscn`으로 직접 실행해 검증한다.
  `run/main_scene` 전환은 W0 Out of Scope로 두고 별도 Step/Task로 미룬다.

### [P1] `gather_windows()`의 "화면 안전 영역"이 정의돼 있지 않아 구현·판정이 불가능하다

- 조건: Step 2 완료 조건 "화면 밖 좌표를 강제로 넣어도 `gather_windows()` 후 다시 보이는 영역으로
  돌아온다", Step 3 완료 조건 "다중 모니터에서 저장된 좌표가 현재 화면 밖이면 회수된다".
- 실제 문서: "현재 화면의 안전 영역"(Task 116, 229행)만 있고 어느 스크린인지, 얼마나 겹쳐야
  "보이는" 것인지, 데코레이션(타이틀바)을 포함하는지가 없다. D5는 이것을 W0 핵심 UX로 규정하는데
  가장 중요한 계약이 비어 있다.
- 영향: 구현자마다 다른 규칙을 만들고, 리뷰어는 완료 여부를 판정할 수 없다. 특히 Godot `Window.Position`은
  데코레이션을 제외한 클라이언트 영역 기준이므로, usable rect에 클라이언트 rect만 clamp하면
  타이틀바가 화면 위로 잘려 **창을 잡아서 옮길 수 없는** 상태가 그대로 남는다.
- 권장: 다음 계약을 Task에 명문화한다.
  - 대상 스크린 = 창 중심이 포함된 스크린. 없으면 `DisplayServer.SCREEN_PRIMARY`.
  - 기준 사각형 = `DisplayServer.ScreenGetUsableRect(screen)`.
  - 판정 = `Window.GetPositionWithDecorations()` / `GetSizeWithDecorations()`로 만든 rect가
    usable rect와 최소 임계치(예: 타이틀바 높이 + 좌우 64px) 이상 교차하는가.
  - 회수 = 위반 시 데코 포함 rect를 usable rect 안으로 clamp하고, size가 usable rect보다 크면 축소.
  - Root window도 회수 대상인지 여부(아래 P2 참조).

### [P2] Root window가 managed window인지 아닌지가 문서 안에서 모순된다

- 조건: registry/preset/gather/persistence의 대상 집합을 정할 때.
- 실제 문서: Window Set v0 표(90~97행)는 Root/Main을 포함한 6행이지만, Step 1 완료 조건(211행)은
  "Window 5종"이다. `apply_preset`이 root window의 position/size도 바꾸는지, `gather_windows()`가
  root를 회수하는지, 배치 저장이 root를 포함하는지 모두 미정이다.
- 영향: Step 2/3의 완료 조건이 대상 집합에 따라 달라진다. Godot에서 root window는 sub window와
  수명 주기·API가 다르므로(자유롭게 hide 불가) 뒤늦게 포함시키면 registry 계약이 깨진다.
- 권장: "Root window는 registry에 `root` id로 등록하되 `visible` 적용 대상에서 제외하고,
  position/size 저장·gather 대상에는 포함한다"처럼 한 문장으로 확정한다. Step 1 완료 조건의
  "5종"과 표의 6행을 일치시킨다.

### [P2] Context가 기존 Window 자산을 사실과 다르게 서술한다

- 실제 코드:
  - `Assets/UI/Window/field_window.tscn`의 루트는 `Window`가 아니라 `Node`("main")이고,
    `main.tscn`의 복사본에 가깝다. 게다가 `field_window.tscn:4`가 **자기 자신의 uid
    (`uid://o131wnnlspr3`)를 ext_resource로 선언하고 `:46`에서 자신을 자식으로 인스턴스**한다.
    재사용 가능한 Window 자산이 아니라 정리 대상이다.
  - `Assets/UI/Window/inspector.tscn`만 실제 `Window`(+`GameWindow.cs`)다. 다만
    `inspector.tscn:6-15`가 `popup_window=true`, `unresizable=true`, `min_size == max_size == 400x600`이다.
    `popup_window` Window는 포커스를 잃으면 닫히고 taskbar에 표시되지 않으며 부모 위에 고정되므로
    워크스페이스 창(독립 이동/크기 변경/멀티모니터)에 부적합하다.
- 영향: Task Context(37~40행)를 근거로 구현자가 `inspector.tscn`을 WorkspaceWindow의 원본으로
  복제하면 Step 1 완료 조건("각 Window는 독립적으로 이동/크기 변경 가능하다")을 만족할 수 없다.
- 권장: Context를 위 사실로 교정하고, Step 1 작업 범위에 "신규 `workspace_window` 기반 씬을 만들며
  `popup_window=false`, `unresizable=false`로 둔다"를 적는다. `field_window.tscn` 자기 참조 정리는
  별도 cleanup 항목으로 [[Open-Tasks]]에 남긴다.

### [P2] `GameWindow` 확장(Open Decision 1의 암묵 전제)은 위험하다

- 실제 코드 `Assets/Script/UI/Window/GameWindow.cs`:
  - `:29` `_snapTarget.GetGlobalPosition()`에 null 체크가 없다. `_snapTarget`이 비면 창을 움직이는
    즉시 NullReferenceException이 난다. 프리셋/gather로 코드가 position을 바꿀 때도 같은 알림 경로를 탄다.
  - `:27` `DisplayServer.Singleton.WindowGetPosition()`은 인자 없이 window id 0(root)을 읽는다.
    sub window가 다른 모니터에 있어도 root 기준으로 스냅을 계산한다.
  - `:48-53` `NotificationWMPositionChanged` 처리 중 다시 `Position`을 대입한다. 주석은 무한 루프가
    없다고 단언하지만 근거가 없고, gather/preset처럼 **코드가** position을 설정하는 W0 경로에서는
    스냅이 재진입해 프리셋 좌표를 덮어쓸 수 있다.
  - 수직 스냅은 주석만 있고 구현이 없다(`:46`).
- 영향: WorkspaceWindow가 `GameWindow`를 상속/확장하면 preset apply와 gather가 스냅 로직과 경합한다.
- 권장: `WorkspaceWindow`를 `Godot.Window` 직접 상속으로 신규 작성하고 `GameWindow`는
  `inspector.tscn` 전용으로 남긴다. Task Scope의 "optional snap 정책"(129행)은 W0 Out of Scope로 옮긴다.
  D6("창 계약은 단단하게, placeholder는 얇게")와 일치한다.

### [P2] 기존 `WindowManager` autoload와 `WorkspaceWindowManager`의 책임이 중복된다

- 실제 코드: `WindowManager`는 autoload(`project.godot:30`)이고 매 프레임 `_Process`로 root의
  최소화 상태를 폴링(`WindowManager.cs:17-40`)하며, `sub_windows` 그룹의 모든 Window에
  `Mode`를 쓰고 `previous_mode` meta를 남긴다(`:45-68`). `main.tscn:46`이 그 그룹을 사용한다.
- 영향: 제안된 `WorkspaceWindowManager`가 최소화 동기화를 함께 맡으면 같은 Window의 `Mode`를
  두 경로가 쓴다. hide된 Window에 `Mode = Minimized`를 걸거나, preset이 `mode`를 바꾼 직후
  `RestoreSubWindows()`가 `previous_mode` meta로 덮어쓰는 경합이 가능하다.
  또한 Task의 window `mode`(`hub`/`battle`/`replay`, 78·141행)와 Godot `Window.ModeEnum`이
  같은 단어를 쓰고 있어 용어 충돌이 있다.
- 권장:
  - Step 1 작업 범위에 "`WorkspaceWindowManager`가 최소화 동기화를 흡수하고 `WindowManager` autoload를
    제거한다" 또는 "`WindowManager`를 유지하고 workspace는 최소화에 관여하지 않는다" 중 하나를 명시한다.
    권장 = 흡수(단일 소유자). 폴링을 유지한다면 "Godot에 root 최소화 signal이 없어 폴링한다"는 근거를 적는다.
  - World window의 `mode`는 `content_mode`/`WorldViewMode` 등으로 개명해 `Window.ModeEnum`과 구분한다.

### [P2] Open Decision 2의 전제 확인: `config_file_handler.gd`는 재사용 대상이 아니다

- 실제 코드 `Assets/Script/gds/config_file_handler.gd`:
  - `extends Node`이지만 `project.godot`의 autoload 목록(22~34행)에 **없다**. 현재 어떤 씬도
    인스턴스화하지 않아 사실상 죽은 코드다.
  - API가 `save_video_setting`/`load_video_setting`으로 `video` 섹션에 하드코딩돼 있어 임의 섹션 확장 불가.
  - `config.load()` 반환값을 검사하지 않아(`:12`) 손상 파일에서 조용히 빈 config로 진행한다.
- 영향: Open Decision 2를 "리뷰에서 확인 후 결정"으로 남긴 채 구현에 들어가면 Step 3에서
  autoload 등록·API 확장·손상 처리까지 끌려 들어와 Step 범위가 부푼다.
- 권장: 후보 B(신규 Workspace 전용 wrapper) 확정. 저장 파일은 `user://settings.ini`를 공유하지 말고
  별도 `user://workspace_layout.cfg`로 둔다(손상 시 이 파일만 폐기해도 다른 설정을 잃지 않아
  Step 3의 "corrupt config" fallback을 안전하게 검증할 수 있다). 최상위에 `version` 키를 두고
  "미지 버전 = 기본 배치로 fallback" 규칙을 Step 3 완료 조건에 추가한다(현재 없음).

### [P2] Step 3의 작업 범위가 "결정"과 "구현"을 섞고 있다

- 실제 문서: Step 3 작업 범위 252행 "상황별 preset custom override 저장 여부 **결정 및 구현**".
- 영향: [[STEP_REVIEW_WORKFLOW]] 기본 원칙 7(구조 개선과 기능 추가를 같은 Step에 섞지 않는다)과
  Step 정의 규칙(입력이 확정돼야 완료 조건이 관찰 가능)을 위반한다. Step 3의 입력이 미정이다.
- 권장: Open Decision으로 승격한다. 권장 = W0에서는 **기본 프리셋 + 마지막 배치 1벌**만 저장하고,
  preset별 custom override는 Out of Scope로 명시한다(D3의 "저장할 수 있어야 한다"는 후속 Task).

### [P3] 검증 계획이 조건부라서 테스트 대역이 설계에 반영되지 않았다

- 실제 문서: Step 1/2 검증 방법이 "가능하면 headless 단위 테스트", "테스트 가능 여부 확인"이다(218·243행).
- 실제 코드: 이 저장소에는 확립된 C# 헤드리스 패턴이 있다 —
  `Assets/Script/Tests/*.tscn` + `Node._Ready()` 어서션 + `GetTree().Quit(0/1)`
  (CB-001/SK-001/ST-001 계열). 즉 "가능하면"이 아니라 **가능하다**.
- 영향: 현재 설계는 `WorkspaceWindowManager`가 `Window` 인스턴스를 직접 조작하므로 순수 검증 지점이 없다.
- 권장: 설계 원칙에 "clamp/gather geometry 계산과 preset resolve는 `Window`를 모르는 순수 함수/구조체
  (`Rect2I` in → `Rect2I` out)로 분리한다"를 추가한다. 그러면
  registry lookup, unknown preset/window id, off-screen clamp, tiny screen, corrupt config round-trip을
  전부 headless로 단언할 수 있고 Verification Matrix의 실패/경계 열이 실제 테스트가 된다.
  Window를 실제로 띄우는 항목(OS close, main minimize/restore, 다중 모니터)만 수동 smoke로 남긴다.

### [P3] 근거 기획서가 저장소에 없다

- Task Context 35~36행이 `06_UXUI.md` E-2/E-7, `08_데모_스코프.md` G-6을 확정 근거로 인용하지만
  두 문서는 저장소에 없다(`LLM_WIKI` 전체 검색 결과 인용만 존재).
- 영향: 리뷰어가 방향의 근거를 대조할 수 없다. AGENTS.md의 "문서와 코드가 다르면 코드를 최종 사실로 본다"
  원칙에서 벗어난 외부 의존이다.
- 권장: 근거를 `10_Architecture` 또는 `40_Decisions`(ADR)로 옮기거나, 최소한 저장소 밖 경로임을 명시한다.
  워크스페이스 방향 자체는 ADR로 남길 가치가 있다.

## 잘 된 부분

- `embed_subwindows=false`(`project.godot:48`)가 이미 설정돼 있어 네이티브 Window 방향과 충돌하지 않는다.
- Out of Scope에 "네이티브 pop-out과 임베디드 MDI를 동시에 지원하는 추상화"를 명시해 조기 일반화를 막았다.
  D6도 같은 방향이다.
- Verification Matrix가 정상/실패·경계를 분리해 열거한다. P3-1의 순수 함수 분리만 하면 그대로 테스트 목록이 된다.
- Step 0의 완료 조건이 "구현 전 결정해야 할 Open Decision이 정리되어 있다"를 포함해 이 리뷰의 목적과 일치한다.

## Open Decisions

구현 전에 사용자가 결정해야 한다.

1. **셸 호스팅 씬 / main_scene 전환**(P1-2)
   - 권장: 신규 `workspace.tscn` + 직접 실행. `run/main_scene` 전환은 별도 Task.
   - trade-off: 앱을 켜면 워크스페이스가 나오는 최종 UX 확인이 W0에서는 불가능하다. 대신 `battle_field.tscn`
     결정론 회귀(CB-001)와 완전히 분리된다.
2. **`WindowManager` autoload 처리**(P2-6)
   - 권장: `WorkspaceWindowManager`가 흡수하고 autoload 제거.
   - trade-off: `main.tscn`/`field_window.tscn`의 `sub_windows` 그룹 동작이 사라진다. 두 씬 모두 실험 자산이라
     수용 가능하다고 본다.
3. **배치 저장 위치**(Task Open Decision 2 → P2-7로 해소)
   - 권장: 후보 B, 신규 `user://workspace_layout.cfg` + `version` 키. `config_file_handler.gd`는 재사용하지 않는다.
4. **preset별 custom override 저장 여부**(P2-8)
   - 권장: W0 Out of Scope. 마지막 배치 1벌만 저장.
5. **Root window의 managed 범위**(P2-4)
   - 권장: registry 등록 + position/size 저장 + gather 대상, `visible` 적용 제외.
6. **WorkspaceWindow 기반**(P2-5)
   - 권장: `Godot.Window` 직접 상속 신규 작성. `GameWindow`/스냅은 W0 Out of Scope.

Task Open Decision 1(C# 중심)과 4(임시 배치만)는 그대로 타당하다. C# 중심 결론은 유지하되,
근거를 "기존 `WindowManager.cs`/`GameWindow.cs`가 C#"에서 "테스트 자산이 C# 헤드리스 패턴
(`Assets/Script/Tests`)이라 registry/geometry 단위 테스트를 같은 언어로 붙일 수 있다"로 보강하는 편이 강하다.

## Step Assessment

| Step | 입력 | 출력 | 선행 조건 | 완료 조건 평가 |
| --- | --- | --- | --- | --- |
| 0 Design Review | Task 문서, 기존 코드 | 이 문서 | 없음 | 충족. 단 Open Decision 6건이 남아 승인 조건이 된다 |
| 1 Skeleton | Open Decision 1·2·5·6 | registry + 5창 + hide 정책 | display 설정 변경(P1-1), 호스팅 씬 결정(P1-2) | "메인 창 최소화/복원" 조건이 현재 설정에서 관찰 불가 → P1-1 해소 필요 |
| 2 Preset + Gather | Step 1, safe-area 계약(P1-3) | preset apply, gather | Root 포함 여부(P2-4) | "보이는 영역"이 미정의 → P1-3 해소 전에는 판정 불가 |
| 3 Persistence | Step 2, 저장 위치·override 결정 | config round-trip | P2-7, P2-8 | "결정 및 구현" 혼재. version/미지 버전 fallback 조건 누락 |
| 4 Docs | Step 1~3 | System 문서 + 완료 리뷰 | 없음 | 타당. `20_Systems/Workspace-Window-System.md` 신규가 적절 |

Step 분할 자체는 적절하다. 합치거나 나눌 필요는 보이지 않는다. 다만 Step 1에
`project.godot` display 변경이 들어가면 그것은 "기능 추가와 무관한 설정 변경"이므로
Step 1 안에서 별도 커밋으로 분리하고 기존 씬 회귀(수동 실행 1회)를 함께 보고하는 편이 좋다.

## Verification Assessment

현재 계획에 빠진 것:

- **테스트 대역 부재.** P3-1대로 geometry/preset resolve를 순수 함수로 분리하지 않으면
  Verification Matrix의 `unknown preset`, `unknown window id`, `off-screen`, `tiny screen`,
  `corrupt config`, `invalid type`, `stale monitor coordinates`는 전부 수동 검증으로 밀린다.
- **repeated registration / freed window.** Matrix에 있지만 Step 1 완료 조건에는 대응 항목이 없다.
  hide 정책을 쓰면 free는 안 되지만, 씬 전환이나 `queue_free` 오사용에 대한 registry의 방어
  (`IsInstanceValid` 확인 후 stale entry 제거)를 Step 1 완료 조건에 넣어야 한다.
- **duplicate open.** `show_window(id)`를 이미 보이는 창에 호출했을 때 position이 리셋되지 않아야 한다는
  기대가 명시돼 있지 않다.
- **저장 파일 미존재 first-run.** Step 3 완료 조건은 corrupt만 다루고, 파일이 아예 없는 첫 실행에서
  기본 프리셋이 적용되는지를 다루지 않는다.
- **`dotnet build` 경고 0.** 기존 Step 리뷰(CB-001/SK-001/ST-001)는 `dotnet build` 경고/오류 0과
  `--import` exit 0을 관례적으로 보고한다. Step 1~3 검증 방법에 `--import`를 추가하면 일관된다.

권장 테스트 자산(기존 패턴 그대로):

- `ws001_step1_registry_test.tscn` — registry 등록/중복/stale, close→hide 정책, unknown id fail-closed.
- `ws001_step2_geometry_test.tscn` — clamp/gather 순수 함수(off-screen, tiny screen, 데코 포함 rect),
  preset resolve(unknown preset/window id).
- `ws001_step3_config_roundtrip_test.tscn` — save→load 일치, corrupt/invalid type/미지 version fallback,
  first-run 기본 배치.

수동 smoke로만 남길 항목: OS close 버튼, root 최소화/복원 동기화, 실제 다중 모니터 좌표.

## 검증 결과

- 통과(정적 대조): `project.godot` display/autoload/main_scene, `WindowManager.cs`, `GameWindow.cs`,
  `config_file_handler.gd`, `main.tscn`, `inspector.tscn`, `field_window.tscn` 실제 내용 확인.
- 통과: `godot --headless --path . --import` exit 0(기존 프로젝트가 임포트 자체는 성공).
- 실행하지 못함: `field_window.tscn`의 자기 참조가 런타임에 실제로 재귀/오류를 내는지는
  씬을 로드하지 않아 확인하지 못했다(정적 파일 내용만 근거). 설계 리뷰 범위상 씬 실행/수정은 하지 않았다.
- 실행하지 못함: `06_UXUI.md`, `08_데모_스코프.md` 대조(저장소에 없음).

## Verdict

**Approved after design fixes**

P1 3건은 모두 코드 결함이 아니라 Task 문서의 누락/사실 오류이며 문서 수정으로 해소된다.
아래를 반영한 뒤 Step 1 구현으로 넘어간다.

1. P1-1: Step 1 작업 범위에 `project.godot` display 설정 변경 추가.
2. P1-2: 셸 호스팅 씬 Open Decision 추가 + main scene이 `battle_field.tscn`이라는 사실 반영.
3. P1-3: safe-area/gather 계약(스크린 선택, 데코 포함 rect, 교차 임계치, clamp 규칙) 명문화.
4. P2-4~8: Root window 범위, Context의 기존 자산 사실 교정, `WorkspaceWindow` 신규 작성,
   `WindowManager` autoload 처리, 저장 파일·version 정책, Step 3 결정/구현 분리.
5. P3-1: geometry/preset resolve 순수 함수 분리를 설계 원칙(D7)으로 추가하고 Step 1~3 검증 방법을
   headless 테스트 3종 + `--import` + `dotnet build`로 확정.

P3-2(기획서 부재)는 구현을 막지 않으나, 워크스페이스 방향을 ADR로 남길 것을 권한다.

## Related

- [[WS-001-Native-Window-Workspace-Shell]]
- [[STEP_REVIEW_WORKFLOW]]
- [[Open-Tasks]]
- [[Current-State]]

## Design Fixes Applied

2026-07-08 Task 문서 [[WS-001-Native-Window-Workspace-Shell]]에 리뷰 지적을 반영했다.

- P1-1: 현재 `project.godot` display 설정상 메인 창 최소화/복원은 관찰 불가임을 Context/Open Decisions/Step 1 완료 조건에 반영했다. display 설정 변경은 Step 1 전 명시 승인 대상으로 남겼다.
- P1-2: 현재 main scene이 `Assets/Scenes/Map/battle_field.tscn`임을 반영하고, 셸 entry scene/launch path 결정을 Open Decision으로 추가했다.
- P1-3: `gather_windows()` safe-area 계약을 decoration 포함 rect와 `GetPositionWithDecorations()` 우선 기준으로 명문화했다.
- P2: `field_window.tscn`, `inspector.tscn`, `GameWindow.cs`, `config_file_handler.gd`의 현재 한계를 반영하고, 신규 `WorkspaceWindow`/`WorkspaceWindowManager` 권장으로 수정했다.
- P2: Root/Main은 managed Window set에서 제외하고, Step 1의 5종은 `World`/`Situation`/`WeeklyAction`/`Calendar`/`Log`임을 명확히 했다.
- P3: geometry/preset resolve 순수 함수 분리를 D7 원칙과 검증 계획에 추가했다.

수정 후 판정: **Approved after design fixes 반영 완료. Step 1 구현 진입 가능.**

## Additional Fixes Applied

2026-07-08 남은 P2/P3 문서 항목을 추가 반영했다.

- Step 3의 "상황별 preset custom override 저장 여부 결정 및 구현"은 Open Decision 7로 올리고 W0 Out of Scope 권장으로 정리했다. Step 3은 기본 preset/마지막 창 배치 저장, first-run, corrupt/unknown version fallback에 집중한다.
- `gather_windows()`는 대상 screen 선택 규칙(데코 포함 rect 중심이 포함된 screen, 없으면 primary)과 최소 교차 임계치(가로 64px, 세로 title-bar 추정 32px), clamp/축소 규칙을 명시했다.
- Step 3 완료 조건에 first-run missing config와 unknown/unsupported version fallback을 추가했다.
- 기존 `WindowManager` autoload는 WS-001 Step 1에서 변경하지 않고, 신규 `WorkspaceWindowManager`를 셸 entry scene 하위 노드로 두는 정책을 명시했다.
- snap UX는 W0 Out of Scope로 분리했다.
- Step 1 완료 조건에 duplicate open, repeated registration, freed/stale window registry 방어를 추가했다.

추가 수정 후에도 판정은 **Approved after design fixes 반영 완료. Step 1 구현 진입 가능.**

## Final Minor Fixes Applied

2026-07-08 Step 1을 막지 않는 P3 3건도 문서에 반영했다.

- workspace managed Window는 기존 `WindowManager` autoload와 충돌하지 않도록 `sub_windows` 그룹에 등록하지 않는다고 Step 1 작업 범위에 명시했다.
- OD7은 "결정: 후보 B"로 정리해 custom preset override가 W0 범위 밖임을 Out of Scope와 일치시켰다.
- `field_window.tscn` 자기 참조/비-Window 루트 정리는 WS-001 범위 밖 cleanup 후속으로 [[Open-Tasks]]에 남겼다.

## Step 1 Gate Decisions

2026-07-08 Step 1 착수 게이트였던 Open Decision 2건을 사용자가 확정했다.

- **OD5 = 후보 B.** 셸 entry scene은 신규 `Assets/Scenes/workspace.tscn`이며 직접 실행으로 검증한다.
  `run/main_scene`은 `battle_field.tscn`으로 유지하므로 CB-001 결정론 씬 경로는 흔들리지 않는다.
- **OD6 = 후보 A.** Step 1에서 `project.godot` display 설정을 `resizable=true`,
  `minimize_disabled=false`, `maximize_disabled=false`로 변경한다. 전역 blast radius가 있으므로
  기능 구현과 별도 커밋으로 분리하고, `battle_field.tscn` 실행 회귀 확인을 Step 1 완료 조건에 포함한다.
  프로젝트에 stretch mode 설정이 없어(`display/window/stretch/*` 미지정) root window 크기 변경이
  viewport 표시에 직접 노출된다는 점이 이 회귀의 핵심 관찰 지점이다.
- 이 결정으로 D5의 메인 창 최소화/복원 동기화가 Step 1의 관찰 가능한 완료 조건이 되었다. 다만
  기존 `WindowManager` autoload는 변경하지 않으므로, 동기화는 신규 `WorkspaceWindowManager`가
  자기 registry를 기준으로 수행하고 managed window는 `sub_windows` 그룹에 등록하지 않는다.

**Step 0 종료. 미결 Open Decision 없음. Step 1 구현 진입 가능.**

## Step 1 코드 리뷰

검토 범위: `WorkspaceWindow.cs`, `WorkspaceWindowManager.cs`, `WorkspaceShell.cs`, `workspace.tscn`, `Ws001Step1RegistryTest.cs`, `ws001_step1_registry_test.tscn`, `project.godot`, Step 1 구현 결과.

제품 코드 수정 없음. 리뷰와 검증만 수행했다.

## Findings

### [P3] WS-001 registry test가 성공하면서도 Godot ERROR 로그를 남긴다

- 조건: `ws001_step1_registry_test.tscn` 실행.
- 실제 코드: `WorkspaceWindowManager.RegisterWindow()`는 빈 `WindowId`와 중복 id를 거부할 때 `GD.PushError`를 호출한다(`WorkspaceWindowManager.cs:70`, `:79`). 테스트는 이 실패 경로를 정상 케이스로 검증한다(`Ws001Step1RegistryTest.cs:80`, `:94`).
- 영향: 테스트 exit code는 0이고 assertion은 통과하지만, 콘솔에는 `ERROR: [WS-001] RegisterWindow...`가 출력된다. 이 저장소의 여러 회귀는 "SCRIPT ERROR 없음"을 중요한 신뢰 신호로 삼기 때문에, 향후 로그 기반 CI/수동 판독에서 실패처럼 보일 수 있다.
- 권장: 제품 코드의 사용자 오류 보고로 `PushError`를 유지할지, 테스트에서 예상 오류 로그를 허용한다고 명시할지 결정한다. 더 깔끔한 선택은 fail-closed API가 구조화된 failure를 반환하고, 테스트 fixture에서는 ERROR 로그 없이 실패 사유를 단언하는 것이다. Step 1을 막지는 않는다.

## 검증 결과

- `dotnet build AutoCrawler.sln -c Debug`: PASS, 경고 0 / 오류 0.
- Godot `--headless --path . --import`: PASS, exit 0. 기존 editor layout 경고와 ObjectDB/resource shutdown noise는 남음.
- `ws001_step1_registry_test.tscn`: PASS, ALL PASS(62 assertions), exit 0. 단 위 P3처럼 expected failure path의 `GD.PushError` 로그가 출력됨.
- `workspace.tscn --quit-after 1` headless smoke: PASS, exit 0. 기존 `WindowManager` autoload의 headless minimize 로그 출력 확인.
- `battle_field.tscn --quit-after 1` headless smoke: PASS, exit 0. 기존 `WindowManager` autoload의 headless minimize 로그 출력 확인.
- CB-001 확인: `cb001_step1_turn_effect_test.tscn` PASS. `cb001_step4_determinism_test.tscn` FAIL(`D.deaths_recorded -> got False, expected True`)를 재현했다. 이는 WS-001과 별도 회귀로 보인다.

## 검증하지 못한 항목

- 실제 데스크톱 GUI에서 `workspace.tscn` 창 5종 open/hide/show/move/resize, OS close, root minimize/restore 동작.
- display 설정 변경 후 `battle_field.tscn` 창 크기 변경/최대화 시각 회귀.
- 실제 멀티모니터/작업표시줄 동작.

## 판정

**수정 후 완료가 아니라, Step 1 코드 기준 완료 가능.**

P0/P1/P2는 발견하지 못했다. P3 테스트 로그 노이즈와 수동 GUI smoke 공백은 남지만, Step 1의 핵심 코드/헤드리스 검증은 설계 조건을 충족한다.

## Step 1 처리 결과

### [P3] 테스트가 통과하면서도 ERROR 로그를 남긴다 - 수정

- 변경 내용: `GD.PushError`/`GD.PushWarning` 자체는 유지했다. 빈 `WindowId`와 중복 id는 실제 배선 실수이고,
  프로덕션에서 조용히 삼키면 창이 뜨지 않는 원인을 추적할 수 없다. 대신 테스트가 기대하는 진단 로그를
  `ExpectDiagnosticLog(reason, body)` helper로 감싸 `EXPECTED-DIAGNOSTIC-BEGIN/END` 마커를 남긴다
  (`Assets/Script/Tests/Ws001Step1RegistryTest.cs`, 케이스 B/C/I).
- 회귀 방지 방법: 로그 판독기는 마커 구간 밖의 `ERROR`/`WARNING`만 실패로 취급하면 된다. 새 fail-closed
  경로를 테스트할 때도 같은 helper를 쓴다.
- 검증: `dotnet build` 경고/오류 0. `ws001_step1_registry_test` ALL PASS(62 assertions), exit 0.
  출력의 `ERROR`/`WARNING` 라인 3건이 모두 마커 구간 안에 있고, 마커 밖 진단 로그는 0건이다.

### 검증 공백 - 수동 smoke로 닫음

- **[P1, 수동 GUI smoke에서 발견] 하드코딩 절대좌표 때문에 title bar가 화면 밖에 남아 창을 이동·닫을 수 없었다.**
  - 조건: 모니터 3개, `screen[0] pos=(0, 542)`. `workspace.tscn`이 준 `position = Vector2i(40, 80)`은
    어떤 screen에도 속하지 않는 가상 좌표다.
  - 실제 동작: Godot이 **client rect만** 화면 안으로 클램프하고 decoration은 남긴다.
    `world`는 `pos=(40, 542)`로 올라갔지만 `decoPos=(32, 511)`이라 title bar가 화면 밖이었다.
    `y = 600`이라 클램프가 걸리지 않은 `calendar`만 정상이었고, `situation`은 `(700, 80)` → `(1920, 774)`로
    다른 모니터에 튕겼다. 이는 Step 0 리뷰 P1-3이 예고한 실패 그대로다.
  - 수정: `workspace.tscn`의 창 좌표 하드코딩을 제거하고 `initial_position = 2`(CenterMainWindowScreen)로
    바꿨다. Step 2 gather를 앞당기지 않으면서 title bar가 usable rect 안에 들어온다.
  - 검증: 진단 probe(`ws001_window_geometry_probe`) 재실행 결과 5창 모두 main window의 screen
    (usable `(1920,774)~(3840,1806)`) 안. 최상단 `log`의 `decoPos.y = 1003`.
    `ws001_step1_registry_test` 62 assertions ALL PASS 유지, workspace 부팅 exit 0.
  - Step 2 반영: 실측 두 가지(screen 원점 ≠ (0,0), 클램프가 decoration 무시)를 Task의
    `Safe Work Area and Gather Contract`에 근거로 명시했다.

- **display 설정 변경 후 `battle_field.tscn` 회귀: 예상된 결과, 크래시 없음.**
  창을 키우거나 최대화하면 전투 화면이 따라 커지지 않고 빈 공간만 생긴다(`display/window/stretch/*` 미설정).
  OD6 조항에 따라 stretch mode 도입 여부를 후속 결정으로 [[Open-Tasks]]에 올렸다. Step 2를 막지 않는다.

### 남은 검증 공백

- root minimize/restore의 실제 OS 동작(작업표시줄, 멀티모니터)은 아직 수동으로 확인하지 않았다.
  헤드리스 테스트는 manager API 수준만 단언한다.

### 범위 밖 - 별도 Task

- `cb001_step4_determinism_test`의 `D.deaths_recorded` 실패는 WS-001 이전부터 존재한다(변경 stash 후에도 재현).
  `Current-State.md`의 "cb001_step1~4 전부 ALL PASS" 서술과 어긋난다.

## Step 1 판정

**수정 후 완료.** P3 1건 수정·재검증 완료, P0/P1/P2 없음. 남은 것은 수동 GUI smoke 공백뿐이며 Step 2를 막지 않는다.

## Step 2 코드 리뷰

검토 범위: `WorkspaceGeometry.cs`, `WorkspaceLayoutPreset.cs`, `WorkspaceWindowManager.cs`,
`WorkspaceWindow.cs`, `WorkspaceShell.cs`, `workspace.tscn`, `Ws001Step2GeometryTest.cs`,
`ws001_step2_geometry_test.tscn`, `Ws001WindowGeometryProbe.cs`, Step 2 구현 결과.

제품 코드 수정 없음. 리뷰와 검증만 수행했다.

## Findings

### [P3] Step 2 expected failure path도 Godot ERROR 로그를 남긴다

- 조건: `ws001_step2_geometry_test.tscn` 실행.
- 실제 코드: `WorkspaceWindowManager.ApplyPreset()`은 unknown preset을 fail-closed로 거부하면서
  `GD.PushError`를 호출한다(`WorkspaceWindowManager.cs:168`). 테스트는 이 실패 경로를 정상 케이스로
  검증한다(`Ws001Step2GeometryTest.cs:246-250`).
- 영향: 테스트 exit code는 0이고 assertion은 통과하지만 콘솔에는 `ERROR: [WS-001] ApplyPreset...`가
  출력된다. Step 1과 같은 로그 위생 이슈이며, 마커 구간 안의 expected diagnostic으로 판독하면 된다.
- 권장: Step 1에서 정한 정책을 그대로 적용한다. 구조적으로 더 깔끔하게 하려면 fail-closed API가
  실패 사유를 반환하고 테스트는 ERROR 로그 없이 그 값을 단언하게 만든다. Step 2를 막지는 않는다.

## 검증 결과

- `dotnet build AutoCrawler.sln -c Debug`: PASS, 경고 0 / 오류 0.
- Godot `--headless --path . --import`: PASS, exit 0. 기존 editor layout 경고와 ObjectDB/resource shutdown noise는 남음.
- `ws001_step2_geometry_test.tscn`: PASS, ALL PASS(66 assertions), exit 0. unknown preset/unknown window 진단 로그는 expected diagnostic 마커 구간 안에서만 출력됨.
- `ws001_step1_registry_test.tscn`: PASS, ALL PASS(62 assertions), exit 0.
- `workspace.tscn --quit-after 1` headless smoke: PASS, exit 0. 기존 `WindowManager` autoload의 headless minimize 로그 출력 확인.

## 검증하지 못한 항목

- 실제 데스크톱 GUI에서 preset 버튼 3종과 `창 모아오기` 버튼을 직접 클릭하는 수동 smoke.
- 모니터 해제/추가 후 stale monitor coordinates 회수. 이는 Step 3 저장/복원 검증과 함께 다룬다.
- 숨겨진 창의 decoration fallback 값이 저장/복원 round-trip에서 실제 표시 시점의 decoration 값과 얼마나 차이 나는지.

## Step 2 판정

**완료 가능.** P0/P1/P2는 발견하지 못했다. `WorkspaceGeometry` 순수 함수 분리, decoration 포함 rect 계약,
target screen 선택, preset 적용 순서, World 창 instance 유지, off-screen gather 회수 모두 Step 2 완료 조건과 맞다.
남은 P3 로그 노이즈와 수동 GUI/Step 3 리스크는 다음 Step 진입을 막지 않는다.

## Step 2 처리 결과

### [P3] unknown preset fail-closed 경로가 ERROR 로그를 남긴다 - 보류(현행 유지)

- 보류 이유(범위·위험): `GD.PushError`를 제거하거나 낮추면 프로덕션에서 잘못된 preset id를 조용히 무시하게 된다.
  preset 버튼 배선 실수나 오타는 "창이 하나도 안 움직인다"로만 드러나고, 로그가 없으면 추적할 수 없다.
  진단을 죽이는 대신 Step 1에서 도입한 `ExpectDiagnosticLog` 마커를 Step 2 테스트에도 적용해 두었다
  (`Ws001Step2GeometryTest.TestApplyUnknownPresetFailsClosed`).
- 영향 범위: 테스트 콘솔 출력만. exit code와 assertion에는 영향이 없다.
- 재확인(2026-07-08): `ws001_step1_registry_test`와 `ws001_step2_geometry_test`의 모든 `ERROR`/`WARNING`
  라인이 `EXPECTED-DIAGNOSTIC-BEGIN/END` 구간 **안에** 있고, 마커 밖 진단 로그는 0건이다.
  따라서 로그 판독 규칙은 "마커 구간 밖의 ERROR/WARNING만 실패"로 확정한다.
- 처리할 후속 Step: 없음. 새 fail-closed 경로를 추가하는 Step 3도 같은 helper를 쓴다.

## Step 2 최종 판정

**완료.** P0/P1 없음. P3 1건은 위 근거로 현행 유지하고 로그 판독 규칙을 확정했다. Step 3 진입 가능.

## Step 3 코드 리뷰

검토 범위: `WorkspaceLayoutStore.cs`, `WorkspaceWindowManager.cs`, `WorkspaceShell.cs`,
`workspace.tscn`, `Ws001Step3PersistenceTest.cs`, `ws001_step3_persistence_test.tscn`, Step 3 구현 결과.

제품 코드 수정 없음. 리뷰와 검증만 수행했다.

## Findings

### [P3] corrupt config 테스트와 실제 손상 파일 로드가 Godot engine ERROR 로그를 남긴다

- 조건: `WorkspaceLayoutStore.Load()`가 파싱 불가 config를 `ConfigFile.Load()`로 읽을 때.
- 실제 코드: `ConfigFile.Load(_path)` 호출 지점은 `WorkspaceLayoutStore.cs:113`이고, 테스트는 파싱 불가 파일을 만들어 이 경로를 정상 fallback으로 검증한다(`Ws001Step3PersistenceTest.cs:125-133`).
- 영향: 제품 동작은 안전하다. `Load()`는 예외 없이 `Corrupt` 또는 `UnsupportedVersion` fallback으로 흐르고 파일도 파괴하지 않는다. 다만 Godot engine이 먼저 `ERROR: ConfigFile parse error...`를 출력하므로, Step 1/2의 expected `GD.PushError`/`PushWarning`보다 로그 판독이 더 시끄럽다. 테스트 exit code와 assertion에는 영향이 없다.
- 권장: 현 Step에서는 허용 가능하다. 로그 기반 CI를 붙일 때는 `ws001_step3_persistence_test`의 corrupt config 구간을 expected diagnostic으로 분류하거나, 추후 정말 조용한 fallback이 필요하면 `ConfigFile.Load()` 전에 파일 텍스트를 별도 pre-validate하는 얇은 wrapper를 검토한다.

## 검증 결과

- `dotnet build AutoCrawler.sln -c Debug`: PASS, 경고 0 / 오류 0.
- Godot `--headless --path . --import`: PASS, exit 0. 기존 editor layout 경고와 ObjectDB/resource shutdown noise는 남음.
- `ws001_step3_persistence_test.tscn`: PASS, ALL PASS(40 assertions), exit 0. store round-trip, first-run, corrupt/unknown version fallback, malformed window skip, manager save/load 재시작 시뮬레이션, default preset fallback, stale 좌표 gather, size <= 0 skip 확인.
- `ws001_step2_geometry_test.tscn`: PASS, ALL PASS(66 assertions), exit 0.
- `ws001_step1_registry_test.tscn`: PASS, ALL PASS(62 assertions), exit 0.
- `workspace.tscn --quit-after 1` headless smoke: PASS, exit 0.
- `battle_field.tscn --quit-after 1` headless smoke: PASS, exit 0.
- 회귀 확인: `cb001_step1_turn_effect_test`, `cb001_step2_combat_rng_test`, `cb001_step3_canonical_order_test`, `st001_step1_natural_regen_test` PASS.
- 참고: 처음에 `cb001_step2_rng_test.tscn`, `cb001_step3_tiebreak_test.tscn`으로 잘못 실행해 scene not found가 났다. 실제 파일명 확인 후 위의 올바른 테스트를 재실행해 PASS를 확인했다.

## 검증하지 못한 항목

- 실제 데스크톱 GUI에서 root close 버튼/Alt+F4가 `CloseRequested -> SaveLayout -> Quit`으로 저장되는지.
- 실물 모니터 해제/추가 후 재시작. 자동 테스트는 강제 off-screen 좌표로 stale monitor 상황을 대체 검증했다.
- 숨겨진 창 저장 시 fallback decoration inset 때문에 생길 수 있는 몇 px 좌표 오차.

## Step 3 판정

**완료 가능.** P0/P1/P2는 발견하지 못했다. `WorkspaceLayoutStore`의 version/타입 검증, 파일 보존 fallback,
manager save/load round-trip, first-run 기본 preset, load 후 gather 회수는 Step 3 완료 조건을 충족한다.
남은 P3 로그 노이즈와 실제 OS close 수동 smoke 공백은 Step 4 문서화/완료 리뷰로 넘겨도 된다.

## Step 3 처리 결과

### [P3] 손상 config 로드 시 Godot 엔진이 `ERROR: ConfigFile parse error`를 출력한다 - 보류(현행 유지)

- 보류 이유(범위·위험): 이 ERROR는 `WorkspaceLayoutStore.Load()`의 `push_error`가 아니라 **Godot 엔진의
  `ConfigFile.Load()`가 파싱 실패 시 내부에서 직접 찍는 것**이라, Step 1/2의 `GD.PushError`처럼 호출부에서
  억제하거나 문구를 바꿀 수 없다. 억지로 없애려면 `ConfigFile`을 안 쓰고 파서를 자작해야 하는데, 손상 감지의
  이득 대비 비용이 크고 Step 3 범위를 벗어난다.
- 영향 범위: 테스트 콘솔 출력만. Load는 이 오류를 `Error.ParseError`로 받아 Corrupt fallback으로 흐르고,
  파일은 보존되며, 테스트는 통과한다.
- 재확인(2026-07-08): 이 엔진 ERROR 라인(테스트 출력 19행)은 `ExpectDiagnosticLog` 마커 구간
  (18~44행) **안에** 있다. 따라서 이미 확정한 로그 판독 규칙("마커 구간 밖의 ERROR/WARNING만 실패")으로
  그대로 걸러진다. Step 1/2와 유일한 차이는 "억제 불가 엔진 로그"라는 점이며, 마커로 관리된다는 결론은 같다.
- 처리할 후속 Step: 없음.

## Step 3 최종 판정

**완료.** P0/P1 없음. P3 1건은 억제 불가 엔진 로그로 현행 유지하고 마커 구간 안에 있음을 재확인했다.
Step 4(문서화/완료 리뷰) 진입 가능.

## Step 4: 문서화 및 완료 리뷰

제품 코드 변경 없음(문서 단계). 갱신한 문서:

- `20_Systems/Workspace-Window-System.md`(신규): W0의 현재 사실(managed set, lifecycle, preset, gather 계약, persistence, 설계 경계, 검증)을 System 문서로 보존.
- `00_Index/Current-State.md`: Workspace 절 추가(WS-001 W0 완료 사실).
- `00_Index/Open-Tasks.md`: WS-001을 "Step 2 대기"에서 "W0(Step 1~3) 완료"로 갱신, 후속 창 장착 작업을 별도 항목으로 분리.
- `00_Index/Home.md`: `Workspace-Window-System`, `WS-001 ...-Review` 링크 추가.

### WS-001 전체 완료 조건 대조

Task의 최상위 Goal과 각 Step 완료 조건을 실제 검증 결과와 대조한다.

| 완료 조건 | 근거 | 판정 |
| --- | --- | --- |
| managed Window 5종을 독립적으로 열고 이동/크기 변경 | 실제 GUI 실행 + `ws001_step1`(K 씬 배선) | 충족 |
| 닫기 = hide, 메뉴에서 재표시 | `ws001_step1`(H), 셸 toggle 역동기화 | 충족 |
| duplicate open 시 좌표 보존, freed/stale registry 방어 | `ws001_step1`(G/I) | 충족 |
| 메인 창 최소화/복원 동기화(숨긴 창 유지) | `ws001_step1`(J) + display 설정 해제 | 충족(실제 OS 최소화는 수동 smoke 잔여) |
| preset이 visible/position/size/content_mode 적용, World 재생성 없음 | `ws001_step2`(H/I) | 충족 |
| 화면 밖 좌표를 gather로 회수, 타이틀바가 화면 밖에 안 남음 | `ws001_step2`(C~G/L) + GUI probe | 충족 |
| 다중 모니터 대상 screen 선택 | `ws001_step2`(A/B) + 실측 3모니터 probe | 충족 |
| 이동 후 재시작 배치 복원 | `ws001_step3`(F) + GUI save/load probe | 충족(실제 OS close 저장은 수동 smoke 잔여) |
| first-run 기본 preset, corrupt/version fallback, 파일 보존 | `ws001_step3`(C/D/E/G/I) | 충족 |
| 저장 좌표가 화면 밖이면 회수 | `ws001_step3`(H) | 충족 |
| placeholder content only(scope boundary) | 모든 창이 Label placeholder, gameplay/SaveGame/BT 미연결 | 충족 |

### 회귀 및 알려진 사실

- WS-001 헤드리스 3종(62+66+40 = 168 assertions) ALL PASS. `dotnet build` 경고/오류 0, `--import` exit 0.
- `battle_field.tscn`은 display 설정 해제 후에도 부팅 exit 0. 창 확대 시 stretch 미설정으로 빈 공간이 생기는 것은
  예상된 결과이며 후속 결정으로 [[Open-Tasks]]에 있다.
- **WS-001과 무관한 기존 회귀 2건**: `cb001_step4_determinism_test`(`D.deaths_recorded`),
  `sk001_step6_data_pack_test`(`D.ChainHitsThree`). `project.godot` 되돌린 baseline에서도 재현되며 각각 별도
  Task로 분리했다. `Current-State.md`의 해당 ALL PASS 서술은 이 두 Task에서 재검증·정정한다.

### 수동 smoke 잔여(헤드리스로 관찰 불가)

- root close 버튼/Alt+F4 → `CloseRequested → SaveLayout → Quit` 저장.
- 실물 모니터 해제/추가 후 재시작(자동 테스트는 강제 off-screen 좌표로 대체).
- root 최소화/복원의 실제 작업표시줄 동작.

## 최종 판정

**WS-001 W0(Step 0~3) 완료.** 설계 리뷰(Approved after design fixes)부터 Step 1~3 구현·리뷰까지 모든 P0/P1이
없고, P2는 설계 단계에서 문서 수정으로 해소, P3는 로그 위생 1계열로 마커 규칙으로 관리된다. 완료 조건 전 항목이
헤드리스 테스트 또는 GUI probe로 충족됐다. 남은 것은 수동 GUI smoke 3건과 W0 범위 밖 후속(콘텐츠 장착·custom
override·스냅·stretch)뿐이며, 모두 [[Open-Tasks]]에 있다.

## Step 4 리뷰 보강

검토 범위: `LLM_WIKI/20_Systems/Workspace-Window-System.md`, `LLM_WIKI/00_Index/Current-State.md`,
`LLM_WIKI/00_Index/Open-Tasks.md`, `LLM_WIKI/00_Index/Home.md`, `LLM_WIKI/30_Tasks/WS-001-Native-Window-Workspace-Shell.md`,
이 Review 문서의 Step 4 섹션.

제품 코드 수정 없음. 문서 정적 대조만 수행했다.

## Findings

### [P3] `Current-State.md`에 기존 CB/SK 회귀와 충돌하는 ALL PASS 서술이 아직 남아 있다

- 조건: 새 세션의 agent가 [[Current-State]]만 읽고 현재 회귀 기준을 판단할 때.
- 실제 문서: `Current-State.md`는 여전히 `cb001_step1~4` 전부 ALL PASS와 `sk001_step6_data_pack_test` ALL PASS 계열 서술을 포함한다(`Current-State.md:64`, `:104`, `:111` 등). 반면 이 Review는 WS-001과 무관한 기존 회귀로 `cb001_step4_determinism_test`(`D.deaths_recorded`)와 `sk001_step6_data_pack_test`(`D.ChainHitsThree`)를 기록한다.
- 영향: WS-001 완료 판정 자체에는 영향이 없다. 다만 `Current-State`가 현재 사실의 진입점이라, 후속 agent가 CB/SK 회귀를 놓치거나 잘못된 baseline으로 판단할 수 있다.
- 권장: 별도 CB/SK 회귀 Task에서 정정한다는 방침은 타당하다. 그 전까지는 `Current-State.md`의 Combat/Skill 상단에 "2026-07-09 기준 cb001_step4/sk001_step6 기존 회귀 있음" 같은 짧은 Known Regression 주석을 추가하면 혼선을 줄일 수 있다. Step 4 완료를 막지는 않는다.

## 검증 결과

- `Home.md`: `Workspace-Window-System`, `WS-001 ... Review` 링크 추가 확인.
- `Workspace-Window-System.md`: managed set, lifecycle, preset, decoration-aware gather, persistence, W0 범위 밖 항목, 검증 자산이 현재 코드 구조와 일치함.
- `Open-Tasks.md`: WS-001 W0 완료와 후속 창 장착/stretch/field_window cleanup 분리 확인.
- `WS-001 Task`: `status: complete`, Step 4 구현 결과와 완료 요약 확인.
- `WS-001 Review`: Step 1~3 처리 결과, Step 4 전체 완료 조건 대조, 최종 판정 확인.

## Step 4 판정

**완료 가능.** WS-001 관련 문서화와 후속 작업 분리는 완료 조건을 충족한다. 남은 P3는 WS-001 범위 밖 기존 회귀의
Current-State 표기 문제이며, 별도 CB/SK 회귀 Task에서 정정해도 된다.

## Step 4 처리 결과

### [P3] Current-State.md의 stale ALL PASS 서술 - 부분 수정(배너 추가)

- 조건: 새 세션이 `Current-State.md`만 읽으면 `cb001_step4_determinism_test`·`sk001_step6_data_pack_test`
  기존 회귀를 놓친다(본문 64·81·98·101·104행 근처가 아직 "CB-001 step1~4 ALL PASS", "sk001_step6 ALL PASS").
- 처리: 본문 개별 서술 정정은 합의대로 CB/SK 회귀 Task 소관으로 남기고, `Current-State.md` **최상단에 Known
  Regressions 배너**를 추가했다. 두 실패 테스트, WS-001 무관(baseline 재현), 별도 Task 분리, 정정 대상 행을
  명시한다. 배너로 "먼저 읽고 놓친다"는 위험을 즉시 닫는다.
- 남긴 것: 본문 행별 정정은 원인 규명이 필요하므로 그 Task에서 배너와 함께 제거한다. 리뷰어 권고("짧은 Known
  Regression 주석")와 일치한다.

## WS-001 최종 완료 확인

Step 0~4 전 단계 판정 완료. P0/P1 없음, P2는 설계 단계 문서 수정으로 해소, P3는 (Step 1/2/3 로그 위생 마커
규칙 + Step 4 Known Regressions 배너)로 관리된다. **WS-001 W0 완료.**
