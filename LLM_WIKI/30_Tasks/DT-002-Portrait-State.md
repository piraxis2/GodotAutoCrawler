---
id: DT-002
type: task
status: completed
system: DialogueTool
created: 2026-06-11
updated: 2026-06-11
tags: [task, dialogue-tool, portrait]
---

# Portrait State

## Goal

Portrait 연출을 Say에 강제 결합하지 않고, 대화 UI의 느슨한 지속 상태로 제공한다.

## Proposed Model

```gdscript
{
    "left": {
        "actor": "noabel",
        "expression": "normal",
        "visible": true
    },
    "right": {
        "actor": "princess",
        "expression": "angry",
        "visible": true
    }
}
```

## Candidate Nodes

- Portrait Show: slot, actor, expression, transition
- Portrait Hide: slot 또는 actor, transition
- Portrait Expression: 기존 slot의 expression 변경
- Portrait Focus: 다른 Portrait dim 또는 현재 발화자 강조. 후순위.

## Design Constraints

- Say는 Portrait가 없어도 실행돼야 한다.
- Portrait 명령은 UI state 변경 요청이며 Flow 실행 규칙과 분리한다.
- Actor database는 MVP 이후 도입한다.
- 리소스 path 문자열을 직접 저장할지 actor id를 저장할지 ADR이 필요하다.

## Suggested Steps

- [x] Portrait runtime request 정의 (Step 1). 상태 모델은 이후 Step DialogueUI 책임.
- [x] Show/Hide/Expression 노드 및 Adapter 구현 (Step 2)
- [x] 저장/재로드 round-trip 검증 (Step 2)
- [x] DialogueUI에 left/center/right slot 추가 (Step 3)
- [x] 연속 Say에서 Portrait 상태 유지 검증 (Step 3)
- [x] 대화 종료/교체 시 상태 정리 검증 (Step 3)

## Completion Criteria

- Portrait 없이 기존 대화가 동일하게 동작한다.
- Portrait 상태가 다음 Say까지 유지된다.
- Hide와 Expression 변경이 slot 단위로 동작한다.
- 대화 교체 시 이전 Portrait 상태가 남지 않는다.

## Step 1: Portrait 런타임 명령 계약 (완료 2026-06-11)

Portrait를 Say와 독립된 비대기 Flow 명령으로 런타임에서 처리한다. 이 Step은
런타임 명령 계약만 다룬다. 상태 소유와 렌더링은 이후 Step의 DialogueUI 책임이다.

### 변경

- `addons/dialogtool/RunTime/dialogue_player.gd`
  - `_execute_until_waiting()`의 dispatch match에 `portrait_show`, `portrait_hide`,
    `portrait_expression` 처리를 추가했다.
  - `_execute_portrait()`는 정규화된 요청을 발행한 뒤 `_go_to_next_node(0)`으로
    즉시 다음 Flow로 진행한다. `waiting_for`를 만들지 않는다.
  - 정규화는 `_build_portrait_request()` / `_portrait_action_from_type()` /
    `_normalize_portrait_slot()` 작은 함수로 분리했다.
- `addons/dialogtool/RunTime/dialogue_manager.gd`
  - `ui_request` 중계에 source guard 추가(리뷰 후속 수정 참조).

### 요청 계약

세 노드는 공통 형식으로 정규화된다.

```gdscript
{
    "type": "portrait_state",
    "action": "show" | "hide" | "expression",
    "slot": "left" | "center" | "right",
    "texture_path": "...",
    "actor": "...",
    "expression": "...",
    "transition": "none",
}
```

- `action`은 노드 타입에서 파생한다.
- `slot`이 유효 집합에 없으면 경고 후 `center`로 일관되게 대체한다(크래시 없음).
- `show`의 `texture_path`가 비어 있어도 Flow를 중단하지 않고 경고 후 요청을 발행한다.
- `actor`/`expression`은 향후 resolver를 위한 메타데이터로 통과시킨다.
- `transition` 기본값은 `none`이다.
- MVP가 `texture_path`를 저장하는 결정은 [[ADR-004-Portrait-Texture-Path-MVP]].

### 기존 리소스 호환

- `SayDef.portrait` export 필드, `get_runtime_params()`의 `portrait`,
  `display_text` 요청의 `portrait` 키를 모두 그대로 유지했다.
- 기존 `.tres`의 `portrait="empty"`를 새 명령으로 변환하지 않았다.
- SayDef와 SayEditorAdapter 구조는 변경하지 않았다.

### 검증

- Godot 4.6.3 headless editor load 성공 (dialogue_player 관련 parse/compile 오류 없음).
- 임시 헤드리스 테스트(`test_portrait_step1.tscn`, 검증 후 제거)로 수작업 runtime
  snapshot을 실행해 20개 체크 전부 통과:
  - `Start -> portrait_show -> Say -> End`가 같은 실행 루프에서 `portrait_state`와
    이어지는 `display_text`를 모두 발행한다.
  - `portrait_hide`, `portrait_expression`도 동일하게 비대기로 Say까지 도달한다.
  - Portrait 없는 대화는 Say 1회만 발행하고 `display_text`에 `portrait` 키가 유지된다.
  - 잘못된 slot(`middle`)은 경고 후 `center`로 대체, 크래시 없음.
  - 빈 `texture_path`의 show도 요청을 발행하고 Flow가 진행된다.
  - `pride_and_prejudice.tres` 로드 시 say 노드 params에 `portrait` 키가 보존된다.

### 리뷰 후속 수정

- **[P1] 대화 교체 후 이전 Player의 stale 요청 발행**: `portrait_*`는 첫 비대기 명령으로
  요청을 발행한 뒤 루프를 계속 진행한다. 요청 콜백에서 `DialogueManager.play()`로 대화를
  교체하면 이전 player가 돌아와 stale `display_text`를 발행하고, Manager가 이를 게임 코드로
  중계할 수 있었다.
  - 수정: `dialogue_manager.gd`의 `ui_request` 중계에 `dialogue_end`와 동일한 source guard를
    적용했다(`_on_ui_request.bind(_ui)`, `source_ui != _ui`면 무시). 교체된 이전 UI가 발행한
    지연 요청은 게임 코드에 도달하지 않는다.
  - 회귀 검증: 재검증 테스트에 reentrancy 시나리오 추가 — portrait_state 콜백에서 play()로
    교체 시, 이전 대화의 `display_text(OLD)`는 차단되고 새 대화의 `display_text(NEW)`만 전달됨을
    확인(3개 체크 통과, 총 15 checks 0 failures).

### Step 2로 넘긴 항목

- Portrait Definition, Editor Adapter, Registry 등록, 에디터 노드 UI.
- `Dialogue_UI.tscn`의 Portrait 슬롯, 상태 소유, 텍스처 로드/렌더링.
- transition 애니메이션, actor database/resolver.
- 대화 교체/종료 시 Portrait 상태 정리(상태가 DialogueUI로 이동한 뒤 검증).

## Step 2: Portrait 에디터 노드와 저장 왕복 (완료 2026-06-11)

세 Portrait 노드를 DialogueTool 에디터에서 생성·편집하고, 입력값이 Definition과
runtime snapshot에 저장되어 재로드 후 보존되게 한다. Step 1의 런타임 요청 계약을
그대로 사용한다. DialogueUI 슬롯/상태/렌더링은 여전히 이후 Step 범위다.

### 변경 파일

- `Resource/NodeDefinitions/Abstract/portrait_def.gd` (신규): `@abstract class_name PortraitDef extends FlowDefinition`.
  공통 export 필드(slot, texture_path, actor, expression, transition)와 `get_runtime_params`,
  `_node_init`/`_capture`(어댑터 위임), `_is_done`/`execute`(비대기, 레거시 no-op)를 제공.
  `Abstract/` 폴더에 두어 노드 목록 검색에서 제외된다.
- `Resource/NodeDefinitions/Flow/portrait_show_def.gd` (신규): `PortraitShowDef`, type `&"portrait_show"`.
- `Resource/NodeDefinitions/Flow/portrait_hide_def.gd` (신규): `PortraitHideDef`, type `&"portrait_hide"`.
- `Resource/NodeDefinitions/Flow/portrait_expression_def.gd` (신규): `PortraitExpressionDef`, type `&"portrait_expression"`.
- `Editor/Adapter/portrait_editor_adapter.gd` (신규): 세 노드 공통 어댑터(경로 기반 extends, class_name 없음).
  노드 type으로 노출 필드 결정 — Hide는 slot/transition만, Show/Expression은 5개 모두.
  위젯은 코드로 구성(슬롯 OptionButton + 나머지 LineEdit)하고 row 1에 Flow 입력/출력 슬롯 설정.
- `Editor/Adapter/node_type_registry.gd` (수정): 세 type을 같은 어댑터 인스턴스로 등록.

SayDef, SayEditorAdapter, DialoguePlayer(Step 1 계약), DialogueManager는 변경하지 않았다.

### Definition / Adapter 와 필드 계약

- 세 Definition은 동일한 직렬화 필드와 `get_runtime_params` 형태(slot, texture_path,
  actor, expression, transition)를 공유하고 `get_runtime_type`만 다르다.
- 노드 목록(`dialogue_node_item_list.gd`)은 `class_name`의 `get_global_name().left(-3)`로
  라벨을 만든다 → "PortraitShow"/"PortraitHide"/"PortraitExpression". 베이스는 `Abstract/`라 제외.
- 각 노드는 Flow 입력 1개 + Flow 출력 1개(포트 index 0). Step 1 player의 output port 0 진행과 일치.
- runtime snapshot에는 문자열/기본 타입만 저장한다(Texture2D나 UI 노드 객체 없음).
- texture_path가 MVP 1차 이미지 식별자(ADR-004), actor/expression은 resolver용 메타데이터, transition 기본 "none".

### 저장 / 재로드 호환

- `editor.gd`의 `capture_current_graphedit`가 `def._capture(node)` 후 `set_runtime_snapshot`을
  호출하는 기존 경로를 그대로 사용한다.
- `_capture`는 어댑터가 실제 캡처한 키만 갱신한다. Hide UI에 노출되지 않는
  texture_path/actor/expression는 기존 Definition 값이 편집/재저장에서 보존된다.
- 기존 `.tres`의 SayDef.portrait와 display_text.portrait 계약은 그대로 유지된다.
  Portrait 노드가 없는 기존 리소스는 동일하게 로드/실행된다.

### 검증 (Godot 4.6.3 headless, 임시 테스트 후 제거 — 44 checks 0 failures)

1. headless editor load 성공, PortraitDef/Show/Hide/Expression 전역 클래스 등록, 오류 없음.
2. 노드 목록에 세 노드 노출, 추상 베이스는 제외.
3. DialogueNode 생성 시 어댑터가 위젯 구성 — Show/Expression 5필드, Hide 2필드, 각 Flow 입력1/출력1.
4. 위젯 편집 → `_capture` → export 필드 갱신. Hide의 미노출 필드는 보존됨을 확인.
5. 저장 → 재로드: 정의 타입/필드, 노드 position_offset, 연결 5개와 포트 순서, runtime_nodes type/params 보존.
6. `Start -> PortraitShow -> PortraitExpression -> PortraitHide -> Say -> End` 실행 시
   portrait_state 3개(action show/expression/hide, slot left/center/right) 뒤 display_text 도달.
7. 기존 `pride_and_prejudice.tres` 로드/실행 정상, Say params에 portrait 키 보존.

전체 GraphEdit 드래그-드롭/Ctrl+S GUI는 headless로 직접 구동하지 않았으나, 그 경로가
호출하는 각 구성요소(노드 목록 검색, 어댑터 apply/capture, `_capture`→`set_runtime_snapshot`
→저장→재로드→`_node_init`)를 `editor.gd` 호출과 동일하게 프로그램적으로 검증했다.

### 리뷰 결과

- P0/P1 없음.
- **[P2] 잘못된 slot 값이 편집/재저장에서 손실됨 — 수정**: 초기 구현은 저장된 slot이
  left/center/right 밖이면 OptionButton이 `center`를 선택하고 `_capture`가 이를 Definition에
  덮어써, 런타임 fallback과 달리 원본 리소스를 조용히 변경했다("저장/재로드 값 보존" 계약 위반).
  - 수정: `portrait_editor_adapter._make_slot_row`에서 알 수 없는 값을 임시 OptionButton 항목으로
    추가해 선택한다. 사용자가 slot을 건드리지 않으면 capture가 원본 값을 그대로 반환하고,
    유효 slot을 명시적으로 선택하면 그때만 교체된다.
  - 검증(임시 테스트, 5 checks 0 failures, 후 제거): "middle" 저장 후 texture_path만 편집 →
    slot "middle" 보존; 명시적 left 선택 → "left"로 교체; 유효 slot은 보존.
- P3(보류, 의도된 동작): transition은 자유 입력·기본값 "none"(transition 시스템은 제외 범위);
  Hide의 runtime params에 빈 texture_path/actor/expression가 포함되나 hide 동작에 무해(공통 베이스 설계).

### 다음 Step으로 넘긴 항목

- Dialogue_UI.tscn의 left/center/right 슬롯, Portrait 상태 소유와 Texture 렌더링.
- transition 애니메이션, actor database/resolver.
- 연속 Say에서 Portrait 상태 유지, 대화 종료/교체 시 상태 정리.

## Step 3: DialogueUI Portrait 상태와 슬롯 렌더링 (완료 2026-06-11)

### 목표

- `DialogueUI`가 Step 1의 `portrait_state` 요청을 소비한다.
- left/center/right 슬롯별 Portrait 상태를 소유하고 `texture_path`의 Texture를 표시한다.
- Portrait 상태가 Say와 직접 결합되지 않은 채 연속 Say와 Choice 전환 중 유지된다.

### 작업 범위

- `addons/dialogtool/UI/Dialogue_UI.tscn`
  - left/center/right Portrait 슬롯을 추가한다.
  - 각 슬롯은 최소한 Portrait Texture를 표시하고 숨길 수 있어야 한다.
  - Portrait가 텍스트와 선택지 입력을 막지 않도록 레이아웃과 mouse filter를 구성한다.
- `addons/dialogtool/UI/dialogue_ui.gd`
  - `portrait_state` 요청을 처리한다.
  - 슬롯별 지속 상태를 생성하고 show/hide/expression을 적용한다.
  - `texture_path`를 안전하게 로드하고 TextureRect에 반영한다.
  - 대화 시작과 종료 시 현재 UI의 Portrait 상태와 표시를 초기화한다.

권장 상태 형태:

```gdscript
{
    "left": {
        "texture_path": "",
        "actor": "",
        "expression": "",
        "transition": "none",
        "visible": false,
    },
    "center": { ... },
    "right": { ... },
}
```

### 상태 전이 계약

- `show`
  - 요청의 slot 상태를 갱신한다.
  - 유효한 `texture_path`를 Texture로 로드하고 해당 슬롯을 표시한다.
  - 빈 경로나 로드 실패는 크래시하지 않아야 한다. 실패 정책을 구현 보고에 명시한다.
- `hide`
  - 지정 slot만 숨기고 다른 slot에는 영향을 주지 않는다.
- `expression`
  - 해당 slot의 기존 상태를 기준으로 요청에서 제공된 값만 갱신한다.
  - 빈 `texture_path`는 기존 Texture를 제거하지 않는다.
  - `texture_path`가 제공되면 Texture를 교체한다.
  - show 이전 요청도 크래시하지 않아야 한다.
- Say와 Choice 표시 전환은 Portrait 상태를 초기화하지 않는다.
- `transition`은 상태에 보존할 수 있지만 애니메이션은 이번 Step에서 적용하지 않는다.

### 설계 제약

- Say 노드와 Portrait를 결합하지 않는다.
- `speaker`로 Portrait slot을 추론하지 않는다.
- 기존 `display_text.portrait` 값을 새 Portrait 상태로 자동 변환하지 않는다.
- `DialoguePlayer`에 UI 상태나 Texture 로딩 책임을 추가하지 않는다.
- `DialogueManager`가 Portrait 슬롯 내부 구조를 알게 하지 않는다.
- Portrait 상태와 렌더링 책임은 `DialogueUI` 내부에 둔다.
- Step 1의 요청 계약과 Step 2의 Definition/Adapter 계약을 변경하지 않는다.
- Texture2D 객체를 Dialogue 리소스나 runtime snapshot에 저장하지 않는다.
- [[ADR-004-Portrait-Texture-Path-MVP]]에 따라 `texture_path`를 MVP 이미지 식별자로 사용한다.
- actor/expression은 향후 resolver용 메타데이터로 보존한다.

### 제외 범위

- fade/slide 등 transition 애니메이션.
- Portrait Focus와 dim 처리.
- actor database 또는 resolver.
- speaker 기반 자동 Portrait 선택.
- 기존 `SayDef.portrait` 마이그레이션.
- Portrait Definition과 Editor Adapter의 추가 구조 변경.
- Dialogue UI 전체 디자인 개편.

### 완료 조건

- Show가 left/center/right 각각에 Texture를 표시한다.
- 세 슬롯을 동시에 서로 다른 상태로 표시할 수 있다.
- Hide가 지정 slot만 숨긴다.
- Expression이 지정 slot의 메타데이터와 선택적 Texture를 갱신한다.
- 연속 Say와 Choice 전환 후에도 Portrait 상태가 유지된다.
- Portrait가 없는 기존 대화의 UI 동작이 동일하다.
- 잘못된 slot, 빈 경로, 존재하지 않는 경로에서 크래시하지 않는다.
- 대화 종료 후 현재 DialogueUI의 Portrait 상태와 표시가 정리된다.
- 새 대화 실행 시 이전 UI의 Portrait 상태가 남지 않는다.
- Step 1의 `DialogueManager.ui_request` source guard가 계속 동작한다.
- P0/P1 문제가 남아 있지 않다.

### 검증 방법

1. Godot 4.6.3 headless editor load.
2. `Dialogue_UI.tscn` 인스턴스 생성과 NodePath 확인.
3. left/center/right 각각 show 및 세 슬롯 동시 표시.
4. 한 slot hide 후 다른 slot 유지.
5. expression의 Texture 교체와 빈 `texture_path`에서 기존 Texture 유지.
6. show 이전 expression 요청의 안전한 처리.
7. 빈 `texture_path`, 존재하지 않는 경로, 잘못된 slot 처리.
8. `PortraitShow -> Say A -> Say B`에서 상태 유지.
9. `PortraitShow -> Choice -> Say`에서 상태 유지.
10. dialogue_end 후 상태/표시 정리.
11. 실행 중 다른 대화로 교체 후 이전 Portrait 미잔존.
12. Portrait가 없는 기존 리소스 실행.
13. portrait_state callback 재진입 시 stale 요청 차단 회귀 확인.

가능하면 테스트용 Texture는 메모리에서 만들거나 기존 프로젝트 리소스를 사용한다.
임시 검증 파일은 완료 전에 제거하거나 장기 회귀 자산으로 유지할 이유를 기록한다.
완료 후 [[STEP_REVIEW_WORKFLOW]]에 따라 별도 리뷰와 재검증을 수행하고, 구현 내용과
검증 증거를 이 문서의 Step 3 아래에 기록한다.

### 구현 결과

**변경 파일**

- `addons/dialogtool/UI/Dialogue_UI.tscn`: 루트에 `Portraits` Control과 left/center/right
  TextureRect 슬롯을 추가했다. `z_index = -1`로 대화 패널 뒤에 그리고, `Portraits`와 세 슬롯의
  `mouse_filter = 2(IGNORE)`로 advance Button 입력을 가로채지 않게 했다. 슬롯은 화면을 좌/중/우
  3등분 anchor, `expand_mode=1`, `stretch_mode=5(keep aspect centered)`, 초기 `visible=false`.
- `addons/dialogtool/UI/dialogue_ui.gd`: `portrait_state` 요청 처리와 슬롯별 지속 상태 소유를 추가했다.
  - `_ui_request` match에 `"portrait_state": _handle_portrait_state(request)` 추가.
  - 상태 소유: `_portrait_state` Dictionary(slot -> {texture_path, actor, expression, transition}).
    슬롯 표시 여부는 상태 존재 여부로 표현(hide 시 erase).
  - `_portrait_show`: slot 상태를 통째로 갱신하고 texture를 로드해 표시.
  - `_portrait_expression`: 기존 상태 기준, 비어있지 않게 제공된 값만 갱신. 빈 texture_path는
    기존 Texture를 유지. show 이전이어도 빈 상태에서 시작해 크래시하지 않음.
  - `_hide_portrait`: 지정 slot만 숨기고 상태에서 제거.
  - `_load_texture`: 빈 경로/미존재/비-Texture2D를 경고 후 null로 안전 처리.
  - `_clear_portraits`: `play()`(대화 시작)와 `_on_dialogue_end`(종료)에서 호출해 상태/표시 정리.

DialoguePlayer(Step 1 계약), DialogueManager, SayDef/SayEditorAdapter, Portrait Definition/Adapter는
변경하지 않았다. Say 핸들러는 Portrait를 건드리지 않아 연속성이 유지된다.

**실패 정책**

- show의 빈/실패 texture_path: 슬롯을 표시하되 texture는 null(아무것도 안 그림), 상태는 기록. 크래시 없음.
- expression의 빈 texture_path: 기존 Texture를 제거하지 않고 메타데이터만 갱신.
- 잘못된 slot(런타임은 이미 정규화하지만 방어적으로): 경고 후 무시, 상태 변경 없음.

**상태 정리와 교체**

- 대화 교체는 DialogueManager가 `play()`마다 새 DialogueUI를 만들고 이전 UI를 free하므로(ADR-003)
  이전 Portrait가 새 UI로 새지 않는다. 추가로 `_on_dialogue_end`와 `play()`에서도 정리한다.
- Step 1의 `DialogueManager.ui_request` source guard가 그대로 동작해, 교체된 이전 player가 발행하는
  stale `portrait_state`는 게임 코드/새 UI로 전달되지 않는다.

### 검증 결과 (Godot 4.6.3 headless, 임시 테스트 후 제거 — 22 checks 0 failures)

1. headless editor load 성공(scene/script parse 오류 없음).
2. left/center/right 세 슬롯 동시 show, 슬롯별 메타데이터 독립.
3. hide는 지정 slot만 숨기고 다른 slot 유지.
4. expression: 빈 texture_path는 기존 Texture 유지·메타만 갱신, 미제공 actor 보존, texture_path
   제공 시 교체, show 이전 expression도 안전 처리.
5. show -> Say -> (Choice) 전환에도 Portrait 상태 유지(연속성).
6. dialogue_end 시 상태/표시 정리.
7. 잘못된 slot/빈 경로/없는 경로 모두 크래시 없음.
8. player+resource 통합: `Start -> portrait_show -> Say -> End`에서 슬롯 반영, Say 도달, End 정리.
9. 기존 `pride_and_prejudice.tres` 실행 정상, Portrait 슬롯 미표시·상태 비어 있음.

실제 화면 픽셀 렌더링은 headless로 캡처하지 않았으나, 각 슬롯의 `visible`과 `texture` 할당,
상태 Dictionary를 프로그램적으로 확인했다. P0/P1 없음.

### 다음 Step으로 넘긴 항목

- transition 애니메이션(fade/slide), Portrait Focus/dim, actor database/resolver.
- Step 4: 반복 실행/교체/재진입 통합 회귀 및 DT-002 MVP 완료 판정.

## Step 4: Portrait 수명 주기와 통합 회귀 (완료 2026-06-11)

### 목표

- Step 1~3을 실제 대화 실행 수명 주기에서 통합 검증한다.
- 종료, 반복 실행, 대화 교체와 signal 재진입에서 이전 Portrait 상태나 요청이 남지 않게 한다.
- 기존 Portrait 없는 대화 리소스의 호환성을 확인하고 DT-002 MVP 완료 여부를 판정한다.

### 작업 범위

- 같은 Dialogue 리소스의 반복 실행.
- 실행 중 다른 Dialogue 리소스로 교체.
- `dialogue_end` callback에서 다음 Dialogue를 즉시 시작하는 재진입.
- `portrait_state` callback에서 Dialogue를 교체하는 Step 1 source guard 회귀.
- 종료 및 교체 시 DialogueUI Portrait 상태, Texture 참조와 표시 정리.
- Portrait가 없는 기존 `.tres` 리소스의 로드와 실행.
- Show/Expression/Hide와 Say/Choice가 함께 있는 통합 그래프 검증.
- Step 1~3 리뷰에서 남은 P0/P1 수정과 재검증.
- 완료 후 Task, Current-State, Open-Tasks, DialogueTool 시스템 문서와 Review 문서 갱신.

### 설계 제약

- Say와 Portrait의 독립 계약을 유지한다.
- 기존 `SayDef.portrait`와 `display_text.portrait`를 삭제하거나 자동 마이그레이션하지 않는다.
- `DialogueManager`의 source guard를 우회하지 않는다.
- 종료된 이전 UI가 새 UI의 Portrait 상태를 정리하거나 변경하지 못하게 한다.
- 통합 검증을 위해 관련 없는 런타임 또는 에디터 구조를 리팩터링하지 않는다.

### 제외 범위

- transition 애니메이션.
- Portrait Focus와 dim 처리.
- actor database와 actor/expression resolver.
- speaker 기반 자동 Portrait 선택.
- named entry, Wait, Sound 등 다른 Dialogue 기능.

### 완료 조건

- `Start -> Show -> Say -> Expression -> Choice -> Hide -> End` 흐름이 종료까지 실행된다.
- 같은 리소스를 반복 실행해도 슬롯 상태와 Texture가 누적되지 않는다.
- 실행 중 새 리소스로 교체하면 이전 Portrait가 즉시 논리적으로 분리되고 새 UI에 남지 않는다.
- 종료 callback에서 새 대화를 시작해도 새 Portrait가 이전 종료 처리로 지워지지 않는다.
- portrait_state callback 교체 시 이전 Player의 stale 요청이 외부로 전달되지 않는다.
- Portrait 없는 기존 리소스가 변경 전과 동일하게 실행된다.
- 저장/재로드 후 Portrait Definition 필드와 Flow 포트가 보존된다.
- Godot headless editor load와 통합 실행 검증이 성공한다.
- P0/P1 문제가 없고, 남은 P2/P3 또는 테스트 공백이 문서화된다.
- DT-002 완료 조건 네 항목을 모두 만족하면 Task status를 `completed`로 변경한다.

### 검증 방법

1. Godot 4.6.3 headless editor load.
2. Portrait 통합 그래프 저장, 재로드와 실행.
3. 같은 리소스 2회 이상 반복 실행.
4. Portrait가 표시된 상태에서 다른 리소스로 교체.
5. `dialogue_end` callback 즉시 재실행.
6. `portrait_state` callback 중 대화 교체와 stale 요청 차단.
7. Show 이전 Expression, 중복 Hide와 빈/잘못된 경로 회귀.
8. Portrait 없는 기존 리소스 실행과 Say portrait 필드 보존.
9. Step 1~3의 핵심 검증 시나리오 재실행.
10. [[STEP_REVIEW_WORKFLOW]]에 따른 최종 코드 리뷰와 재검증.

중요한 최종 리뷰 결과는 `LLM_WIKI/50_Reviews`에 기록한다. 완료 시
`00_Index/Open-Tasks.md`에서 DT-002를 제거하고, 미완성 후속 기능은 별도 Task로 남긴다.

### 구현 결과

Step 4는 검증 단계다. Step 1~3 리뷰에서 남은 P0/P1이 없어 **제품 코드 변경은 없었고**,
실제 `DialogueManager` 오토로드 경로(생성/교체/정리)를 거쳐 통합/수명주기를 검증했다.

**검증 결과 (Godot 4.6.3 headless, 임시 테스트 후 제거 — 29 checks 0 failures)**

- [A] 통합 그래프(`Start→Show→Say→Expression→Choice→Hide→End`) 저장/재로드: Show/Expression/Hide
  Definition 필드, Flow 연결/포트, runtime type 보존.
- [B] Manager 경유 전체 실행: show→left 표시, Say 유지, advance→expression(texture 교체/표정),
  Choice 대기 중 유지, select_choice(0)→hide(slot 단위), Say2, advance→End에서 Portrait 정리.
- [C] 같은 리소스 2회 반복 실행: 슬롯 상태가 누적되지 않음(매 실행 left 1개).
- [D] 실행 중 다른 리소스로 교체: 새 UI 인스턴스로 교체, 이전 Portrait가 새 UI에 남지 않음.
- [E] `dialogue_end` callback에서 새 대화 시작: 새 Portrait가 이전 종료 처리로 지워지지 않음.
- [F] `portrait_state` callback에서 교체(Step 1 source guard 회귀): 이전 player의 stale
  `display_text(OLD)` 차단, 새 대화 `display_text(NEW)` 전달.
- [G] Portrait 없는 기존 `pride_and_prejudice.tres` 실행 정상, Say `portrait` 키 보존, 슬롯 미표시.

리뷰 판정과 증거는 [[DT-002-Portrait-Review]]에 기록했다.

### DT-002 완료 판정

완료 조건 네 항목을 모두 만족한다.

- Portrait 없이 기존 대화 동일 동작 — [G].
- Portrait 상태가 다음 Say까지 유지 — [B], Step 3.
- Hide/Expression이 slot 단위로 동작 — [B], Step 3.
- 대화 교체 시 이전 Portrait 미잔존 — [D].

따라서 DT-002 Portrait State MVP를 **완료**로 판정하고 Task status를 `completed`로 변경한다.
transition/Focus/resolver/자동 회귀 자산은 아래 "MVP 이후 후속 작업"으로 분리한다.

### 남은 P2/P3 및 테스트 공백

- 실제 화면 픽셀 렌더링은 headless로 캡처하지 않았다(슬롯 `visible`/`texture`/상태는 프로그램 검증).
- 통합 회귀는 임시 테스트로 수행 후 제거했다 — 고정된 자동 회귀 자산은 미도입(별도 후속).
- expression의 show 이전 호출은 슬롯을 표시한다(MVP 정책, resolver 도입 시 재검토).

## MVP 이후 후속 작업

다음 항목은 DT-002 Portrait State MVP 완료를 막지 않으며 별도 Task로 분리한다.

- Portrait transition 애니메이션(fade/slide 등).
- Portrait Focus와 비활성 Portrait dim 처리.
- actor database 및 actor/expression -> Texture resolver.
- speaker 기반 자동 focus 또는 Portrait 선택 정책.
- 기존 `SayDef.portrait` 데이터의 명시적 마이그레이션 도구.
- 고정된 DialogueTool 자동 통합 회귀 테스트 자산.

## 완료 후 UX 개선: texture_path 드래그 앤 드롭 (2026-06-11)

- Portrait Show/Expression의 `texture_path` 입력은 기존 문자열 직접 입력을 유지하면서,
  Godot FileSystem에서 Texture2D 리소스 하나를 드롭하면 해당 `res://` 경로를 자동 입력한다.
- 파일이 여러 개이거나, 프로젝트 리소스 경로가 아니거나, 로드 결과가 Texture2D가 아니면
  드롭을 거부하고 기존 입력값을 보존한다.
- 런타임 요청, Definition export 필드와 저장 리소스 형식은 변경하지 않는다.
