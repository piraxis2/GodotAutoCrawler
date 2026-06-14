---
id: DT-005-Review
type: review
system: WorldState
date: 2026-06-12
status: 완료
target: [[DT-005-StateSchema-WorldStateStore]]
---

# DT-005 World State 통합 리뷰

타입 안전 World State 기반(StateDefinition/StateSchema/WorldStateStore)과 Dialogue read provider
seam의 Step 1~6 구현에 대한 통합 리뷰다. 판정은 코드와 헤드리스 실행 결과를 기준으로 한다.

## 범위

- Step 1: `StateDefinition`/`StateSchema` — 타입/default/lifetime 선언, validation, key lookup.
- Step 2: `WorldStateStore` read/write/reset + `value_changed`, 계약 compile.
- Step 3: SAVE/SESSION lifetime, `reset_lifetime`, JSON snapshot export/import(replace-load).
- Step 4: `apply_batch` atomic mutation.
- Step 5: Dialogue read 상태 provider seam(`DialogueManager`→`DialogueUI`→`DialoguePlayer`).
- Step 6: 통합 회귀 + 본 리뷰.

## 검증 결과 (Godot 4.6.3 mono headless)

| 테스트 | 범위 | 결과 |
| --- | --- | --- |
| `dt005_step1_schema_test` | schema validation 행렬·lookup·무효화·`.tres` 왕복 | ALL PASS |
| `dt005_step2_store_test` | ready/read/write/reset/타입·read-only·계약 격리·재초기화 | ALL PASS |
| `dt005_step3_snapshot_test` | lifetime/snapshot 왕복·JSON 타입 복원·import 오류 행렬·원자 발행·JSON-safe int·재진입 | ALL PASS |
| `dt005_step4_batch_test` | atomic batch 성공/실패·입력 순서·중복·key 타입 | ALL PASS |
| `dt005_step5_provider_seam_test` | provider 미지정/fake/Store 주입·Manager 전달·수명주기(latest-wins/폐기 취소) | ALL PASS |
| `dt005_step6_integration_test` | default→batch→set/reset→SAVE/SESSION→JSON 왕복→reset_lifetime→Dialogue 주입 | ALL PASS |
| DialogueTool `dt004_step1~4`(+integration) | 기존 런타임/에디터/validation/통합 회귀 | ALL PASS |

- Godot 4.6.3(mono) headless editor load 성공.
- 종료 시 "ObjectDB instances leaked"/"resources still in use" 경고는 import pass에서도 나타나는
  Godot 종료 시점 양성 경고로, 테스트 실패가 아니다. 음성 경로의 `ERROR:`/`WARNING:` 로그는
  의도된 `push_error`/`push_warning`다.

## 리뷰 이력 (수정 완료된 지적)

리뷰-수정-재검증을 반복했고, 발견된 P1/P2는 모두 해소됐다.

- **Step 1**: (P1) 검증 후 Resource/배열 변경 시 stale lookup → `changed` 시그널 + `definitions`
  구조 지문 기반 무효화. (P2) validation 결과 외부 변조 → deep copy 반환.
- **Step 2**: (P1) 초기화 후 schema 변경이 read/write에 섞임 → init 시점 계약을 private map으로
  compile, 이후 mutable schema 미조회.
- **Step 3**: (P1) import 중 부분 상태 노출 → 전체 반영 후 결정 순서 발행. (P1) 손실 수치 입력 승인
  → finite·정확 정수만, JSON-safe `±(2^53-1)` 범위 강제(쓰기 경계 전체 + FLOAT int wire 포함).
  (P2) report signal/반환 분리·발행 정책 통일. (P1) 알림 중 재진입 mutation(initialize 포함) 거부.
- **Step 4**: (P1) 잘못된 key 타입 런타임 오류 → 구조화된 `malformed_change`. (P2) 중복 검사 독립화.
- **Step 5**: (P1) deferred 시작의 resource/provider 결합 깨짐 → 한 쌍으로 묶어 latest-wins.
  (P1) Manager 폐기 UI의 pending 대화 실행 → `_dismiss`에서 pending 시작 취소.

## 발견 사항 (현재)

- P0/P1: 없음.
- P2/P3: 없음(이월된 설계 한계는 아래 accepted debt 참조).

## Accepted Debt / 의도된 한계

- snapshot/runtime INT는 JSON-safe `±(2^53-1)`로 제한, FLOAT는 finite만 허용. 게임 상태 범위엔
  충분하며, full int64가 필요하면 INT를 canonical decimal String으로 직렬화하는 후속 설계가 필요.
- 알림(value_changed) 발행 중 재진입 mutation은 거부(`ERR_BUSY`)한다. 동기 반환 계약을 흐리지
  않기 위한 선택이며, "콜백 후속 mutation을 별도 transaction으로" 큐잉하려면 deferral 정책을 후속에서.
- mutation provider는 소비 노드가 없어 `DialoguePlayer`에 주입하지 않는다.
- State Read/Set Dialogue 노드와 ConditionEvaluator는 본 Task 범위 밖(후속 Task).
- `WorldStateStore`는 autoload로 등록하지 않았다(테스트는 `new()`+schema 주입). 실제 게임 통합 시
  schema 리소스를 할당한 `.tscn`을 autoload로 연결하는 작업이 남는다.
- `apply_batch` change 형식은 `{key, value}`만 지원(add/multiply 등 연산 Effect는 후속).

## 판정

**완료** — Step 1~6 완료 조건 충족, P0/P1 없음, headless editor load 성공, 기존 DialogueTool 회귀
유지. 후속 작업(ConditionSet/Effect 노드, Store autoload, full int64)은 accepted debt로 명시했다.

## Related

- [[DT-005-StateSchema-WorldStateStore]]
- [[ADR-006-Typed-World-State]]
- [[World-State-System]]
