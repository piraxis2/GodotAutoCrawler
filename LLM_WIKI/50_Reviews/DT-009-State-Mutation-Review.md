---
id: DT-009-Review
type: review
system: DialogueTool, WorldState
created: 2026-06-16
updated: 2026-06-16
tags: [review, dialogue, world-state, mutation, effect]
---

# DT-009 State Mutation Dialogue Effects Review

[[DT-009-State-Mutation-Dialogue-Effects]] Step 0~4와 Step 3b의 구현·수정·검증 기록과 완료 판정이다.
설계 결정은 [[ADR-010-State-Mutation-Dialogue-Effects]](accepted)를 따른다.

## Step별 판정

| Step | 내용 | 판정 |
| --- | --- | --- |
| 0 | Design Review(D1~D10 확정) | Approved after design fixes |
| 1 | `WorldStateStore.add_state` 원자 Add provider API | 수정 후 완료(P1/P2 수정) |
| 2 | Runtime mutation provider 주입 + `state_set`/`state_add` dispatch | 수정 후 완료(P1/P2 수정) |
| 3 | State Set/Add editor authoring + `.tres` round-trip | 수정 후 완료(P1/P2 수정) |
| 3b | Choice 항목별/공통 Effect authoring | 수정 후 완료(P1/P2 수정) |
| 4 | Manager/UI/Player/Store end-to-end completion | Approved after design fixes |

**전체 판정: 완료.** Step 0~4 + 3b 구현·검증, P0/P1 없음. State mutation은 명시적 mutation provider를 통해서만
수행되며, read provider 자동 승격은 없다.

## 주요 리뷰 지적과 처리

- **[P1] Add delta 자체가 도메인 밖이어도 상쇄되면 통과 — 수정.** `add_state`가 결과만 검사하던 것을
  delta 선검사로 바꾸어 JSON-safe 범위 밖 INT delta와 INF/NAN FLOAT delta를 `out_of_domain`으로 거부한다.
- **[P1] 잘못된 mutation provider가 SCRIPT ERROR — 수정.** provider 검증을 `typeof`/`is_instance_valid`,
  arity, 인자 타입, typed array 원소 타입(`Array[Dictionary]`), 반환 Dictionary 스키마 검증까지 강화했다.
- **[P1] listener가 provider를 교체해 같은 Effect chain 후속 mutation을 오염 — 수정.** `_run_effects` 진입 시
  mutation provider를 고정해 같은 chain의 후속 Effect는 동일 provider를 사용한다.
- **[P1] 잘못된 literal이 조용히 유효 값으로 변환 — 수정.** `coerce_to_type` 제거. capture는 엄격 파싱하고,
  실패한 literal은 원본 String으로 보존해 저장 validation과 Store strict typing이 거부하게 한다.
- **[P1] 공통 Choice Effect가 왕복 후 항목0 전용으로 오염 — 수정.** Choice에 전용 공통 Effect 포트를 추가하고
  capture/load/resize에서 `choice_index` 없는 공통 연결을 별도로 보존한다.
- **[P1] 손상된 `choice_index` 타입이 런타임 SCRIPT ERROR 또는 명시적 null 공통 실행 — 수정.**
  `connection.has("choice_index")`로 필드 부재와 명시적 값을 구분한다. 필드 없음은 공통, 유효 int는 항목/명시적 공통,
  명시적 `null`/String/Dictionary는 fail-closed로 건너뛴다. 에디터 load와 런타임 계약이 일치한다.

## 완료 검증

- Store API: `dt009_step1_add_state_test` A~U ALL PASS.
- Runtime dispatch/provider: `dt009_step2_runtime_mutation_test` A~U ALL PASS.
- Editor round-trip: `dt009_step3_editor_roundtrip_test` A~F ALL PASS.
- Per-choice/common Choice Effect: `dt009_step3b_per_choice_effect_test` A~G ALL PASS.
- End-to-end: `dt009_step4_e2e_completion_test` A~G ALL PASS.
- 전체 회귀: DT-004 Step1~4(+pipeline), DT-005 Step1~6, DT-006 Step1~5, DT-007 Step1~4,
  DT-008 Step1~5, DT-009 Step1/2/3/3b/4. Godot 4.6.3 headless `--import` 0 오류.

Step 4 핵심 e2e:

- `take` 선택: Choice 항목0 `state_add(gold,+50)` 실행, gold 100→150, 바로 다음 Branch `gold >= 150`이 true,
  Say "Rich", mutation report `{operation:"add", old_value:100, new_value:150}` 1회.
- `leave` 선택: mutation 없음, gold 100, Branch false, Say "Poor".
- provider 누락/read-only 실패는 값 불변 + 구조화 report + Flow 계속.
- same-frame 교체는 폐기 provider mutation 0회.
- 에디터 authored Choice+항목별 Add 리소스가 save→reload 후 동일하게 실행된다.

## Accepted Debt / 후속

- 실제 SaveGame file/slot 시스템은 별도 Task에서 `WorldStateRuntime.capture_world_state()` /
  `restore_world_state(snapshot)` adapter를 소비한다.
- State Read Data 노드는 아직 없다. 현재 읽기는 State Condition 또는 게임 코드의 provider API가 담당한다.
- schema-aware key picker, condition/mutation trace inspector, disabled-choice reason UI는 후속 UX 작업이다.
- runtime INT/FLOAT 도메인은 JSON-safe INT와 finite FLOAT 정책을 유지한다.

## Related

- [[DT-009-State-Mutation-Dialogue-Effects]]
- [[ADR-010-State-Mutation-Dialogue-Effects]]
- [[DT-008-Choice-Integration-Review]]
- [[World-State-System]]
- [[DialogueTool]]
