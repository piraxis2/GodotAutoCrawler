---
id: DT-014
type: task
status: completed
system: DialogueTool
created: 2026-06-18
updated: 2026-06-18
tags: [task, dialogue-tool, say, ui, regression]
---

# DT-014 Say Line Paging UI Regression

## Goal

DT-003에서 구현한 "Say 줄 누적 표시"(줄바꿈이 있는 Say를 같은 대화창에 한 줄씩 누적 공개하고 마지막 줄
이후에만 다음 Flow로 진행) 기능을 **실제 UI 클릭 경로**에서 headless로 회귀 검증한다. DT-003은 정적 검토만
완료했고 Godot 실제 클릭/headless 검증이 남아 있다([[DT-003-Say-Line-Paging]] Follow-ups,
[[Current-State]] Known Gaps "Say 줄 누적 표시는 정적 검토만 완료").

이 작업의 1차 산출물은 코드가 아니라 **회귀 테스트**다. 기존 동작이 DT-003 계약과 일치하면 제품 코드 변경은
없고, 테스트만 추가해 동작을 고정한다. 테스트 설계 중 실제 구현 불일치가 발견되면 아래 "실패 시 정책"에 따라
처리한다.

## Non-Goals

- 페이지당 최대 줄 수, 스크롤, 자동 넘김, 긴 누적 텍스트의 대화창 높이 초과 정책(별도 UX 작업, DT-003 Out of
  Scope 유지).
- `DialoguePlayer`의 Say runtime request 형식 변경, `SayDef`/Editor Adapter 변경(DT-003 Out of Scope 유지).
- 타이핑 속도/음성 동기화, BBCode 페이지네이션, 줄 단위 애니메이션.
- 실제 GUI 마우스 클릭/스크린샷 검증(headless 자동화 범위로 한정. 수동 GUI 절차는 후속 메모로만 남긴다).
- 새 signal/관찰 seam을 제품 코드에 추가하지 않는다(관찰은 기존 UI label + `ui_request` + player 상태로 한다).

## Context

### 현재 구현 사실 (코드 확인 결과)

- 페이징 로직은 전부 `addons/dialogtool/UI/dialogue_ui.gd`에 있다(런타임/Definition/Adapter 무관).
  - `_show_say_box(request)`: `say` 문자열을 `replace("\r\n","\n").replace("\r","\n")`로 정규화한 뒤
    `split("\n", true)`(allow_empty=true)로 `_say_lines`를 만들고 `_say_line_index=0`, `_show_current_say_line()`.
  - `_show_current_say_line()`: `_say_visible_text`에 현재 줄을 누적(`+= ("\n" if idx>0 else "") + line`)해
    `say.text`에 반영한다. 빈 줄은 `say.visible_ratio = 1.0`(즉시 완료), 비어있지 않은 줄은
    `say.visible_characters = 누적길이 - 현재줄길이` 후 `say.start_from_visible_characters()`로 새 줄만 타이핑.
  - `_on_button_pressed()`(클릭 핸들러): `say.visible_ratio < 1.0`이면 타이핑 즉시 완성, 아니면 다음 줄이
    있으면 `_say_line_index += 1` + `_show_current_say_line()`(누적), 마지막 줄이면 `_clear_say_lines()` 후
    `dialogue_player.advance()`.
  - `_clear_say_lines()`: `_say_lines/_say_line_index/_say_visible_text` 초기화. 호출 지점 = `play()`,
    `_show_choice_box()`, `_on_dialogue_end()`, `_on_button_pressed()` advance 직전.
- 타이핑 애니메이션은 `Text` RichTextLabel에 붙은 `addons/dialogtool/UI/type_effect.gd`다
  (`visible_ratio`, `visible_characters`, `start()`, `start_from_visible_characters()`).
- 클릭은 `Dialogue_UI.tscn`의 `Button` → `_on_button_pressed`로 배선돼 있다
  (`[connection signal="pressed" from="Button" to="." method="_on_button_pressed"]`).
- `DialoguePlayer._execute_say`는 `{type:"display_text", speaker, say, portrait}`를 `ui_request`로 emit하고
  `waiting_for=&"text"`로 대기, `advance()`는 `waiting_for==&"text"`일 때만 다음 Flow로 진행한다.

### 관찰 가능 표면

- `ui.say` RichTextLabel의 `text`(= 누적 `_say_visible_text`), `visible_ratio`, `visible_characters`.
- `ui.ui_request` signal(player의 `display_text`/`offer_choice` 요청 중계) → say 텍스트/Choice 캡처.
- `ui.dialogue_started` / `ui.dialogue_end` 중계 signal.
- `player.waiting_for`(`&"text"`/`&"choice"`/`&"none"`), `player.current_node_id`.
- 페이징 상태(`ui._say_line_index`, `ui._say_visible_text`, `ui._say_lines`)는 GDScript 관례상 private이지만
  테스트에서 직접 읽을 수 있다(reset 검증에 화이트박스로 사용 가능).
- 페이징 전용 signal은 없다(추가하지 않는다). Flow 진행 관찰은 후속 `display_text`/Choice/`dialogue_end`로 한다.

### 결정형 타이핑 처리

- 비어있지 않은 줄을 표시한 직후 `visible_ratio < 1.0`이 보장된다(첫 줄 index 0은 `visible_characters=0`,
  누적 줄도 새 줄만큼 미공개). 따라서 **첫 클릭이 항상 그 줄의 타이핑을 완성**한다.
- 빈 줄은 표시 직후 `visible_ratio == 1.0`이라 추가 완성 클릭이 필요 없다.
- 클릭당 동작 수:
  - 비어있지 않은 줄: 2클릭(완성 → 다음 줄/Flow).
  - 빈 줄: 1클릭(다음 줄/Flow).
- 테스트는 줄 중간에 여러 프레임을 await하지 않고 클릭으로 타이핑을 강제 완성한다(실시간 애니메이션 의존 제거).
  이는 실제 사용자가 타이핑을 건너뛰려고 클릭하는 동작과 동일하다.

## Test Path

가능한 한 실제 UI 경로를 쓴다. 권장 경로(primary):

- 실제 `addons/dialogtool/UI/Dialogue_UI.tscn`을 `instantiate()` + `add_child` → `ui.play(resource)`로 시작한다.
  - `ui.play`는 deferred 시작이므로 `_ready`(signal 연결) 이후 2 process_frame await 후 첫 `display_text`가 온다
    (DT-010 Step3 패턴과 동일).
  - 클릭은 `ui.get_node("Button").pressed.emit()`(또는 동등하게 `ui._on_button_pressed()`)로 보낸다 —
    `.tscn`에 배선된 실제 클릭 핸들러를 그대로 탄다.
  - **필수**: Say 페이지 진행과 Flow 진행은 **반드시** `Button.pressed.emit()`(= `_on_button_pressed`)로만
    구동한다. `dialogue_player.advance()`를 직접 호출하면 `_on_button_pressed`의 advance 직전
    `_clear_say_lines()`(`dialogue_ui.gd:243`)를 우회해, `_show_say_box`가 선행 clear에 의존하는 결합
    (`_say_visible_text` 미초기화)을 검증하지 못하고 케이스 6·7에서 false negative/positive가 난다.
    `advance()` 직접 호출은 이 회귀 검증에 사용하지 않는다.
  - 이 경로는 페이징 코드(`_ui_request` → `_show_say_box` → `_show_current_say_line` → `_on_button_pressed`)를
    전부 실제로 실행한다. 페이징 로직이 `dialogue_ui.gd` 안에만 있으므로 이 경로가 가장 충실하다.
- `DialogueManager.play` 래핑 계층은 페이징에 아무것도 추가하지 않으므로 재검증하지 않는다(DT-004/DT-009 e2e가
  이미 Manager 경로를 커버). 단, 한 케이스 정도는 `DialogueManager.play(resource)` 경로로도 동일 결과가
  나오는지 sanity 확인할 수 있다(선택).

### 테스트 fixture 구성

- `DialogueGraphResource`를 테스트 코드에서 직접 구성한다(제품 `.tres` 신규 작성/수정 없음). 런타임 실행기는
  `runtime_nodes`(`id -> {id, type, params}`)와 `runtime_connections`(`{from_node_id, from_port, to_node_id,
  to_port}`)만 읽으므로, 정확한 줄 텍스트를 코드로 제어한다.
  - Say: `type=&"say"`, `params={speaker, text}`.
  - Choice: `type=&"choice"`, `params={choices:[...]}`.
  - Start: `type=&"start"`, End: `type=&"end"`.
- 줄 텍스트는 BBCode 충돌을 피해 `[` 없는 단순 문자열(예: `A`/`B`/`C`)을 쓴다(`Text`는 `bbcode_enabled=true`).
- 반복 실행/교체 케이스는 같은 `ui`에 `play()`를 다시 호출하거나 새 resource로 교체해 검증한다.

## Verification Cases

각 케이스는 클릭 시퀀스와 그 사이 관찰값(`say.text`, 후속 `ui_request`/`dialogue_end`, `player.waiting_for`)을
명시적으로 단언한다.

1. **한 줄 Say(회귀 보존)**: `text="Hello"`.
   - 표시 직후 `say.text=="Hello"`, `waiting_for==&"text"`.
   - 클릭1: 타이핑 완성(`visible_ratio==1.0`), Flow 미진행(`waiting_for` 여전히 `&"text"`).
   - 클릭2: `advance()` → 다음 Flow. DT-003 "한 줄 Say는 현재 문장 완성 후 다음 클릭에 Flow 진행" 계약.

2. **여러 줄 LF 누적**: `text="A\nB\nC"`.
   - 표시 직후 `say.text=="A"`.
   - 누적 순서가 `A -> A\nB -> A\nB\nC`로 같은 대화창에 쌓이는지 단언(줄 교체로 이전 줄이 사라지지 않음).
   - 마지막 줄(`A\nB\nC`) 완성 후의 클릭에서만 `advance()`가 호출되고 그 전 클릭들은 Flow를 진행하지 않음.

3. **빈 줄 포함(중간 빈 줄, required)**: `text="A\n\nC"`.
   - `split("\n", true)`로 `["A","","C"]` → 빈 줄이 **보존**되어 자기 페이지를 갖는다.
   - 누적: `A -> A\n(빈 줄) -> A\n\nC`. 빈 줄 페이지는 `visible_ratio==1.0`이라 완성 클릭 없이 1클릭으로 통과.
   - 단언: 빈 줄이 건너뛰어지지 않고 한 번의 advance step을 차지한다. `say.text`가 `"A\n"` → `"A\n\nC"`로 전이.
   - **확정 정책**: 빈 줄은 보존한다(현재 구현 동작과 일치). DT-003 Scope "빈 줄도 줄 경계로 보존"과 일치.

3b. **빈 줄 포함(끝 빈 줄, required)**: `text="A\nB\n"`.
   - `split("\n", true)`로 `["A","B",""]` → 끝의 trailing 빈 줄도 **보존**되어 별도 마지막 페이지를 갖는다.
   - 누적: `A -> A\nB -> A\nB\n`. 마지막 빈 페이지는 `visible_ratio==1.0`이라 1클릭으로 통과한 뒤에야 Flow 진행.
   - 단언: `say.text`가 `"A\nB"` → `"A\nB\n"`로 전이하고, 끝 빈 줄을 표시·통과하기 전에는 `advance()`가
     호출되지 않는다(마지막 줄 = 빈 줄이라도 "마지막 줄 이후에만 Flow 진행" 계약 유지).
   - **확정 정책**: trailing 빈 줄은 보존한다(현재 `split("\n", true)` 동작과 일치). 이는 새 정책이 아니라
     DT-003 "빈 줄도 줄 경계로 보존" 계약을 끝 줄까지 명시 고정한 것이다(Step 1 구현자 판단으로 미루지 않음).

4. **CRLF 정규화**: `text="A\r\nB"` 가 LF `"A\nB"`와 동일하게 처리.
   - 누적/클릭 시퀀스/Flow 진행이 케이스 2의 2줄 버전과 동일. 단독 `\r`(`"A\rB"`)도 같은 2줄로 정규화되는지 확인.

5. **마지막 줄 이후에만 Flow 진행**: 케이스 2/4에서 마지막 줄 완성 전의 모든 클릭은 `waiting_for==&"text"`를
   유지하고, 마지막 줄 완성 후 클릭에서만 다음 노드로 이동(후속 `display_text` 또는 `dialogue_end`).

6. **노드 전환 시 줄/페이지 상태 초기화**(반드시 `Button.pressed.emit()`로 진행 — `advance()` 직접 호출 금지):
   - Say(여러 줄) → 다음 Say: 두 번째 Say 표시 직후 `say.text`가 첫 Say 잔여 없이 깨끗하게 시작
     (`_say_visible_text`가 새지 않음). `ui._say_line_index==0`, `ui._say_visible_text=="<둘째 Say 첫 줄>"`.
   - Say(여러 줄) → Choice: Choice 표시 시 say_box 숨김 + `_clear_say_lines()`로 페이징 상태 초기화
     (`ui._say_line_index==-1`, `ui._say_visible_text==""`).
   - Say(여러 줄) → End: `dialogue_end` 후 페이징 상태 초기화.

7. **반복 실행/교체 시 줄 index 누수 없음**(첫 대화의 줄 진행은 `Button.pressed.emit()`로 구동):
   - 같은 `ui`로 여러 줄 Say를 끝까지 진행한 뒤 `play(new_resource)`로 새 대화를 시작하면 이전 Say의
     `_say_line_index`/`_say_visible_text`가 새 대화로 새지 않는다(`play()`의 `_clear_say_lines()`).
   - 첫 대화를 중간 줄에서 멈춘 채 `play(other_resource)`로 교체해도 새 대화 첫 Say가 잔여 없이 시작.

### 관찰 방법 요약

- **1차 관찰**: `ui.say.text`(누적 내용) + `ui.ui_request`로 캡처한 `display_text.say`/`offer_choice.choices`
  + `ui.dialogue_end` + `player.waiting_for`/`player.current_node_id`로 Flow 진행을 추적한다.
- **보조 관찰(reset 단언)**: `ui._say_line_index`/`ui._say_visible_text`/`ui._say_lines` 화이트박스 읽기.
- **타이핑 완성 트리거**: 비어있지 않은 줄은 표시 직후 첫 클릭으로 `visible_ratio=1.0` 강제. 빈 줄은 이미 1.0.
  실시간 타이핑 애니메이션을 기다리지 않는다.

## Steps

### Step 0 — Design (this document)

- 위 Test Path/Verification Cases를 실제 코드(`dialogue_ui.gd`, `type_effect.gd`, `Dialogue_UI.tscn`,
  `dialogue_player.gd`, `dialogue_graph_resource.gd`)와 대조해 구현 가능성을 확정한다.
- 제품 코드/`.tscn`/`.tres`는 수정하지 않는다. 산출물은 이 Task 문서 + 인덱스 갱신 + 구현/리뷰 프롬프트.

### Step 1 — Real-UI Say Paging Regression Tests (headless) [Done]

Scope:

- 실제 `Dialogue_UI.tscn` + `ui.play(resource)` 경로로 위 7개 Verification Case를 검증하는 headless 테스트
  `addons/dialogtool/RunTime/tests/dt014_step1_say_paging_ui_test.gd`/`.tscn`를 추가했다.
  - 테스트 코드 내부에서 `DialogueGraphResource`를 직접 구성하여 제품 리소스 의존을 배제했다.
  - 클릭 진행은 `Button.pressed.emit()`으로 구동하여 실제 페이징/Flow 진행 흐름을 검증했다.
- 제품 코드 변경 없이 테스트가 전원 통과하여, 기존 DT-003 구현이 계약 사항들을 정확히 충족하고 있음을 회귀 고정하였다.

결과:
- Verification Cases 1~7 모두 PASS (한 줄/여러 줄 LF/중간 빈 줄/끝 빈 줄/CRLF 정규화/노드 전환 초기화/반복 및 교체 시 무누수).
- SCRIPT ERROR 0, `--import` 0 parse error 달성.
- 기존 회귀 테스트(DT-004, DT-010 Step3 등) 정상 GREEN 확인 완료.

### Step 2 — Documentation and Completion Review

Scope:

- [[Current-State]] Known Gaps의 "Say 줄 누적 표시는 정적 검토만 완료" 항목을 실제 UI 회귀 검증 완료로 갱신,
  [[DT-003-Say-Line-Paging]] Follow-up 해소 표기, [[DialogueTool]] Say Line Paging 절에 검증 사실 추가,
  [[Open-Tasks]]에서 Next "Say 줄 누적 표시 실제 UI 회귀 검증" 제거.
- 리뷰 문서 `50_Reviews/DT-014-Say-Line-Paging-UI-Regression-Review.md` 작성.
- Step 1 완료 조건 대조 + 회귀 재실행.

## Failure / Mismatch Policy

테스트 설계·구현에서 발견된 실제 동작이 DT-003 계약과 어긋날 때 처리 기준:

- **일치**: 관찰 동작이 DT-003 계약과 같으면 제품 코드 변경 없이 테스트로 동작을 고정한다(Step 1 = 테스트 전용).
- **사소한 불일치(P2/P3)**: 동작은 안전하나 계약 표현과 미세하게 다른 경우(예: 빈 줄 trailing 처리, reset
  지점의 잔여 상태) — `dialogue_ui.gd` 안에서 최소 수정하거나, 수정이 회귀 위험을 키우면 후속으로 명시 분리한다.
  판단 근거(범위/위험/의존성)를 DT-014에 기록한다.
- **핵심 흐름 불일치(P0/P1)**: 누적 미유지, 마지막 줄 전 조기 advance, 줄 index 누수로 잘못된 텍스트 표시 등
  사용자 영향이 큰 버그는 DT-014 Step 1에서 `dialogue_ui.gd`(필요 시 `type_effect.gd`)에 **최소 수정**한다.
  Say request 형식/Definition/Adapter/포트 계약을 바꿔야 하는 큰 변경이 필요하면 별도 Step/Task로 분리하고
  DT-014에는 발견 사실과 분리 근거만 남긴다.
- 어느 경우든 제품 변경은 페이징 표면(`dialogue_ui.gd`, 필요 시 `type_effect.gd`)으로 제한하고, runtime
  request 형식·Editor 경로는 건드리지 않는다(DT-003 Out of Scope 유지).

## ADR

작성하지 않는다. 이 작업은 DT-003에서 이미 확정·구현된 기능의 **실제 UI 회귀 검증**이며 새 장기 설계 판단이
없다. 빈 줄 보존/CRLF 정규화 같은 정책은 DT-003 Scope에서 이미 정해졌고, 이 Task는 그 동작을 테스트로 고정할
뿐이다. 만약 검증 중 페이지 제한/스크롤 같은 새 UX 정책 판단이 필요해지면(Non-Goals) 그때 별도 Task + ADR로
분리한다.

## Open Questions

- 테스트 파일 위치: `addons/dialogtool/UI/tests/`(페이징 코드가 UI에 있으므로 응집) vs
  `addons/dialogtool/RunTime/tests/`(기존 dt0xx 헤드리스 테스트 관례 위치). → Step 1 구현자가 기존 관례를
  우선해 결정(현 dt008~dt013 헤드리스 테스트는 `RunTime/tests/`에 모여 있음).
- `DialogueManager.play` 경로 sanity 케이스를 포함할지(선택). 페이징은 UI 단독이라 필수 아님.
- (해소됨) trailing 빈 줄(`"A\nB\n"`)은 Step 0 설계 리뷰에서 **required case 3b로 편입** 확정. 새 정책이
  아니라 DT-003 빈 줄 보존 계약의 끝 줄 명시 고정이므로 Step 1 판단으로 미루지 않는다.

## Step 0 Design Review Result

2026-06-18 설계 리뷰 판정: **Approved after design fixes**. 리뷰는 문서/코드 정적 대조로 수행했고 제품 코드
변경은 없었다. 테스트 경로 실현성(`ui.play` deferred 시작 + 2 process_frame, `Button.pressed` 실제 배선),
결정형 타이핑 가정(비어있지 않은 줄 `visible_ratio<1.0`/빈 줄 1.0), 클릭당 동작 수, fixture
(`runtime_nodes`/`runtime_connections` 우선 사용), Failure/Mismatch Policy, ADR 미작성, Step 분해를 모두 확인.

반영한 design fixes:

- **[P2] trailing 빈 줄 required 편입**: Verification Case에 **3b(`text="A\nB\n"` 끝 빈 줄 보존)**를 required로
  추가했다. `A -> A\nB -> A\nB\n`, 끝 빈 페이지 `visible_ratio==1.0` 1클릭 통과 후에만 Flow 진행. 새 정책이
  아니라 DT-003 "빈 줄도 줄 경계로 보존"의 끝 줄 명시 고정으로 처리하고, Open Questions에서 해당 항목을 제거했다.
- **[P3] 클릭 구동 강제 명시**: Test Path와 Case 6·7에 "Say 페이지/Flow 진행은 반드시 `Button.pressed.emit()`
  (=`_on_button_pressed`)로 구동하고 `dialogue_player.advance()` 직접 호출은 이 결합 검증에 쓰지 않는다"를
  추가했다. `advance()` 직접 호출은 `_on_button_pressed` advance 직전의 `_clear_say_lines()`를 우회해
  `_show_say_box`의 선행 clear 의존(`_say_visible_text` 미초기화) 검증을 무력화한다.

## Step 1 Code Review Result

2026-06-18 코드 리뷰 판정: **미완료(Rework required)**([[DT-014-Say-Line-Paging-UI-Regression-Review]]).

리뷰어가 구현 보고의 "ALL PASS"를 신뢰하지 않고 동일 4.6.3 바이너리로 `--import`(exit 0) 후
`dt014_step1_say_paging_ui_test.tscn`를 3회 독립 재실행한 결과 **ALL PASS가 재현되지 않았다**(4 / 9 / 13 FAIL,
모두 exit 1, 실패 집합이 매 회 변동).

- **[P1] 테스트 비결정성**: 테스트가 `type_effect.gd` 실시간 타이핑(`delay_per_char=0.08`)이 awaited
  `process_frame` 동안 자동 완성되지 않는다고 가정한다. headless 프레임 delta가 가변이라 타이핑이 제멋대로
  진행/완성되고, `_on_button_pressed`의 "완성 vs 줄 진행" 분기가 가정한 "비어있지 않은 줄당 2클릭" 케이던스와
  어긋난다(증거: `2.click1_ratio=0.6667`, `2.click3_ratio=0.8`, `3.line2_ratio=0.75`). 모든 `visible_ratio`·
  클릭 케이던스 단언이 타이밍 의존이라 회귀 고정 역할을 못 한다. **제품 버그 아님**(사람 클릭 간격에선 정상) →
  test-only 수정. 권장: 테스트에서 타이핑 자동 진행을 정지(`ui.say.speed=0` 등)시켜 완성을 클릭으로만 일어나게
  하고, 연속 5회 이상 반복 실행으로 결정성 입증.
- **[P3] Case 6.2 Choice flow 포트**: `_c(2, 1, 3, 0)`은 항목 0의 flow를 port 1(=조건 Data 입력 자리)에 걸어
  dead connection이다. flow 출력 포트는 항목 i = port i이므로 `_c(2, 0, 3, 0)`로 수정. 현재는 선택 후 단언이
  없어 미관측 통과.
- **[P3]** Case 5 전용 함수 없음(Case 2/3/3b 흡수 — 허용), `_click_button` 주석이 실제(`pressed.emit()`)와 불일치.
- **[Process]** 리뷰 전 완료 마킹(Open-Tasks Recently Completed 이동, Current-State "완료", 완료 Review)은
  사실과 달라 본 리뷰에서 rework 상태로 정정했다.

잘 된 부분: 테스트 경로/fixture 선택은 설계대로 옳고(`Dialogue_UI.tscn` + `ui.play` + `Button.pressed.emit()`,
in-code `runtime_nodes`), 타이밍 무관 단언(`say.text` 누적, `_say_line_index`/`_say_visible_text` reset,
`waiting_for`, `current_node_id`, Case 6/7 누수 가드)은 3회 전부 통과했다.

### Step 1 Rework 해결 결과 및 재리뷰 검증 [Done]

2026-06-18 지적된 P1/P3 이슈를 해결하고 재검증을 완료했다:

- **[P1] 테스트 비결정성 해결**: `_setup_ui` 헬퍼 함수에서 `ui.say.set_process(false)`를 호출하여 타이핑 자동 연출을 완전히 차단하였다. 이를 통해 프레임 delta 가변에 영향을 받지 않고 오직 클릭 시에만 단계적으로 텍스트가 완성 및 진행되도록 결정성을 고정했다. 수정 후 5회 연속 반복 테스트를 수행하여 100% PASS를 확인하여 결정성을 입증하였다.
- **[P3] Case 6.2 Choice flow 포트 수정**: Choice의 첫 번째 항목(index 0)의 flow 출력 포트는 port 0이므로, `_c(2, 1, 3, 0)`로 잘못 배선되어 있던 부분을 `_c(2, 0, 3, 0)`로 수정하여 오배선을 바로잡았다.
- **[P3] 주석 불일치 수정**: `_click_button` 헬퍼 함수의 주석을 실제 본문 구현(`Button.pressed.emit()`)과 일치하도록 정정하였다.

## Completion Criteria

- 실제 `Dialogue_UI.tscn` 클릭 경로에서 한 줄/여러 줄 LF/빈 줄/CRLF Say의 누적·클릭 순서·Flow 진행이 headless
  테스트로 검증된다.
- 마지막 줄 이후에만 다음 Flow로 진행하고, Say/Choice/End 전환 및 반복/교체 시 페이징 상태가 초기화됨을 단언한다.
- SCRIPT ERROR 0, `--import` 0 parse error, 지정 회귀 GREEN.
- DT-003 Follow-up "실제 클릭/headless 회귀 검증"과 [[Current-State]] Known Gaps "정적 검토만 완료"가 해소된다.
- 발견된 불일치는 Failure/Mismatch Policy에 따라 처리·기록된다.

## Related

- [[DT-003-Say-Line-Paging]]
- [[DialogueTool]]
- [[DialogueTool-Architecture]]
- [[STEP_REVIEW_WORKFLOW]]
