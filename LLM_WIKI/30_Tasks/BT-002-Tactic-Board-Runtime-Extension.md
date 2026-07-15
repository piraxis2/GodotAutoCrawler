---
id: BT-002
type: task
status: complete
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

## Closed Decisions (Step 0)

Step 0 설계 리뷰([[BT-002-Tactic-Board-Runtime-Extension-Review]], 판정 `Approved after design fixes`)가 OD1~8을 아래로 확정했다. 계약 상세는 [[ADR-023-Tactic-Board-Runtime-Execution-Contract]](`accepted`)가 소유한다.

1. **상태 표현** — 확정: `BtStatus` 3값 유지 + 택틱 내부 결과 enum, selector 경계에서 변환. 전역 확장 안 함.
2. **행 latch 소유자** — 확정: PrioritySelector가 Running 행 latch(**action+approach 두 단계 모두**), Row가 내부 단계 소유. `CurrentTurnAction`은 멀티턴 action 재개 전용, 새 턴은 `ResetForNewTurn()` 명시 호출.
3. **Commit 관찰점** — 확정: adapter가 commit 명시 보고(action=`CurrentTurnAction` 세팅, approach=첫 이동 시작). 성립 검사는 commit 이전 read-only 경로로 분리.
4. **조건 후보 identity** — 확정: v0는 live Node instance identity 교집합, freed는 `IsInstanceValid` 제외. stable `unit_id`는 후속.
5. **접근 가능성 검사** — 확정: 실제 이동과 같은 path/range를 mutation 없이 공유하는 read-only helper. 이동량 단일 소스 `Mobility+1`.
6. **Commit 후 대상 무효** — 확정: `CancelledAfterCommit`으로 턴 종료, fallback 금지.
7. **최소 실증 어휘** — 확정: Step 1~2는 test condition/action double. Step 3에서 Always/EnemyCasting/ContextTarget/NearestEnemy + 근접 공격으로 실증.
8. **ADR 상태** — 확정: [[ADR-023-Tactic-Board-Runtime-Execution-Contract]] `accepted`. 추가로 fail-closed 구조 계약(비택틱 자식/행동 미실행, 조건 Success-only)을 ADR §9로 고정.

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
- **action-only 상태 기계**: 조건 게이트 → 행동 → commit/latch/fallback. 행동은 test double이 내부 결과를 직접 보고.
- 잘못된 구조 fail-closed(비택틱 자식/행동 미실행, Running 조건 차단) — ADR-023 §9.
- test condition/action 기반 headless fixture.

Out of scope:

- 실제 대상 검색, 이동, SkillSystem 연동.
- **approach 단계 latch/preempt·이동 tween 누수** — approach가 없으므로 Step 3에서 실증한다.
- **mutation-free feasibility와 `Mobility+1` 단일 이동량** — 실제 이동 경로가 붙는 Step 3 범위.
- `ResetForNewTurn()`의 실제 턴 시작 배선(CharacterArticle.TurnPlay 연결)은 Step 3. Step 1은 seam과 명시 호출 테스트만.

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

## Step 3: Approach, Action, and Wait — 택틱 런타임 상태 기계

Goal:

- 두 접근 정책·commit 경계·최종 하한의 택틱 런타임 상태 기계를 승인된 test 어댑터 대역으로 구현·검증한다.

Scope:

- 승인된 ExecuteAction, Approach, Wait, ActionSequence, 접근 정책(Immediate/ApproachAllowed) feasibility 필터.
- **selector Running latch를 approach 단계까지 확장**: 이동이 `CurrentTurnAction`을 거치지 않아도 latch가 preempt를 막는다(리뷰 P2-1, OD2).
- 행동 어댑터 seam(mutation-free feasibility + commit + 결과 보존 `Ineligible/Running/Completed/CancelledAfterCommit`).
- `ResetForNewTurn()`을 `CharacterArticle.ApplyTurnStartEffects` 턴 시작 경로에 배선(수제 BT no-op).
- fail-closed 구조(비택틱 selector/시퀀스 자식·행동 미실행, Running 조건 차단) — ADR-023 §9.

Out of scope (→ Step 3.5):

- 실제 `CharacterArticle` 어댑터, 실제 AStar/AttackRange 공유 feasibility, `Mobility+1` 실측, 제품 어휘, 전장 fixture.
- compiler/UI와 전체 스킬 어휘.

Done condition:

- `Immediate`는 이번 턴 행동 가능한 후보만 성립하고 먼 대상에게 접근하지 않는다(대역).
- 이동 후 사거리 내면 같은 대상에게 같은 턴 행동한다(대역).
- `ApproachAllowed`는 미도달 시 접근으로 턴을 끝내고 다음 행을 막는다(대역).
- 이동 Running 중 상위 행이 성립해도 preempt하지 않고, reset이 진행 중 접근을 취소한다.
- 성립 검사에서 접근/지불이 일어나지 않고, 실제 실행에서 한 번만 지불한다(대역 호출 수).
- 행동의 commit 전 실패는 다음 행 fallback, Running 행동은 턴을 유지, commit 후 실패는 CancelledAfterCommit.

Verification:

- `bt002_step3_approach_action_test`(승인된 어댑터 대역 통합) 43 단언. commit 후 대상 무효 취소는 Minimum Verification Matrix #9를 충족한다.
- Step 1~2 회귀, BT-001, CB-001 Step1(ApplyTurnStartEffects)·BS-001 실제 전투 턴 회귀.

## Step 3.5: Real Combat Adapter and Product Vocabulary

Goal:

- 택틱 런타임을 실제 `CharacterArticle` 전투에 배선하고 최소 제품 어휘로 실전장에서 실증한다.

Scope:

- `CharacterArticle`이 `ITacticTarget`·`ITacticActionAdapterProvider`를 구현하고 실제 전투 어댑터를 제공.
- 실제 AStar/`AttackRangePositions`를 mutation 없이 공유하는 read-only feasibility, 이동량 단일 소스 `Mobility+1`.
- **per-action feasibility 바인딩**(ADR-023 §10): 행의 구체 action이 range/cost를 feasibility에 공급.
- 최소 제품 어휘: `Always`, `EnemyCasting`(→`IsCasting`), `NearestEnemy`/`ContextTarget`, 근접 `TacticExecuteAction`.
- 실제 멀티턴 스킬 `CurrentTurnAction` 재개와 택틱 latch/turn reset 연동.

Done condition:

- 실제 `CharacterArticle`로 구성한 작은 전장에서 택틱 보드가 성립·접근·행동·대기로 턴을 진행한다.
- `Immediate` 대상이 `Mobility+1` 기준 정확히 도달/1칸 부족 경계에서 성립/불성립이 갈린다.
- 성립 검사 전후 grid solid·마나·탄약·위치가 불변이다.
- 실제 멀티턴 스킬이 `CurrentTurnAction`으로 재개되고 다음 턴에 latch/문맥이 초기화된다.

Verification:

- 작은 전장 fixture 통합 테스트(경계값·무변형·재개), CB-001/SK deterministic combat 회귀, build/import.

## Step 4: Lifecycle, Debug Metadata, and Completion

Goal:

- 반복 전투와 디버그 환경에서 안전한 BT 확장으로 닫는다.

Scope:

- RowId/debug payload, death/tree exit/session teardown reset.
- 다중 유닛·반복 전투 격리, 택틱 노드 구조 validation.
- **Step 3.5 이월**: 바인딩된 확정 대상의 death/free/teardown 고정 테스트(`BoundTarget`이 freed/사망 대상을 반환하지 않고 fallback도 하지 않음).
- **Step 3.5 이월**: headless 종료 시 잔여 Tween 누수의 소유자 추적 — 이동 tween과 전투 VFX tween의 수명 주기를 닫는다.
- System/Current-State/Open-Tasks와 완료 리뷰.

Done condition:

- RowId가 debug tick에서 식별되며 debug-off 비용 계약을 깨지 않는다.
- death/free/teardown 후 latch/context/target이 남지 않는다.
- 두 유닛·두 전투에서 상태가 섞이지 않는다.
- 잘못된 택틱 child 구조가 실행 전 validation으로 드러난다.
- 기존 일반 BT editor/debugger와 수제 전투 BT가 회귀하지 않는다.
- 문서와 코드가 일치하고 완료 리뷰에 Step 0~3, Step 3.5, Step 4 증거가 모두 있다.

Verification:

- lifecycle/debug/validation headless test.
- BT-001, CB/SK/BS 영향권 회귀, build/import.
- [[STEP_REVIEW_WORKFLOW]]에 따른 Step별 리뷰와 재검증.

## 진행 기록

### Step 0 — Design Review (완료)

- OD1~8을 닫고 [[ADR-023-Tactic-Board-Runtime-Execution-Contract]]를 `accepted`로 확정. 리뷰: [[BT-002-Tactic-Board-Runtime-Extension-Review]], 판정 `Approved after design fixes`.

### Step 1 — Priority and Row Lifecycle (완료)

- 구현: `Assets/Script/AutoCrawlerBehaviorTree/Tactic/`의 `TacticResult`(내부 결과 enum + `BtStatus` 매핑), `ITacticNode`(LastResult/ResetForNewTurn), `TacticPrioritySelector`(Running 행 latch + 위→아래 최초 성립), `TacticRow`(pre-action 게이트 + 행동, `_executing` 단계 latch).
- fail-closed(리뷰 P2): selector는 `ITacticNode` 자식만 실행, row 행동은 `ITacticNode`만 허용, 조건은 동기 gate라 `Success`만 통과(`Running`/비택틱 차단). turn reset은 `ResetForNewTurn()` 명시 seam(실제 배선 Step 3), `OnInit`은 terminal 경로 보조.
- 검증: `bt002_step1_priority_lifecycle_test` 54/54 PASS(A~K), `dotnet build` 0/0, `BehaviorTreeValidationTest`·CB-001 Step1~3·SK-004 Step1 회귀 PASS. CB-001 Step4는 알려진 로스터 선행 실패([[battlefield-skill-test-gotchas]]).
- 리뷰 재판정: 미완료 → (문서 게이트 close + P2 fail-closed 보강) → 완료.

### Step 2 — Condition Candidates and Target Context (완료)

- 구현: `TacticRowContext`(후보 identity 교집합 + 확정 대상 + `IsInstanceValid` freed 필터, 유닛별 소유), `ITacticTarget`(위치 seam), `TacticCondition`(gate/target-bearing base), `TacticTargetSelector`(호환 필터 seam → ADR-017 tie-break, `SkillUtil.OrderByCanonical` 재사용), `TacticRow` 컨텍스트 바인딩 확장.
- fail-closed(재리뷰 P1/P2): 타깃 셀렉터는 마지막 pre-action 자식(행동 바로 앞)·최대 하나로 강제(교집합 우회 차단), owner가 `ITacticTarget`이 아니면 `Failure`(월드 원점 오선택 방지).
- 검증: `bt002_step2_condition_target_test` 33/33 PASS(L~R + Q 보관 후 free 무효화), Step 1·BT-001 회귀 PASS.
- 리뷰 재판정: 수정 필요(P1×1, P2×2) → (fail-closed + 회귀 테스트 보강) → P3 문서만 잔여.

### Step 3 — Approach, Action, and Wait: 택틱 런타임 (완료)

- 구현: `TacticApproach`/`TacticExecuteAction`/`TacticWait`/`TacticActionSequence`, 접근 정책 feasibility 필터(`TacticTargetSelector`), 어댑터 seam(`ITacticActionAdapter`, feasibility+commit+결과 보존), commit 보고(`ITacticCommitting`).
- 배선: `CharacterArticle.ApplyTurnStartEffects`에서 택틱 루트 `ResetForNewTurn()`(OD2/P2-1 stale-latch 수정, 수제 BT no-op).
- fail-closed(재리뷰 P2): selector/시퀀스 비택틱 자식·행동 미실행. 행동 결과 계약(재리뷰 P1): `ExecuteAttack`이 `Ineligible/Running/Completed/CancelledAfterCommit` 반환.
- 검증: `bt002_step3_approach_action_test` 43/43 PASS(S~Z4 — Immediate/ApproachAllowed, preempt 차단, reset 취소, commit 전 실패 fallback, Running 턴 유지, commit 후 무효 취소(Matrix #9), 구조 fail-closed), Step 1·2·BT-001·CB-001 Step1/3·BS-001 Step1/3 회귀 PASS.
- 실물 통합(실제 어댑터·제품 어휘·전장 fixture)은 설계 리뷰 결정으로 **Step 3.5로 분리**(ADR-023 §10 per-action feasibility 바인딩 확정).

### Step 3.5 — Real Combat Adapter and Product Vocabulary (완료)

- seam 개정(ADR-023 §10): `ITacticActionSpec`(행의 행동이 사거리·`CanStart`를 컨텍스트에 공급) + 어댑터 feasibility가 range 오프셋을 인자로 받음 → 근접/스킬마다 올바른 사거리가 적용된다. `TacticExecuteAction`은 spec을 공급하는 abstract base가 됐다.
- 실제 어댑터 `CharacterTacticAdapter`: 상대 열거, 사거리, `Mobility+1` 단일 소스(경로 노드 예산 = Mobility+1 → 최대 Mobility칸), AStar 사본 기반 무변형 feasibility, 정규 경로(ADR-017) 이동/부분 접근, tween 취소.
- 실제 배선: `ArticleBase : ITacticTarget`, `CharacterArticle : ITacticActionAdapterProvider`(유닛별 어댑터), `ApplyTurnStartEffects`에서 어댑터 턴 reset.
- 확정 대상 공유(R4): `TurnActionBase`의 **명시적 대상 바인딩 계약** — `IsExplicitTargetBound`/`BindExplicitTarget`/`ClearExplicitTarget`/`BoundTarget`. 바인딩이 활성이면 근접의 `GetTarget`도 스킬의 `SelectTarget`도 상대를 재검색하지 않고 그 대상만 쓰며, 대상이 무효(free 또는 사망)면 **다른 적으로 fallback하지 않고** 무소모 실패한다. 유효성은 `IsInstanceValid` → `IsAlive` 순으로 본다. 수제 BT는 바인딩하지 않아 무회귀.
- 행동 수집 일원화: `ITurnActionProvider`(BehaviorTree_TurnAction·TacticTurnAction 구현) — `ChargeBattleAmmo`/`UsableTurnActions`/사거리 계산이 wrapper 타입이 아니라 이 계약으로 순회한다. 택틱 보드의 제한 스킬이 ammo 충전에서 누락되던 결함을 닫는다.
- 실제 행동 `TacticTurnAction`: `TurnActionBase`를 감싸 Failure/Running/Executed/End를 택틱 결과로 보존하고 멀티턴은 `CurrentTurnAction`으로 재개시킨다. 새 턴에 바인딩을 해제한다.
- 제품 어휘: `TacticConditionAlways`, `TacticConditionAnyEnemy`(최근접 적 행의 후보 소스), `TacticConditionEnemyCasting`(영창 방해 행). "가장 가까운 적"은 AnyEnemy + 셀렉터 정규 순서로 표현된다.
- 검증: `bt002_step35_battle_fixture_test` **40 단언 PASS** — 실제 전장에서
  - `Mobility+1` **정확히 도달/1칸 부족 경계**를 어댑터와 **실제 row/selector 경유** 양쪽으로 확인(1칸 부족 시 접근하지 않고 위치 불변 → wait 행이 턴 소비).
  - 성립 검사 **무변형**: 유닛 위치·타일 점유 스냅샷·마나·**탄약** 불변.
  - 택틱 보드가 **접근 → 같은 대상 근접 공격**으로 턴 진행, 다음 턴 latch/문맥 reset 후 재평가.
  - `EnemyCasting → ContextTarget`: 영창 중이 아니면 불성립(대기), 영창 시 그 적을 확정 — `SkillState.ConfirmedTargets[0]`가 택틱 확정 대상과 **동일 인스턴스**.
  - 실제 멀티턴 스킬(`WindupCost=1`): **마나·탄약 1회만 소비**, 다음 턴 **보드 재평가 없이 `CurrentTurnAction` 재개**(root `LastResult`가 reset 후 `Ineligible` 유지), 재개 시 재지불 없음, 완료 후 action/state 정리, 그다음 턴 첫 행부터 재평가.
- 회귀: BT-001, BT-002 Step 1~3, CB-001 Step1~3, SK-002 Step1/3, SK-004 Step1, ST-001 Step1, BS-001 Step1/3 ALL PASS.
- 잔여였던 Tween 누수(당시 비결정적 1~5개)는 **Step 4에서 종결**됐다. 소유자는 이동 tween(어댑터)과 전투 VFX tween(`damage_floater.gd`) 두 곳이었다 — 아래 Step 4 항목 참조.

### Step 4 — Lifecycle, Debug Metadata, and Completion (완료)

- **RowId debug payload**: `BehaviorTree.ReportTacticRow`가 tick payload에 `tactic_row_id`/`tactic_row_result`를 싣고, `TacticPrioritySelector`가 **선택/재개한 행**만 보고한다. `DebugEnabled`가 꺼져 있으면 호출조차 하지 않아 debug-off 비용 계약을 깨지 않는다. 수제 BT payload에는 이 키가 없어 기존 계약 유지.
- **수명 주기**: `CharacterArticle.ResetTacticRuntime()` — 새 자기 턴·**사망(OnDead)**·**tree exit**에서 latch/문맥/확정 대상/진행 중 이동 연출을 모두 폐기한다. 수제 BT는 no-op.
- **구조 validation**: `BehaviorTreeValidation`에 택틱 규칙 추가(비택틱 selector 자식, 행동 없음, 비택틱 행동, 셀렉터 위치 오류, 시퀀스 비택틱 자식) — 런타임 fail-closed와 **같은 규칙**을 편집 시점에 오류로 표면화한다.
- **이월 처리**: (1) 바인딩 대상 free 시 `BoundTarget`이 null을 반환하고 fallback하지 않음을 실제 전장에서 고정. (2) **Tween 누수 종결** — 소유자는 두 곳이었다: 어댑터가 경로 전체를 미리 tween으로 만들어 두던 것(→ **한 번에 하나만** 소유하고 Kill+Dispose로 즉시 반납)과, 기존 전투 VFX `damage_floater.gd`의 `get_tree().create_tween()`(~0.5초)이 종료 전에 끝나지 않던 것(→ fixture가 FxPlayer를 flush하고 실제 시간을 준 뒤 teardown). **`bt002_step4_lifecycle_test`는 반복 실행에서 leak 0**이다. `bt002_step35_battle_fixture_test`에 드물게 남는 4건은 전부 `AudioStream*`(공격 SFX가 재생 중인 상태로 종료)로, 기존 전투 오디오 수명 문제이며 택틱 런타임 밖이다.
- 검증: `bt002_step4_lifecycle_test` **61 단언 PASS**
  - **debug payload**: `EndDebugTick`이 실제로 만드는 dictionary(`BuildTickPayload`)를 검증 — 선택된 행의 `tactic_row_id`/`tactic_row_result`가 실리고, `Running` 행 재개 tick도 같은 RowId다. debug-off면 payload 자체가 null이고, **수제 BT payload에는 택틱 키가 없다**(기존 계약 보존).
  - **구조 validation 5종**: 비택틱 selector 자식, 행동 없음, 비택틱 행동, 셀렉터 위치 오류, 시퀀스 비택틱 자식. 정상 보드는 오류 0.
  - **실제 사망**: 접근 중(latch·문맥 대상·이동 tween 보유) 상태를 만든 뒤 HP 0 → `Dead()` → `OnDead` → latch·문맥·행동 바인딩·`CurrentTurnAction`/State·이동 tween이 **모두 정리**됨을 확인. 바인딩 대상 free 시 fallback 없이 null.
  - **두 유닛·두 전투 격리**: 두 유닛 latch/문맥 독립, 반복 턴 잔여 상태 없음. **전투 1**에서 상태를 만들고 사망·teardown한 뒤 **전투 2**를 새로 생성해 초기 상태와 독립 실행을 확인(두 보드가 **같은 `TurnAction` 리소스 공유** — 바인딩이 전투 간에 새지 않음).
- 회귀: BT-001, BT-002 Step 1~3·3.5, CB-001 Step1~3, SK-002 Step1/3, SK-004, ST-001 Step1, BS-001 Step1/3, BS-002 Step1, BS-003 Step1 **ALL PASS**(17종).

### 완료

- Step 0~4 전부 완료. 완료 리뷰: [[BT-002-Tactic-Board-Runtime-Completion-Review]].
- 후속(별도 Task): 택틱보드 Resource/validator/compiler, 편집 UI, H-4 전체 어휘, DungeonRun 연결.

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
- [[ADR-023-Tactic-Board-Runtime-Execution-Contract]]
- [[STEP_REVIEW_WORKFLOW]]