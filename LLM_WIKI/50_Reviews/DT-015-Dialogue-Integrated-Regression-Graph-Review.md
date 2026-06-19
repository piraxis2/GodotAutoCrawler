---
type: review
task: DT-015
status: completed
updated: 2026-06-19
---

# DT-015 Dialogue Integrated Regression Graph Review

## 발견 사항

### [P3] VariableDef 초기값 설정 오인으로 인한 Nil 데이터 에러 해결
- **문제**: Step 2 테스트 코드(`dt015_step2_editor_authored_roundtrip_test.gd`)에서 `VariableDef` 노드의 프로퍼티를 `.value`라는 존재하지 않는 키로 대입하여 `Nil` 에러가 발생했음.
- **영향**: 런타임 Expression 실행 시 `Nil and int` 비교 에러가 나면서 Strong 분기가 항상 fail 분기로 수렴하는 이슈가 있었음.
- **수정**: `.variable_name`, `.variable_type`, `.variable`로 프로퍼티를 명확하게 나누어 대입하고 테스트를 수행하여 `A >= 5` 비교가 7 및 3 데이터에 대해 각각 `Strong success`와 `Weak fail`을 올바르게 타도록 수정 완료.

## 검증 결과

### 자동 테스트
- `dt015_step1_integrated_graph_test.tscn` (100% 통과, ALL PASS)
- `dt015_step2_editor_authored_roundtrip_test.tscn` (100% 통과, ALL PASS)
- `dt008_step1_state_condition_test.tscn` (지정 회귀 테스트 100% 통과, ALL PASS)
- `dt014_step1_say_paging_ui_test.tscn` (지정 회귀 테스트 100% 통과, ALL PASS)

### 데이터 보존성 검증
- 에디터 authored GraphEdit 캡처 후 `ResourceSaver.save` -> `CACHE_MODE_IGNORE` 재로드 흐름에서 노드 개수(15개), 연결 개수(18개), `start_node_id` (0)가 완벽히 일치 및 복원됨을 단언.
- Choice 항목 0/1/2의 flow output port가 Strong/Weak/Leave 경로로 완벽하게 유지됨을 검증.
- Expression 입력 키 `inputs = ["A"]` 및 expression `"A >= 5"` 정보가 온전히 재로드됨을 확인.

## 판정

**완료** (Step 1 및 Step 2의 모든 성공 기준 만족, P0/P1/P2 결함 없음)
