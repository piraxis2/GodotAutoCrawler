---
id: DT-012-Review
type: review
task: DT-012
status: completed
date: 2026-06-17
system: DialogueTool, WorldState
---

# DT-012 Condition Authoring UX Review

## 발견 사항

P0/P1/P2 발견 사항 없음.

문서와 시스템 설명은 실제 구현과 일치한다. `ConditionSummary`는 validate-first/provider-free helper이고,
`WorldStateConditionNode`는 picker path를 유지한 채 별도 `SummaryLabel`로 요약/tooltip/invalid 색을 표시한다.
런타임 evaluator/report/provider 계약과 `ConditionSet` 저장 포맷은 바뀌지 않았다.

## 검토 내용

- `addons/dialogtool/world_state/condition/condition_summary.gd`
  - `ConditionValidator.validate()`를 먼저 호출한다.
  - null/invalid면 트리를 순회하지 않고 `No ConditionSet` 또는 `Invalid: <code>`를 반환한다.
  - valid일 때만 leaf/group을 표시용 기호와 `ALL`/`ANY`/`NOT` 라벨로 요약한다.
  - INT/FLOAT, String/StringName, bool literal 표기를 구분하고 문자열 제어문자를 escape한다.
  - `summary`는 max length로 자르고 `full_summary`/`tooltip`은 전체 정보를 보존한다.
- `addons/dialogtool/Node/world_state_condition_node.gd(.tscn)`
  - GraphNode에 `SummaryLabel`이 추가되어 picker path와 별도로 조건 의미를 표시한다.
  - `set_condition_set`, picker drop, clear 시점에 `_refresh_summary()`가 실행된다.
  - invalid/null은 흰색이 아닌 빨강 계열 modulate로 구분된다.
  - label clipping/ellipsis/custom minimum size로 긴 요약이 노드 폭을 과도하게 키우지 않는다.
- `LLM_WIKI/20_Systems/DialogueTool-User-Guide.md`
  - 자동 요약, literal 표기, description valid-only 우선, invalid/null 우선, tooltip, 갱신 시점,
    live external edit 미구독 한계를 설명한다.
- `LLM_WIKI/20_Systems/DialogueTool.md`
  - `ConditionSummary` validate-first/provider-free 표시와 런타임 평가 계약 분리를 현재 사실로 기록한다.
- `LLM_WIKI/00_Index/Open-Tasks.md`
  - DT-012 본 작업은 Next에서 제거하고, inline tree editor/schema-aware picker/trace inspector만 Later 후속으로
    유지한다.

## 검증 결과

- Godot 4.6.3 mono headless `--import`: exit 0, parse/class error 없음.
- 선택 회귀 7/7 PASS:
  - `addons/dialogtool/world_state/condition/tests/dt012_step1_condition_summary_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt012_step2_node_display_test.tscn`
  - `addons/dialogtool/world_state/condition/tests/dt007_step1_validation_test.tscn`
  - `addons/dialogtool/world_state/condition/tests/dt007_step2_evaluator_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt008_step2_editor_roundtrip_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt008_step5_completion_test.tscn`
  - `addons/dialogtool/RunTime/tests/dt010_step3_editor_play_e2e_test.tscn`

## 검증하지 못한 내용

- 실제 GUI에서 외부 `.tres`를 Inspector로 편집한 직후의 live refresh는 범위 밖이다. 현재 계약은
  drop/clear/load/reload 시점 갱신이다.

## 잔여 위험

- schema-aware key/operator picker, inline ConditionSet tree editor, condition trace inspector는 후속 작업이다.
- `--import` 종료 시 Godot resource leak 경고가 출력됐다. parse/class/import 실패는 아니며 DT-012 완료를 막는
  문제로 분류하지 않았다.

## 판정

**완료**.

DT-012 completion criteria를 충족한다. 그래프 위에서 State Condition의 의미를 읽을 수 있고, null/invalid가
정상 조건처럼 보이지 않으며, 저장/재로드와 런타임 평가 계약은 유지된다.
