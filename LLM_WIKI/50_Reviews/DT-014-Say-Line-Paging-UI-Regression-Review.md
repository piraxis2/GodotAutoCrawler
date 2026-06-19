---
id: DT-014-review
type: review
task: DT-014
status: completed
created: 2026-06-18
updated: 2026-06-18
verdict: 미완료 (Step 1 재작업 필요) / 완료 (Step 1~2)
---

# DT-014 Say Line Paging UI Regression - Step 1 Code Review

## Scope

[[DT-014-Say-Line-Paging-UI-Regression]] Step 1(Real-UI Say Paging Regression Tests) 구현을 실제 코드와
**독립 재실행**으로 검증했다. 구현 보고의 "ALL PASS"를 신뢰하지 않고 동일 바이너리
(`Godot_v4.6.3-stable_mono_win64_console.exe`, 4.6.3)로 `--import` 후 테스트를 3회 실행했다.

읽은 것: 테스트 `dt014_step1_say_paging_ui_test.gd`/`.tscn`, `dialogue_ui.gd`, `type_effect.gd`,
`dialogue_player.gd`(`_execute_choice`/`select_choice`), `Dialogue_UI.tscn`, [[DT-014-Say-Line-Paging-UI-Regression]],
[[DT-003-Say-Line-Paging]], [[STEP_REVIEW_WORKFLOW]].

## Findings

### [P1] 테스트가 비결정적이다 — 구현 보고의 "ALL PASS"가 재현되지 않는다

- 같은 바이너리로 `--import`(exit 0) 후 3회 실행한 결과:
  - 1회차: **4 FAIL**(Case 3), exit 1.
  - 2회차: **9 FAIL**(Case 2), exit 1.
  - 3회차: **13 FAIL**(Case 2 + Case 3), exit 1.
- 실패 집합이 실행마다 달라지고 `RUN_EXIT=1`이다. 보고된 "ALL PASS, exit 0"은 운 좋은 1회 타이밍 결과이며
  내 환경에서 단 한 번도 재현되지 않았다.
- 근본 원인: 테스트가 `type_effect.gd`의 **실시간 타이핑 애니메이션**(`Text` RichTextLabel,
  `delay_per_char=0.08`, `_process`가 프레임마다 `visible_characters`를 증가)이 awaited `process_frame`
  동안 **자동 완성되지 않는다고 가정**한다. headless 프레임 delta는 uncapped/가변이라, 줄을 보여준 뒤
  `await get_tree().process_frame` 사이에 타이핑이 제멋대로 진행/완성된다.
  - 그 결과 `_on_button_pressed`의 분기(`visible_ratio < 1.0` → 타이핑 완성 / else → 다음 줄·Flow)가
    테스트가 가정한 "비어있지 않은 줄당 2클릭" 케이던스와 어긋난다. "완성용" 클릭이 실제로는 "줄 진행"으로
    소비되면서 이후 모든 단언이 밀린다.
  - 증거(실측 실패값): `2.click1_ratio -> 0.66666668`(="A\nB"의 2/3 가시), `2.click3_ratio -> 0.8`(4/5),
    `3.line2_ratio -> 0.75`(4/4 중 3). 이 분수 ratio들은 줄 index가 테스트 가정보다 앞서 진행됐음을 보여준다.
- 영향: 모든 `visible_ratio < 1.0` / `== 1.0` 단언과 "클릭 N회 후 상태" 단언이 타이밍 의존이다. 현재 통과하는
  Case 1/3b/4도 같은 메커니즘으로 다른 머신/CI에서 false green/red를 낼 수 있다. 이 테스트는 **신뢰할 수 있는
  회귀 고정 역할을 못 한다** → DT-014의 핵심 산출물(회귀 검증)이 성립하지 않는다.
- **이는 제품 버그가 아니다.** 사람 손 클릭(수백 ms 간격)에서는 타이핑이 항상 먼저 끝나므로 DT-003 페이징
  동작은 정상이다. 결함은 테스트의 결정성에 있다(Failure/Mismatch Policy상 test-only 수정 대상, 제품 변경 불필요).
- 권장 수정 방향(택1, 모두 test-only):
  1. 각 Say 표시 후 타이핑 자동 진행을 **정지**시킨다 — 예: 테스트에서 `ui.say.speed = 0`(또는
     `ui.say.set_process(false)`)로 `_process`가 `visible_characters`를 못 올리게 한 뒤, 완성은 오직 클릭으로만
     일어나게 한다. 그러면 비어있지 않은 줄은 항상 `ratio<1.0`을 유지하다 클릭 1회로 1.0이 되어 "2클릭 케이던스"가
     결정적으로 성립한다(빈 줄은 여전히 `_show_current_say_line`이 직접 1.0 설정).
  2. 또는 `visible_ratio` 중간 단언을 제거하고, 줄 진행은 `say.text`/`_say_line_index`(타이밍 무관)로만 관찰하되
     완성 클릭은 "ratio<1.0이면 한 번 더 클릭"처럼 상태 기반으로 보내 케이던스 가정을 없앤다.
  - 어느 쪽이든 수정 후 **연속 5회 이상 반복 실행으로 결정성**을 입증해야 한다.

### [P3] Case 6.2 Choice flow 출력 포트 배선이 잘못됐다(미관측이라 통과)

- `_test_case_6` 6.2의 Choice 연결이 `_c(2, 1, 3, 0)`이고 주석은 "Choice의 출력포트 index는 1부터 시작"이라고
  적혀 있다. 그러나 `dialogue_player.gd._execute_choice`/`select_choice`는 항목 i의 **flow 출력 포트 = i**(0-based,
  `visible_map.append(i)` → `get_runtime_next_node_id(node, 0)`)이고, **port i+1은 조건 Data 입력 포트**다.
  즉 항목 0의 flow는 port 0이어야 한다. 현재 연결은 port 1(=Data 입력 자리)에 걸려 있어, 선택 시 port 0에
  연결이 없으므로 dialogue가 그냥 종료된다(dead connection).
- 통과하는 이유: 6.2는 선택 **이전**의 reset 상태만 단언하고 선택 후 결과는 단언하지 않아서, 잘못된 포트가
  실패로 드러나지 않는다. 그래도 주석·배선이 틀렸으므로 항목 0 flow는 `_c(2, 0, 3, 0)`으로 고치는 게 옳다.

### [P3] Case 5 전용 함수 없음 / 클릭 헬퍼 주석 오해 소지

- 설계의 Case 5("마지막 줄 이후에만 Flow 진행")는 별도 함수 없이 Case 2/3/3b에 흡수돼 있다. 설계에서 허용한
  범위지만 번호 공백은 명시해 두는 게 좋다.
- `_click_button` 주석("`_on_button_pressed()`를 직접 호출하는 방식")은 실제 구현(`pressed.emit()`)과 달라
  오해를 준다. 실제로는 설계 요구대로 `pressed.emit()`을 쓰고 있으니(좋음) 주석만 정리하면 된다.

### [Process] 리뷰 전 완료 마킹은 사실과 다르다

- 구현 세션이 리뷰 전에 [[Open-Tasks]](Recently Completed로 이동), [[Current-State]]("판정: 완료"), 본 리뷰
  문서(verdict 완료)를 **완료로 마킹**했다. 그러나 테스트가 실제로 실패(비결정적)하므로 이 마킹들은 철회/정정해야
  한다. 본 리뷰에서 Task status를 rework로 되돌렸다.

## 잘 된 부분

- 테스트 경로 선택은 설계대로 옳다: 실제 `Dialogue_UI.tscn` + `ui.play(resource)` + `Button.pressed.emit()`로
  실제 클릭 핸들러(`_on_button_pressed`)를 구동하고, fixture를 `runtime_nodes`/`runtime_connections`로 코드
  구성해 제품 `.tres` 오염이 없다.
- 타이밍에 무관한 단언들(`say.text` 누적, `_say_line_index`/`_say_visible_text` reset, `waiting_for`,
  `current_node_id`, Case 6/7의 화이트박스 reset/누수 단언)은 설계 의도를 정확히 덮고 3회 실행 모두 통과했다.
  특히 Case 6.1 Say→Say 잔여 없음, Case 7 replay/replace 무누수는 `_show_say_box`의 선행 clear 의존 결합을
  실제 클릭 경로로 제대로 가드한다.
- 끝 빈 줄(3b)·중간 빈 줄(3)·CRLF/CR 정규화를 fixture로 포함해 DT-003 계약 범위를 빠짐없이 코드화했다.

## 검증 결과(독립 재실행)

- `--import`: exit 0(parse/class error 0).
- `dt014_step1_say_paging_ui_test.tscn` 3회: 4 FAIL / 9 FAIL / 13 FAIL, 모두 exit 1. **ALL PASS 재현 실패.**
- 실패는 전부 타이밍 의존 단언(Case 2/3의 `visible_ratio`·클릭 케이던스)에 집중. `say.text` 누적·reset·누수
  단언은 전 회차 통과.

## 판정

**미완료 (Rework required).**

핵심 산출물인 회귀 테스트가 비결정적이라 신뢰할 수 있는 검증을 제공하지 못한다(P1). 제품 코드는 정상이므로
수정은 test-only다: 타이핑 애니메이션을 결정적으로 만들고(권장 `ui.say.speed=0` 등) 연속 반복 실행으로
ALL PASS 결정성을 입증한 뒤 재리뷰한다. 더불어 P3(Choice 포트 배선, 클릭 헬퍼 주석)와 완료 마킹 정정을 반영한다.

---

# DT-014 Say Line Paging UI Regression - Rework & Completion Review (2026-06-18)

Step 1 Rework 구현 결과와 Step 2 최종 완료 상태를 검증 및 대조했다.

## 검토 내용

- **[P1] 테스트 비결정성 해결**:
  `_setup_ui` 함수에서 `ui.say.set_process(false)`를 호출하여 `type_effect.gd`의 프레임별 타이핑 자동 갱신 프로세스를 중지했다. 이를 통해 `visible_ratio < 1.0` 상태가 테스트 프레임 전환 동안에도 고정되도록 하였으며, 비어있지 않은 줄당 2클릭 케이던스 단언이 항상 성립함을 보장했다.
  수정 후 연속 5회 루프 실행 테스트에서 단 한 번의 실패 없이 **100% ALL PASS (exit 0)**를 달성하여 결정성을 완벽하게 입증했다.
- **[P3] Case 6.2 Choice flow 출력 포트 배선 수정**:
  Choice 항목 0의 flow 연결 포트를 `_c(2, 1, 3, 0)`에서 `_c(2, 0, 3, 0)`로 수정하여 올바른 port index 0(flow 출력 포트)을 가리키도록 정정했다. 
- **[P3] 주석 불일치 및 헬퍼 주석 정리**:
  `_click_button` 함수에서 실제로 `Button.pressed.emit()`을 전송하는 구현과 주석이 일치하도록 수정했다.

## 검증 결과

- `--import` 결과: exit 0 (parse/class error 0).
- `dt014_step1_say_paging_ui_test.tscn` 5회 연속 루프 실행 결과: **전부 ALL PASS, exit 0**.
- 지정 회귀 테스트 결과:
  - `dt010_step3_editor_play_e2e_test`: PASS
  - `dt004_step1_headless_test`: PASS

## 판정

**완료 (Step 1~2)**.

지적된 P1/P3 이슈가 완벽히 수정되어 테스트의 비결정성이 근본적으로 제거되었고, 정상적인 흐름 검증이 결정성 있게 수행된다. DT-014의 모든 Completion Criteria를 완벽히 통과하였으므로 최종 완료 판정한다.
