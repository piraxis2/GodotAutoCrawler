---
id: BT-002
type: task
status: proposed
system: BehaviorTree
created: 2026-07-14
updated: 2026-07-14
tags: [task, behavior-tree, tactic-board, combat-runtime]
---

# Tactic Board Runtime Extension

## Goal

플레이어가 작성한 택틱보드를 기존 BehaviorTree에서 예측 가능하고 결정론적으로 실행할 수 있도록 최소 runtime 기반을 추가한다.

이번 Task는 택틱보드 Resource나 편집 UI가 아니다. 엄격한 행 우선순위, 실행 중 행 고정, 문맥 대상, commit 이후 fallback 금지, `Immediate`/`ApproachAllowed` 접근 정책과 최종 대기를 기존 BT 위에서 표현·검증하는 것이 목표다.

## Player Contract Source

- 기획 기준: `GameDesign/기획서/09_택틱보드.md` H-3, H-7, H-8.
- 택틱보드는 유닛별 우선순위 행 리스트이며 위에서 아래로 최초 성립 행 하나를 실행한다.
- 조건·대상·행동은 자연어 문장 하나로 읽혀야 하며 내부 BT가 그 의미를 바꾸면 안 된다.
- 전투 중에는 성공적으로 컴파일된 독립 스냅샷을 사용한다. 데이터와 compiler는 후속 Task다.

## Current Code Evidence

2026-07-14 기획 확장 시 실제 코드를 대조했다.

- `BehaviorTree_Selector`와 `BehaviorTree_Sequence`는 매 tick 첫 자식부터 다시 평가하며 `Running` child index를 기억하지 않는다.
- `BehaviorTree_Node`는 `Running` 동안 자기 상태를 유지하지만 부모 composite의 선택 행은 고정하지 않는다.
- `CharacterArticle.TurnPlay()`는 `CurrentTurnAction == null`일 때 BT를 평가하고 진행 action은 직접 재개한다. 택틱 행 latch와 action의 책임 경계를 정해야 한다.
- `BehaviorTree_Move`는 행의 확정 대상이 아니라 상대 전체를 다시 검색한다. 이동 후 action이 실패하면 기존 Selector가 다음 분기를 실행할 수 있다.
- `BehaviorTree_TurnAction`은 지불/검증 실패를 `Failure`로 반환하지만 commit 전후 실패를 구분하지 않는다.
- `CharacterArticle.IsCasting`, `TurnActionBase.CanStart()`, ADR-017 정규 순서와 전투 RNG는 재사용 가능한 seam이다.
- `BehaviorTreeValidation`은 현재 구조적 child 제약만 검사하며 택틱 행 의미는 모른다.

## Scope

- 택틱보드 전용 runtime node와 유닛별 실행 문맥.
- 행 성립 검사와 실제 상태 변경 사이의 commit 경계.
- 조건 후보 교집합, 행동 호환성, 접근 정책, selector의 고정된 평가 순서.
- 실행 중 행/대상 유지와 다음 턴·사망·teardown reset.
- 최종 대기와 RowId/debug metadata 최소 seam.
- 기존 수제 BT 및 editor/debugger 무회귀.

## Out of Scope

- 보드 Resource/저장 버전, validator/compiler와 compiled snapshot 관리.
- 택틱보드 UI, 프리셋, 경고 표시, BattleSession 적용.
- H-4 전체 어휘와 행별 전투 통계/리포트.
- DungeonRun, 층 진행, 전투 전 준비 화면.
- 기존 일반 Selector/Sequence/Move의 전면 교체.

## Minimum Runtime Contract

### R1. 평가와 reset

- 행동 가능한 자기 턴이고 `CurrentTurnAction`이 없을 때 새 행을 고른다.
- 새 턴에는 이전 latch, 후보, 대상, 접근 단계를 제거한다.
- 멀티턴 action은 기존 action 경로로 재개하며 보드를 다시 평가하지 않는다.
- 유닛 사망, tree exit, session teardown에서 문맥과 Node 참조를 제거한다.

### R2. 엄격한 우선순위

- 활성 행을 위에서 아래로 평가하고 최초 성립 행 하나만 고른다.
- commit 전 `Ineligible` 행만 다음 행으로 fallback한다.
- 가중치나 Node/Dictionary 수집 순서를 선택 기준으로 사용하지 않는다.

### R3. 순수한 성립 검사

행 성립은 `모든 조건 참 AND 행동 시작 가능 AND 유효 대상 존재 AND 접근 정책 충족`이다.

- 검사 중 자원 지불, 이동, animation, 상태 효과를 발생시키지 않는다.
- target-bearing 조건 두 개는 같은 대상이 모두 만족해야 한다(후보 교집합).
- targetless 조건은 행 gate이며 후보를 만들지 않는다.
- 양립 불가능한 후보 진영·타입은 이후 validator가 잡을 수 있는 구조 오류로 보존한다.

### R4. 대상 확정 순서

`조건 후보 → 행동 호환 후보 → 접근 정책 가능 후보 → selector → ADR-017 tie-break → 대상 하나` 순서다.

- 조건·접근·행동은 같은 확정 대상을 공유한다.
- `그 대상`은 조건 후보 집합에서 고른다.
- 여러 후보는 거리→Y→X로 결정하며 수집 순서에 의존하지 않는다.

### R5. 접근 정책

- `Immediate`: 현재 또는 이번 턴 이동 후 행동까지 가능한 후보만 성립한다. 불가능하면 접근하지 않고 다음 행으로 간다.
- `ApproachAllowed`: 대상과 경로가 있으면 성립한다. 이번 턴 행동할 수 없으면 접근으로 턴을 소비하고 다음 행을 금지한다.
- `[적이 영창 중] → [소닉 블로] → [그 적]`은 `Immediate` 기준 사례다. 먼 영창 적만 있으면 불성립이며, 여러 영창 적 중 이번 턴 방해 가능한 후보에서 선택한다.

### R6. Commit과 fallback

첫 이동 시작, 비용 지불, action/animation/영창 시작, 실제 효과 적용 중 하나가 발생하면 committed다.

- commit 전 실패만 다음 행을 허용한다.
- commit 후 대상 무효·취소·실패는 턴을 종료하고 다음 행을 금지한다.
- 한 턴에 상태를 변경하는 행은 최대 하나다.

### R7. Running 행 고정

- 선택 행이 `Running`이면 다음 tick에도 같은 행과 대상을 재개한다.
- 실행 중 조건이 달라져도 상위 행으로 preempt하지 않는다.
- 다음 자기 턴에는 latch를 해제하고 첫 행부터 재평가한다.

### R8. 최종 하한

기본 의미는 `공격 가능→공격 / 접근 가능→접근 / 모두 불가→대기`다.

- wait는 정상적인 턴 소비다.
- 살아 있는 상대가 있지만 행동할 수 없는 상태가 무한 `Failure`가 되지 않는다.
- 진영 전멸과 전투 종료는 계속 `BattleSession` 책임이다.

### R9. 결정론과 격리

- 같은 택틱 구조·전투 상태·seed는 같은 행·대상·행동을 선택한다.
- 같은 구조를 쓰는 두 유닛도 latch와 context를 공유하지 않는다.
- freed 대상은 다음 평가 후보가 아니며 stale Node 참조를 남기지 않는다.

## Proposed Runtime Components

이름과 API는 Step 0에서 확정한다.

- `TacticPrioritySelector`: 행 우선순위와 `Running` child latch.
- `TacticRow`: 조건→후보→대상→접근/행동의 행 수명주기와 RowId.
- `TacticRowContext`: 접근 정책, 후보, 확정 대상, commit/실행 단계.
- `TacticCondition`, `TacticTargetSelector`, `TacticExecuteAction`.
- `TacticApproach`, `TacticWait`.
- 내부 결과 후보: `Ineligible`, `Running`, `ActionCompleted`, `ApproachConsumed`, `CancelledAfterCommit`.

권장안은 전역 `BtStatus`를 늘리지 않고 내부 결과를 경계에서 변환하는 것이다.

```text
Ineligible           → BtStatus.Failure
Running              → BtStatus.Running
ActionCompleted      → BtStatus.Success
ApproachConsumed     → BtStatus.Success
CancelledAfterCommit → BtStatus.Success
```

## Open Decisions for Step 0

1. **상태 표현** — 권장: `BtStatus` 유지 + 택틱 내부 결과. 전역 확장은 기존 composite/editor/debugger 파급이 크다.
2. **행 latch 소유자** — 권장: PrioritySelector가 child, Row가 내부 단계 소유. `CurrentTurnAction`과 reset 책임을 대조한다.
3. **Commit 관찰점** — 권장: 접근/action adapter가 commit을 명시 보고한다. 기존 action 비용·animation 순서에서 가능한지 확인한다.
4. **조건 후보 identity** — 권장: stable identity 교집합. v0 Node identity와 향후 stable `unit_id` seam을 결정한다.
5. **접근 가능성 검사** — 권장: 실제 이동과 같은 path/range 계산을 mutation 없이 공유한다.
6. **Commit 후 대상 무효** — 권장: `CancelledAfterCommit`으로 턴 종료, fallback 금지.
7. **최소 실증 어휘** — 권장: Always, EnemyCasting, ContextTarget, NearestEnemy, test action/wait. 제품 어휘 포함 범위를 결정한다.
8. **ADR 필요성** — 권장: status/commit/context 수명은 장기 결정이므로 ADR-023 작성 여부를 Step 0에서 확정한다.

## Step 0: Design Review

Goal:

- 현재 BT/TurnAction/TurnHelper 경로로 최소 계약을 구현할 수 있는지 확인하고 Open Decisions 1~8을 닫는다.

Scope:

- `BehaviorTree_Node`, composites/actions/decorators, `CharacterArticle.TurnPlay`, TurnAction, Move/pathfinding, Blackboard, debugger/validation 실측.
- 상태 변환, turn reset, commit seam, context identity/lifetime, feasibility API와 fixture 전략 확정.

Out of scope:

- 제품 코드, `.tscn`, `.tres` 수정.
- compiler/UI와 전체 어휘 상세 설계.

Done condition:

- [[Design-Review-Prompt]] 판정이 `Approved` 또는 `Approved after design fixes`다.
- P0/P1 없음, OD1~8과 Step 1~4 경계가 확정된다.
- API, reset/teardown, 오류 정책과 테스트 대역이 구현 가능한 수준이다.
- ADR이 필요하면 Step 1 전에 accepted 상태로 준비한다.

Verification:

- Task/기획/실제 코드 정적 대조.
- 리뷰 문서 `LLM_WIKI/50_Reviews/BT-002-Tactic-Board-Runtime-Extension-Review.md` 작성.

## Step 1: Priority and Row Lifecycle

Goal:

- 불성립 fallback, `Running` 행 고정, commit 후 턴 종료의 최소 상태 기계를 구현한다.

Scope:

- 승인된 PrioritySelector, Row, 내부 결과/commit 표현, turn reset과 인스턴스 격리.
- test condition/action 기반 headless fixture.

Out of scope:

- 실제 대상 검색, 이동, SkillSystem 연동.

Done condition:

- 첫 행 성립/불성립과 두 번째 행 fallback이 순서대로 동작한다.
- `Running` 중 상위 조건이 바뀌어도 선택 행이 유지된다.
- commit 후 실패는 다음 행을 실행하지 않는다.
- 다음 턴에는 첫 행부터 재평가하고 두 tree instance 상태가 격리된다.

Verification:

- 신규 Step 1 headless test, `BehaviorTreeValidationTest` 및 BT/CB 영향권 회귀.
- `dotnet build`, Godot `--import`.

## Step 2: Condition Candidates and Target Context

Goal:

- 조건 후보 교집합과 확정 대상 공유를 구현한다.

Scope:

- 승인된 RowContext, Condition, TargetSelector seam.
- targetless gate/target-bearing 조건 조합, compatibility filter, ADR-017 tie-break.
- OD7에서 승인한 최소 어휘 또는 테스트 대역.

Out of scope:

- 실제 이동·비용 지불과 H-4 전체 어휘.

Done condition:

- 두 대상 조건은 같은 유닛이 모두 만족할 때만 성립한다.
- gate 조건은 후보를 바꾸지 않는다.
- 여러 후보는 거리→Y→X로 같은 대상을 선택한다.
- 조건·selector·action seam이 같은 identity를 보고 freed 후보를 남기지 않는다.

Verification:

- 교집합/빈 후보/다중 후보/tie-break/invalid instance headless test와 Step 1 회귀.

## Step 3: Approach, Action, and Wait

Goal:

- 실제 행동 경계에서 두 접근 정책과 최종 하한을 검증한다.

Scope:

- 승인된 ExecuteAction, Approach, Wait.
- mutation 없는 feasibility와 실제 path/range 규칙 공유.
- commit 보고, 영창 방해 `Immediate`, 일반 `ApproachAllowed` 사례.

Out of scope:

- compiler/UI와 전체 스킬 어휘.

Done condition:

- `Immediate`는 이번 턴 행동 가능한 후보만 성립하고 먼 영창 적에게 접근하지 않는다.
- 이동 후 사거리 내면 같은 대상에게 같은 턴 행동한다.
- `ApproachAllowed`는 미도달 시 접근으로 턴을 끝내고 다음 행을 막는다.
- 모든 행동/접근 불가 시 wait가 턴을 소비한다.
- 사전 검사에서 비용을 내지 않고 실제 실행에서 한 번만 지불한다.

Verification:

- 작은 전장 fixture 또는 승인된 path/action test double 통합 테스트.
- Step 1~2, SkillSystem, deterministic combat 영향권 회귀.

## Step 4: Lifecycle, Debug Metadata, and Completion

Goal:

- 반복 전투와 디버그 환경에서 안전한 BT 확장으로 닫는다.

Scope:

- RowId/debug payload, death/tree exit/session teardown reset.
- 다중 유닛·반복 전투 격리, 택틱 노드 구조 validation.
- System/Current-State/Open-Tasks와 완료 리뷰.

Done condition:

- RowId가 debug tick에서 식별되며 debug-off 비용 계약을 깨지 않는다.
- death/free/teardown 후 latch/context/target이 남지 않는다.
- 두 유닛·두 전투에서 상태가 섞이지 않는다.
- 잘못된 택틱 child 구조가 실행 전 validation으로 드러난다.
- 기존 일반 BT editor/debugger와 수제 전투 BT가 회귀하지 않는다.
- 문서와 코드가 일치하고 완료 리뷰에 Step 0~4 증거가 있다.

Verification:

- lifecycle/debug/validation headless test.
- BT-001, CB/SK/BS 영향권 회귀, build/import.
- [[STEP_REVIEW_WORKFLOW]]에 따른 Step별 리뷰와 재검증.

## Minimum Verification Matrix

1. 첫 행 성립 → 첫 행 실행.
2. 첫 행 불성립 → 두 번째 행 실행.
3. 첫 행 `Running` 중 조건 변화 → 첫 행 유지.
4. `Immediate` 대상이 너무 멂 → 접근 없이 다음 행.
5. `Immediate` 대상이 이동 후 사거리 내 → 이동 후 같은 대상에게 행동.
6. `ApproachAllowed` 대상이 멂 → 접근으로 턴 종료, 다음 행 미실행.
7. 두 대상 조건 → 같은 유닛이 모두 만족할 때만 성립.
8. 여러 후보 → 거리→Y→X로 같은 대상.
9. commit 후 대상 사망/무효 → 다음 행 미실행.
10. 모든 행동 불가 → wait로 정상 종료.
11. 다음 턴 → 이전 latch/대상 제거 후 첫 행부터 평가.
12. 동일 구조의 두 유닛/두 전투 → context 격리.
13. 성립 검사 → 자원·위치·상태 mutation 없음.
14. teardown → stale Node/context 없음.

## Risks

- 전역 `BtStatus` 확장은 composite/editor/debugger의 모든 status 처리에 회귀를 만들 수 있다.
- 행 latch와 `CurrentTurnAction`의 소유가 겹치면 영구 `Running`, 중복 실행, reset 누락이 생길 수 있다.
- feasibility와 실제 이동이 다른 path/range 계산을 쓰면 성립 뒤 실행 실패가 반복된다.
- Node reference를 오래 보관하면 대상 free와 반복 session에서 stale 참조가 남는다.
- commit 경계가 action마다 다르면 이동·비용 지불 후 fallback으로 한 턴에 두 행동이 생긴다.

## Follow-ups

- 택틱보드 Resource schema, validator/compiler와 compiled snapshot.
- 전투 전 편집·검증·적용 UX와 BattleSession party snapshot 배선.
- H-4 어휘, 해금 모델, 행별 발동 통계와 전투 후 수정 제안.
- DungeonRun의 `Preparing → Tactic setup → Battle → Next floor` 연결.

## Related

- [[BehaviorTree-System]]
- [[BT-001-BehaviorTree-Graph-Editor-Debugger]]
- [[CB-001-Deterministic-Combat-Resolution]]
- [[SK-001-Data-Driven-Skill-System]]
- [[SK-004-Usable-Attack-Gate]]
- [[Battle-Session-System]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[STEP_REVIEW_WORKFLOW]]