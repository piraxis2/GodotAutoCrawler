---
id: DT-008-Review
type: review
system: DialogueTool, WorldState
created: 2026-06-15
updated: 2026-06-15
tags: [review, dialogue, world-state, condition, branch, choice]
---

# DT-008 State Condition Dialogue Integration Review

[[DT-008-State-Condition-Dialogue-Integration]] Step 1~5의 구현·수정·검증 기록과 완료 판정 추적이다.
설계는 [[ADR-009-State-Condition-Dialogue-Consumption]](accepted), 조건 계층은 [[DT-007-Condition-Review]]를 따른다.

## Step별 판정

| Step | 내용 | 판정 |
| --- | --- | --- |
| 0 | Design Review(D1~D7, F1~F5 확정) | Approved after design fixes |
| 1 | Runtime State Condition Data Node | 수정 후 완료(P1 1건 수정) |
| 2 | Editor Authoring + Resource Round-trip | 수정 후 완료(P2 2건: 노출 위험 해소 + inline 왕복/ERROR 정리) |
| 3 | Branch End-to-End Integration | 수정 후 완료(P1 1건: 폐기 평가 검출 강화) |
| 4 | Conditional Choice Runtime Mapping | 수정 후 완료(P1 1건: error-dominance 전파) |
| 5 | Editor 연결 보존 + 완료 판정 | **Approved after design fixes**(P2 문서 정합 처리) |

**전체 판정: 완료.** Step 0~5 구현·검증, P0/P1 없음. Step 5 완료 판정 리뷰에서 제품 코드는 설계와 일치함을
확인받았고, 남은 P2(현재-사실 문서 3곳의 미구현 표기)는 완료 사실로 갱신했다.

## 주요 리뷰 지적과 처리(중요도순)

- **[P1] signal listener의 분기 변조(Step 1) — 수정.** GDScript signal은 Dictionary를 참조 전달하므로,
  발행 뒤 같은 report에서 `passed`를 읽으면 동기 listener가 분기를 뒤집을 수 있었다. 발행 전 `passed`
  캡처 + signal에 `report.duplicate(true)` deep copy. 회귀 O(취약 코드로 되돌리면 실패 확인).
- **[P1] Expression이 오류 조건을 true로 뒤집음(Step 4) — 수정.** state_condition의 invalid를 단순 false로
  collapse하면 `not c`/`c or true`가 오류 조건을 노출시켰다(ADR-008 error-dominance 위반). 내부 Data
  평가를 `_eval_data → {value, errored}`로 전파하고 Branch/Choice가 errored면 무조건 false/숨김. 회귀
  L1~L4(취약 코드로 되돌리면 L1/L2 실패 확인).
- **[P1] 폐기된 조건 평가 미검출(Step 3) — 수정.** 같은 프레임 교체 시 say만 보면 폐기 player의 평가를
  놓쳤다. 두 player의 `condition_evaluated`와 폐기 provider read 횟수를 각각 단언(폐기 0/활성 1).
- **[P2] 미완성 노드 에디터 노출 / inline 왕복·ERROR 정리(Step 2) — 수정.** 정식 GraphNode/Adapter 등록으로
  빈 노드 노출 해소, inline ConditionSet 에디터 왕복 케이스 추가, 실제 `dialoguetool_main.tscn` fixture로
  교체해 "Node not found" ERROR 0건 종료.
- **[P3] Current-State 판정 stale/중복(Step 4 리뷰) — 수정.** Step 1~4 판정 라벨 갱신 + 중복 회귀 텍스트 제거.

## 완료 검증(Step 5)

- 에디터: State Condition boolean output ↔ Choice 항목별 Data 입력 연결이 저장/재로드 후 동일하고,
  Choice resize(3→2)가 남은 항목의 조건/Flow 연결을 잘못 재배치하지 않으며 사라진 항목 연결만 드롭한다
  (`dt008_step5_completion_test` A).
- 런타임: 복합 `Branch(state_condition) + conditional Choice` 그래프가 실제 `WorldStateStore` 상태에 따라
  같은 evaluator 계약으로 일관 동작(B1~B4).
- 전체 회귀 26/26 GREEN(DT-004~008), editor `--import` 0 오류.

## 헤드리스 테스트 목록(DT-008)

- `dt008_step1_state_condition_test`(15, P1 회귀 O)
- `dt008_step2_snapshot_spike`(F4 external/inline) / `dt008_step2_editor_roundtrip_test`
- `dt008_step3_branch_e2e_test`(DialogueManager e2e, watchdog)
- `dt008_step4_conditional_choice_test`(11 + L1~L4 error-dominance)
- `dt008_step5_completion_test`(에디터 조건 연결/resize + 복합 e2e)

## 후속(범위 밖)

- State Set/Add/Multiply Effect와 mutation provider 주입
- schema-aware key/operator picker, 조건 trace inspector UI, DialogueHistory
- disabled-choice + reason UI, inline ConditionSet tree editor

## Related

- [[DT-008-State-Condition-Dialogue-Integration]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[DT-007-Condition-Review]]
- [[DialogueTool]]
- [[World-State-System]]
