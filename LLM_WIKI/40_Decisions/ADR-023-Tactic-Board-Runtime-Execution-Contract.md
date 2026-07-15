---
id: ADR-023
type: decision
status: accepted
system: BehaviorTree
created: 2026-07-14
updated: 2026-07-14
tags: [decision, behavior-tree, tactic-board, runtime, combat]
---

# Tactic Board Runtime Execution Contract

## Status

Accepted (2026-07-14). [[BT-002-Tactic-Board-Runtime-Extension]] Step 0 설계 리뷰에서 실제 BT/TurnAction/TurnHelper 실행 경로와 대조해 OD1~8을 닫고 승인했다(리뷰: [[BT-002-Tactic-Board-Runtime-Extension-Review]], 판정 `Approved after design fixes`). 이 계약은 택틱 runtime node 계층의 status 변환, commit 경계, context 수명, feasibility 공유를 장기 결정으로 고정한다. Step 1은 이 중 status 매핑·행 latch·commit 후 턴 종료·turn reset seam을 test double로 구현했다.

## Context

플레이어 택틱보드는 `조건 0~2개 → 행동 하나 → 대상 셀렉터`로 읽히는 엄격한 우선순위 행 목록이다. 내부 실행은 기존 BehaviorTree를 재사용하지만 현재 일반 Selector/Sequence는 매 tick 첫 자식부터 다시 평가하고, Move는 행의 확정 대상 대신 상대 전체를 다시 검색한다. 이동 후 action 실패도 일반 `Failure`가 되어 다음 분기를 실행할 수 있다.

따라서 기존 노드를 단순 조립하면 실행 중 행 preemption, 조건 대상과 행동 대상 불일치, 이동·비용 지불 후 다른 행 실행 같은 규칙 위반이 발생할 수 있다. 택틱보드 compiler와 UI를 만들기 전에 runtime 의미를 장기 계약으로 고정해야 한다.

## Decision

### 1. 기존 일반 BT와 택틱 runtime을 분리한다

기존 `BehaviorTree_Selector`, `BehaviorTree_Sequence`, `BehaviorTree_Move`의 의미를 전역 변경하지 않는다. 택틱보드에는 전용 priority selector, row, condition/target/action/approach/wait node와 row-scoped context를 추가한다.

기존 수제 BT와 editor/debugger는 현재 계약을 유지하고, 택틱보드 compiler만 전용 node를 생성한다.

### 2. 전역 BtStatus는 유지한다

`Failure/Running/Success`에 택틱 전용 값을 추가하지 않는다. 택틱 행 내부에서 더 정밀한 결과를 사용하고 BT 경계에서 변환한다.

```text
Ineligible           → Failure
Running              → Running
ActionCompleted      → Success
ApproachConsumed     → Success
CancelledAfterCommit → Success
```

`Failure`는 상태를 변경하지 않은 commit 전 불성립만 의미한다.

### 3. 선택 행과 행 내부 단계의 소유자를 나눈다

- 택틱 priority selector는 현재 선택된 `Running` child를 기억한다.
- 택틱 row는 후보, 확정 대상, 접근 정책, commit과 내부 실행 단계를 기억한다.
- `CurrentTurnAction`이 생긴 이후의 멀티턴 action 재개는 기존 `CharacterArticle` 경로를 유지한다.
- 새 자기 턴, 유닛 사망, tree exit와 battle teardown에서 택틱 문맥을 초기화한다.

### 4. Eligibility와 Commit을 분리한다

행 성립 검사는 순수해야 한다. 조건, 비용, 대상, path/range feasibility를 검사하는 동안 자원 지불, 이동, animation 또는 상태 효과를 발생시키지 않는다.

첫 이동, 비용 지불, action/animation/영창 시작 또는 실제 효과 적용 중 하나가 발생하면 committed다. commit 전 실패만 다음 행을 허용하고, commit 후 취소·대상 무효·실패는 해당 턴을 종료한다.

### 5. 대상은 row-scoped context로 고정한다

처리 순서는 `조건 후보 → 행동 호환 후보 → 접근 정책 가능 후보 → selector → ADR-017 tie-break → 대상 확정`이다.

- 두 target-bearing 조건은 후보 집합 교집합을 사용한다.
- targetless 조건은 행 gate다.
- 조건·접근·행동은 같은 확정 대상 identity를 공유한다.
- 임시 대상은 전역 Blackboard 문자열 key에 방치하지 않고 유닛별 row context 수명에 묶는다.

### 6. 접근 정책은 두 가지다

- `Immediate`: 현재 또는 이번 턴 이동 후 행동까지 실행할 수 있어야 성립한다. 불가능하면 접근하지 않고 다음 행으로 간다.
- `ApproachAllowed`: 유효 대상과 경로가 있으면 성립한다. 이번 턴 행동 사거리에 못 들어오면 접근으로 턴을 소비한다.

`[적이 영창 중] → [소닉 블로] → [그 적]`은 `Immediate` 기준 사례다. 여러 영창 적 중 이번 턴 실제로 방해 가능한 후보에서 결정론적으로 선택한다.

### 7. 최종 wait와 결정론을 보장한다

기본 행은 공격, 접근, wait 순으로 하한을 제공한다. 행동할 수 없는 살아 있는 유닛이 무한 `Failure`를 반복하지 않는다.

대상 tie-break는 ADR-017 거리→Y→X를 따르고, 수집 순서나 Dictionary/Node 순서에 의존하지 않는다. 향후 random selector는 전투 RNG만 사용한다.

### 8. Compiler와 UI는 이 계약의 소비자다

보드 Resource, validator/compiler, Draft/Applied 상태, BattleSession 적용과 편집 UI는 BT-002 범위 밖이다. 후속 compiler는 이 ADR의 runtime node와 결과 의미를 대상으로 하며 전투 중에는 독립된 compiled snapshot을 적용한다.

### 9. 잘못된 구조는 실행하지 않는다 (fail-closed)

commit 규칙을 우회할 수 있는 구조는 실행 전에 차단한다(Step 1 리뷰 P2).

- 택틱 priority selector의 자식은 택틱 노드(row 등)만 실행한다. 일반 BT 노드는 commit 구분을 보고하지 못하므로 실행하지 않고 건너뛰며 오류를 남긴다.
- row의 행동(마지막 자식)은 commit 구분을 보고하는 택틱 행동만 허용한다. 일반 `Failure`는 commit 후에도 다음 행으로 샐 수 있으므로, 비택틱 행동이면 행을 실행하지 않는다.
- 조건은 동기 순수 gate다. `Success`만 통과시키고 `Running`은 잘못된 구조로 보아 행동을 실행하지 않는다.

이 규칙은 Step 4의 정적 택틱 구조 validation이 편집 시점에 표면화하기 전의 런타임 안전망이다. 행동 시퀀스도
같은 규칙을 적용한다: 접근·행동 단계는 commit 구분을 보고하는 택틱 노드만 허용하고, 아니면 실행하지 않는다.

### 10. Feasibility와 행동 결과 계약 (Step 3 정련)

Step 3에서 접근 정책·행동 경계를 구현하며 seam을 다음으로 확정한다.

- **접근 정책 feasibility는 대상 확정 전에 셀렉터에서 필터한다**(§5 순서). `Immediate`는 "지금 사거리 또는 이번 턴 이동 후 행동 가능", `ApproachAllowed`는 "지금 사거리 또는 접근 경로 있음" 후보만 통과시킨다. 그래야 여러 후보 중 이번 턴 실제로 처리 가능한 대상에서 선택된다(H-3-2).
- **성립 검사는 mutation-free여야 한다**: 사거리·이동 가능성·경로 판정 중 grid solid·자원·위치를 변형하지 않는다. 실제 이동/지불은 접근·행동의 commit 경로에서만 발생한다.
- **이동량은 단일 소스 `Mobility+1`로 계산**하고, 성립 검사와 실제 이동이 같은 path/range 규칙을 공유한다.
- **행동 실행 결과를 보존한다**: 행동 어댑터는 `void`가 아니라 `Ineligible/Running/Completed/CancelledAfterCommit`을 반환해, 실제 `TurnActionBase.Action()`의 지불 전 실패(다음 행 fallback)·멀티턴 진행(Running)·완료를 택틱 결과로 정확히 매핑한다. `Ineligible`만 미commit이다.
- **commit 보고**: 접근(이동)과 행동(지불)은 commit 여부를 보고해, 시퀀스가 commit 이후 실패를 `CancelledAfterCommit`(fallback 금지)으로 승격한다.
- **per-action feasibility 바인딩**: 사거리·이동 가능성은 행의 **구체 action**(근접/스킬마다 range·비용이 다름)에 의존한다. 따라서 feasibility는 owner 단독이 아니라 **행의 action이 자신의 range/mobility/비용을 feasibility 계산에 공급**하는 방식으로 바인딩한다. owner가 제공하는 어댑터는 전장(AStar/타일) 접근을, 행의 action이 range/cost를 제공해 둘을 합성한다. 실제 `CharacterArticle` 어댑터·제품 어휘·전장 fixture 통합은 Step 3.5의 범위다.
- **turn reset 배선**: 멀티턴 action이 `CurrentTurnAction`으로 BT tick을 우회하면 트리 base status가 Running으로 남을 수 있으므로, 실제 턴 경계(`CharacterArticle.ApplyTurnStartEffects`)에서 택틱 루트 `ResetForNewTurn()`을 명시 호출한다. 수제 BT에는 no-op이다.

## Consequences

### Positive

- 플레이어가 읽은 행과 실제 행동의 의미가 일치한다.
- 실행 중 조건 변화가 행을 바꾸거나 한 턴에 두 행이 상태를 변경하는 일을 막는다.
- 영창 방해와 일반 접근의 사거리 밖 행동을 예측할 수 있다.
- 기존 일반 BT와 debugger의 status 계약을 보존한다.
- compiler/UI가 의존할 테스트 가능한 runtime 경계가 생긴다.

### Negative

- 일반 BT와 택틱 전용 node 계층이 함께 존재한다.
- path/range feasibility를 실제 이동과 공유하도록 기존 이동 코드를 추출해야 할 수 있다.
- row context와 `CurrentTurnAction` 사이 reset/소유권 구현이 복잡해진다.
- 전체 택틱 어휘와 compiler/UI는 별도 작업으로 남는다.

### Risks

- commit 관찰점이 action별로 다르면 commit 후 fallback이 다시 발생할 수 있다.
- eligibility와 실제 실행이 다른 path/range 계산을 사용하면 성립 직후 실패한다.
- row context의 Node reference가 death/free/teardown 뒤 남으면 반복 전투를 오염시킨다.
- priority selector와 row가 모두 같은 latch를 소유하면 영구 Running 또는 reset 누락이 생길 수 있다.

## Alternatives Considered

### Extend BtStatus globally

기각 권고. 모든 composite, editor, debugger payload와 status switch에 파급되며 기존 수제 BT 의미까지 바뀐다.

### Change the existing Selector and Sequence to memory composites

기각 권고. 기존 BT가 매 tick 재평가에 의존할 가능성이 있고 택틱 row의 commit·대상 문맥까지 해결하지 못한다.

### Execute TacticBoard directly without BehaviorTree

기각 권고. 기존 action 실행, debugger, replay와 결정론 기반을 중복 구현하게 된다. 택틱보드는 제한된 DSL이고 BT는 내부 runtime으로 유지한다.

### Allow fallback after movement or cost payment

기각. 한 턴에 여러 판단과 상태 변경이 발생해 우선순위 행 문장의 의미가 깨진다.

### Always approach out-of-range targets

기각. 영창 방해처럼 이번 턴에 효력이 있어야 하는 행이 이미 끝날 기회를 쫓아가며 턴을 낭비한다.

## Resolved in Step 0 (OD1~8)

Step 0 설계 리뷰가 아래로 확정했다(근거·파일 라인은 [[BT-002-Tactic-Board-Runtime-Extension-Review]]).

- **latch 인계점**: 행 latch는 selector가 소유하며 **action과 approach 두 단계 모두**를 덮는다. approach(이동)는 `CurrentTurnAction`을 거치지 않으므로 selector의 Running latch가 preempt를 막는다. `CurrentTurnAction`은 멀티턴 action 재개에만 쓰고, 새 자기 턴 배선은 `ResetForNewTurn()`을 명시 호출한다(Step 3). OnInit 기반 reset은 terminal 경로의 보조 수단일 뿐 턴 경계의 유일한 근거가 아니다.
- **feasibility/commit API**: 성립 검사는 실제 이동과 같은 path/range 규칙을 mutation 없이 공유하는 read-only helper로 만든다(`SetPointSolid` 변형 경로 미사용). commit은 adapter가 명시 보고한다(action=`CurrentTurnAction` 세팅, approach=첫 이동 시작).
- **이동량 단일 소스**: "이번 턴 이동 후 행동 가능"의 이동량은 단일 소스(`Mobility+1`)로 통일하고, 실증 이동 노드를 그 소스에 맞춘다(현 Move 1칸 vs MultipleMove `Mobility+1` 이원화 해소).
- **후보 identity**: v0는 live Godot Node instance identity 교집합으로 충분하다. freed 후보는 `IsInstanceValid`로 제외하고, stable `unit_id` seam은 후속으로 예약한다.
- **실증 어휘**: Step 1~2는 test condition/action double로 상태 기계·후보 교집합을 검증한다. Step 3은 접근 정책·commit 경계의 런타임 상태 기계를 승인된 어댑터 대역으로 검증한다. `Always`/`EnemyCasting`/`ContextTarget`/`NearestEnemy` + 근접 공격의 **제품 어휘 실증과 실제 전투 어댑터 배선은 Step 3.5**다(§10 per-action feasibility 바인딩).

## Follow-ups

- 택틱보드 Resource와 validator/compiler, compiled snapshot.
- 전투 전 편집·검증·적용 UX와 BattleSession party snapshot.
- 전체 조건/행동/selector 어휘, 발동 통계와 전투 후 리포트.
- DungeonRun의 층 준비와 다음 층 재설정 흐름.

## Related

- [[BT-002-Tactic-Board-Runtime-Extension]]
- [[BehaviorTree-System]]
- [[BT-001-BehaviorTree-Graph-Editor-Debugger]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[SK-004-Usable-Attack-Gate]]
- [[Battle-Session-System]]