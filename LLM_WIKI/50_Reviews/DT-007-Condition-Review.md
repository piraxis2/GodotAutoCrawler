---
id: DT-007-Review
type: review
system: WorldState
date: 2026-06-14
status: complete
target: [[DT-007-ConditionSet-ConditionEvaluator]]
---

# DT-007 ConditionSet and ConditionEvaluator 리뷰

DT-005/006의 타입 안전 World State 위에 구조화·결정론적 조건 평가 계층을 올린 Step 0~4에 대한 통합
리뷰다. 판정은 코드와 headless 실행 결과를 기준으로 한다. 구현 세부는
[[DT-007-ConditionSet-ConditionEvaluator]], 설계 근거는 [[ADR-008-Structured-Condition-Evaluation]].

## 범위

- Step 0: 설계 리뷰 — Resolutions 1~7 확정, ADR-008 accepted.
- Step 1: Condition Resource 모델(`ConditionClause` @abstract / `StateCondition` / `ConditionGroup` /
  `ConditionSet`)과 `ConditionValidator` 구조 검증(iterative, strict tree, 한계).
- Step 2: `ConditionEvaluator` pure-read 평가(strict 비교, ALL/ANY/NOT, key cache, 전체 trace, fail-closed).
- Step 3: 실제 `WorldStateStore` provider 주입 통합(제품 코드 변경 없음).
- Step 4: end-to-end(`.tres` 왕복 + Store lifecycle + trace parity), 성능 sanity, 전체 회귀, 문서 완료.

## 구현 산출물

- `Assets/Script/gds/world_state/condition/`: `condition_clause.gd`, `state_condition.gd`,
  `condition_group.gd`, `condition_set.gd`, `condition_validator.gd`, `condition_evaluator.gd`.
- 테스트: `condition/tests/dt007_step1_validation_test`(24), `dt007_step2_evaluator_test`(23),
  `dt007_step3_store_integration_test`(11), `dt007_step4_e2e_test`(5 그룹), `dt007_spike_resource_roundtrip`.

## 핵심 계약 (후속 소비 Task 입력)

- `ConditionEvaluator.evaluate(condition_set: ConditionSet, read_provider) -> Dictionary`.
  - 반환: `{passed: bool, valid: bool, errors: Array[{code,path,key,message}], trace: Dictionary, read_count: int}` — 호출별 deep copy.
  - `valid := errors.is_empty()`. `valid==false`면 `passed`는 항상 false.
- read_provider 계약: `has_state(key: StringName) -> bool`, `read_state(key: StringName) -> Variant`.
  `WorldStateStore`(=`/root/WorldState`)가 그대로 만족한다. 비-Object/freed/메서드 누락/arity·arg 타입
  위반/`has_state` 비-bool 반환은 `provider_missing`/`provider_contract_invalid`로 fail-closed.
- 2단계 평가: 구조 검증(provider read 0) 통과 후에만 provider를 읽는다. 같은 key는 호출 내 1회 read(miss 포함).
- trace 노드: group `{kind:"group", logic, path, passed, children}`; leaf `{kind:"state", path, key,
  operator, expected, actual, passed}`(에러 leaf는 `actual:null` + `error:<code>`). operator/logic 문자열은
  안정 계약(`equal|not_equal|less|less_equal|greater|greater_equal`, `all|any|not`). root path=`[]`.
- 오류 코드: 구조(`condition_set_null`/`root_null`/`clause_unknown`/`group_empty`/`not_arity_invalid`/
  `logic_invalid`/`cycle_detected`/`clause_aliased`/`depth_limit_exceeded`/`node_limit_exceeded`/`key_empty`/
  `key_invalid_format`/`operator_invalid`/`expected_type_invalid`/`ordered_type_invalid`), 런타임(`provider_missing`/
  `provider_contract_invalid`/`state_missing`/`actual_type_mismatch`).

## 검증 결과 (Godot 4.6.3 mono headless)

| 테스트 | 범위 | 결과 |
| --- | --- | --- |
| `dt007_spike_resource_roundtrip` | @abstract 인식 + 재귀 typed Resource `.tres` 왕복 | ALL PASS |
| `dt007_step1_validation_test` | 구조/한계/key/operator/expected/ordered/path/저장(24 시나리오) | ALL PASS |
| `dt007_step2_evaluator_test` | 타입/논리 truth table/cache/provider(비-Object·arity·arg·반환)/fail-closed/trace(23 그룹) | ALL PASS |
| `dt007_step3_store_integration_test` | 실제 Store 주입·set/batch/reset/lifetime/snapshot 재평가·pure read(11 그룹) | ALL PASS |
| `dt007_step4_e2e_test` | `.tres` 왕복 전체 report parity·lifecycle·load lifecycle(restore_world_state, SESSION reset 직접 단언)·성능 sanity·fail-closed 불변(5 그룹) | ALL PASS |
| DT-005 `dt005_step1~6` | schema/store/snapshot/batch/provider/통합 회귀 | ALL PASS |
| DT-006 `dt006_step1~5` | bootstrap/autoload/lifecycle/adapter/통합 회귀 | ALL PASS |
| DialogueTool `dt004_step1~4`(+pipeline) | 런타임/에디터/validation/통합/pipeline 회귀 | ALL PASS |

- editor `--import` 성공(parse 오류 0, exit 0). 단, import 종료 시 `ObjectDB instances leaked` /
  `52 resources still in use`가 출력된다 — 이는 clean import에도 재현되는 기존 editor-import 종료 노이즈
  (DT-005/006 baseline)이지 본 작업의 결함이 아니다. 별개로, 조건 테스트의 cycle/self-ref *fixture* 누수는
  단언 후 참조 해제로 정리해 **해당 테스트** 종료 시에는 누수 경고가 없다. 음성 경로 `push_error`는 의도된
  fail-closed 로그다.

## 리뷰 이력 (수정 완료된 지적)

- **Step 1**: (P2) self/indirect cycle fixture가 실제 순환 참조 누수 → 단언 후 `children.clear()`로 해소.
  `logic_invalid`(손상 logic enum fail-closed) 추가 비준, null child→`clause_unknown` 분류 허용.
- **Step 2**: (P1) 비-Object provider에서 `has_method()` 실패·arity/반환 미검사로 평가 중 SCRIPT ERROR →
  reflection 사전 검사(Object/arity/StringName arg 타입/`has_state` 반환 bool)로 강화. 추가 P1 재검토:
  미선언 반환의 truthy 누수 → has_state 반환을 Variant로 받아 런타임 `typeof()==TYPE_BOOL` 검사,
  read_count==1 정책 명시. 회귀 G2~G7 추가.
- **Step 2(P2)**: spike `inst.free()`가 RefCounted에 SCRIPT ERROR(false-green) → 수동 free 제거.
- **Step 3(P2)/Step 4 문서**: System/Current-State의 stale "Step 3 미구현" 모순 정리, 테스트 수치 동기화.

## 발견 사항 (현재)

- P0/P1: 없음.
- P2/P3: 없음.

## Accepted Debt / 의도된 한계

- **state-to-literal 비교만**: state-to-state, time, random, scene function operand는 후속 operand 설계 필요(ADR-008 D1).
- **숫자 ordered만**: String/StringName lexical ordering·암시적 int↔float 미지원. int vs float authoring 마찰은
  문서화됨(Resolution 5/7). schema-aware key picker는 후속 editor 통합.
- **non-short-circuit 단일 모드**: fast mode는 규모 측정 후에만 추가(Risk 4).
- **trace는 actual 값 포함**: 민감 정보 redaction 정책은 후속(Risk 3).
- **Inspector 클릭 authoring은 headless 미검증**: spike가 직렬화 backbone(`.tres` 왕복)만 보장.

## Follow-up 입력 계약 (State Condition Dialogue node Task)

후속 Task는 위 "핵심 계약"을 그대로 입력으로 받는다. 추가로:

- Dialogue Condition 노드는 `ConditionSet`을 data로 보유하고, Branch가 `evaluate(...).passed`로 분기한다.
  `DialoguePlayer`는 이미 read provider를 주입받으므로 같은 provider를 evaluator에 넘기면 된다.
- 조건부 Choice/Response Selector도 동일 evaluator/trace를 공유한다(ADR-008 D2).
- mutation(Set/Add/Multiply State Effect)은 별도 mutation provider 주입이 필요하며 본 계층 범위 밖이다.

## 판정

- **수정 후 완료**.
- Step 0~4 완료 조건 충족, P0/P1 없음, headless editor load 성공, DT-004/005/006 회귀 유지.
- 리뷰에서 발견된 provider 계약, cycle fixture, RefCounted 정리, lifecycle false-green, 문서 불일치는
  회귀 테스트와 재검증을 포함해 모두 수정됐다.

## Related

- [[DT-007-ConditionSet-ConditionEvaluator]]
- [[ADR-008-Structured-Condition-Evaluation]]
- [[World-State-System]]
- [[World-State-User-Guide]]
- [[DT-006-WorldState-Runtime-Review]]
