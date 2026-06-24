---
id: DT-006-Review
type: review
system: WorldState
date: 2026-06-12
status: 완료
target: [[DT-006-WorldState-Runtime-Integration]]
---

# DT-006 WorldState Runtime Integration 리뷰

DT-005의 타입 안전 WorldStateStore를 실제 게임 런타임의 단일 상태 서비스로 연결한 Step 0~5에 대한
통합 리뷰다. 판정은 코드와 헤드리스 실행 결과를 기준으로 한다.

## 범위

- Step 0: 설계 리뷰 — D1~D6 확정, [[ADR-007-WorldState-Runtime-Lifecycle]].
- Step 1: 게임용 bootstrap Schema `.tres`(6 key)와 Store scene.
- Step 2: `WorldState` autoload 등록과 boot readiness.
- Step 3: `WorldStateRuntime` coordinator — new game/load lifecycle, transactional restore, SESSION 정책.
- Step 4: snapshot adapter 경계(`capture_world_state`/`restore_world_state`).
- Step 5: end-to-end 통합 회귀 + 본 리뷰.

## 검증 결과 (Godot 4.6.3 mono headless)

| 테스트 | 범위 | 결과 |
| --- | --- | --- |
| `dt006_step1_bootstrap_test` | bootstrap Schema valid·6 key 계약·Store scene ready·`.tres` 왕복 | ALL PASS |
| `dt006_step2_autoload_test` | 단일 `/root/WorldState` ready·이름 충돌 없음·invalid 명시 실패·churn 보존 | ALL PASS |
| `dt006_step3_lifecycle_test` | new game/restore transactional·SESSION default·보존·busy·Store 교체 보호 | ALL PASS |
| `dt006_step4_adapter_test` | capture SAVE-only·JSON 왕복 타입 보존·invalid 보존·not-ready 안전 | ALL PASS |
| `dt006_step5_integration_test` | boot→new→capture→restore→churn→DialogueManager 주입 end-to-end | ALL PASS |
| DT-005 `dt005_step1~6` | schema/store/snapshot/batch/provider/통합 회귀 | ALL PASS |
| DialogueTool `dt004_step1~4`(+integration) | 기존 런타임/에디터/validation/통합 회귀 | ALL PASS |

- Godot 4.6.3(mono) headless editor load 성공. 종료 시 leak/resource 경고는 양성(import pass에도 재현),
  음성 경로 `ERROR:`/`WARNING:`는 의도된 `push_error`/`push_warning`.

## 리뷰 이력 (수정 완료된 지적)

- **Step 0(설계)**: (P1) coordinator Store 획득/autoload 순서/주입 미정 → ADR-007 D2a로 고정.
  (P1) 실패 restore의 상태 보존 정책 부재 → D4a transactional(pre-validation) 도입. (P2) bootstrap
  schema 표 미확정 → 6 key 표 확정. (P3/P2) 문서 문구 정리(Open Decisions/Runtime Contract/restore 순서).
- **Step 2**: (설계 수정) autoload 이름 `WorldStateStore`가 class_name과 충돌("hides an autoload
  singleton") → autoload `WorldState`로 변경, ADR-007 D2 갱신. (P2) 현재 사실 문서·Step 3 지침의
  잔여 이름 정리.
- **Step 3**: (P1) `set_store()`가 lifecycle transaction 우회(callback 교체/null → 불일치·런타임
  오류·stale session) → `_busy` 중 거부 + 실제 교체 시 session 해제 + 트랜잭션 동안 Store 참조 고정.
  회귀 테스트 G/H 추가.

## 발견 사항 (현재)

- P0/P1: 없음.
- P2/P3: 없음(아래 accepted debt는 의도된 범위 밖 항목).

## Accepted Debt / 의도된 한계

- **실제 file/slot 저장 없음**: coordinator는 `capture_world_state`/`restore_world_state` 메모리
  계약까지만 제공한다. 실제 `FileAccess` 직렬화, slot 목록, autosave, backup, 암호화는 후속
  **SaveGame file/slot Task**가 이 adapter를 소비해 구현한다.
- **bootstrap key는 placeholder**: 6 key는 통합 증명용이며 제품 quest/actor 의미는 미확정. 소유자
  확인 후 확장(ADR-007 D6).
- **scene change_scene 회귀는 proxy 검증**: headless 단일 scene에서 실제 `change_scene` 후속 검증은
  어려워 transient scene churn + autoload root-parenting으로 보존을 확인했다(Step 2/3/5). autoload
  semantics상 main scene 교체에도 동일하게 보존된다.
- **SESSION은 새 게임/SAVE load에서만 default**: scene 교체·대화 종료 자동 reset 없음(ADR-007 D4).
- snapshot/runtime INT는 JSON-safe `±(2^53-1)`, FLOAT finite(DT-005 accepted debt 연계).
- State Read/Set Dialogue 노드와 ConditionEvaluator는 본 Task 범위 밖(후속 Task).

## 판정

**완료** — Step 0~5 완료 조건 충족, P0/P1 없음, headless editor load 성공, DT-005/DialogueTool 회귀
유지. 후속 SaveGame file/slot Task와 ConditionSet/Effect 노드는 accepted debt/follow-up으로 명시했다.

## Related

- [[DT-006-WorldState-Runtime-Integration]]
- [[ADR-007-WorldState-Runtime-Lifecycle]]
- [[DT-005-WorldState-Review]]
- [[World-State-System]]
