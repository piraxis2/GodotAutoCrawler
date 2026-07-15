---
id: BT-002-review
type: review
status: design-review
updated: 2026-07-14
system: BehaviorTree
step: 0
---

# BT-002 Tactic Board Runtime Extension — Step 0 Design Review

## Scope

이 리뷰는 [[BT-002-Tactic-Board-Runtime-Extension]] **Step 0 (Design Review)** 전용이다. 제품 코드/`.tscn`/`.tres`는 수정하지 않는다.

목표는 R1~R9 최소 계약을 현재 BT/TurnAction/TurnHelper 경로로 구현 가능한지 실제 코드와 정적 대조하고, Open Decisions 1~8을 닫아 Step 1~4 경계를 확정하는 것이다.

## 대조한 실측 코드

- 상태 열거: `addons/behaviortree/Constants.cs:10` — `enum BtStatus { Success, Failure, Running }` (3값 고정).
- Composite tick: `addons/behaviortree/node/BehaviorTree_Selector.cs:8-18`, `BehaviorTree_Sequence.cs:8-18` — 매 tick 첫 자식부터 재평가, Running child index 미기억.
- Node 수명: `addons/behaviortree/node/BehaviorTree_Node.cs:46-71` — `_status`가 Success/Failure이면 `OnInit`+`_elapsedTime` 리셋, Running이면 유지. Row/branch latch는 없음.
- 턴 진입: `Assets/Script/Article/CharacterArticle.cs:91-117` — `CurrentTurnAction == null`일 때만 `BehaviorTree.Behave`, 아니면 action 직접 재개. `ActionState.Failure → BtStatus.Failure`.
- Action 어댑터: `Assets/Script/AutoCrawlerBehaviorTree/Action/BehaviorTree_TurnAction.cs:16-38` — 진입마다 `TurnAction.Init(this)` 후 `Action`; Executed/Running이면 `CurrentTurnAction` 세팅(=commit 관찰점), pay/validate 실패는 `Failure`.
- Action 기반: `Assets/Script/TurnAction/TurnActionBase.cs:43-96` — `CanStart(caster)`는 base에서 null 체크만, `GetTarget`은 live tile 질의 + `IsAlive`/`IsOpponent` + `OrderByCanonical`, `Action`이 비용 지불/큐 실행을 함께 수행.
- 이동: `Assets/Script/AutoCrawlerBehaviorTree/Action/BehaviorTree_Move.cs:73-108` — `_moveTween`/`_targetPosition`을 노드 내부에 보관, Running 동안 `CurrentTurnAction`을 세우지 **않음**. 한 턴 1칸(`path[1]`).
- 다중 이동: `Assets/Script/AutoCrawlerBehaviorTree/Action/BehaviorTree_MultipleMove.cs:56-60` — 한 턴 이동량 = `Mobility.Value + 1`(기본 3)칸.
- 대상 재검색: `TurnActionBase.GetTarget()` 및 `BehaviorTree_Move.FindTarget()` 모두 확정 대상이 아니라 상대 전체를 매번 재질의.
- feasibility: `Assets/Script/TurnAction/Skill/SkillUtil.cs:16-59` — `OrderByCanonical`(거리→Y→X), `GetAttackRangePositions`(순수), `SelectCanonicalPath`. 단 `FindTarget`은 `_aStar2D.SetPointSolid`로 grid를 **변형**한다.
- 스케줄러: `Assets/Script/TurnHelper.cs:181-192` — `IsAlive:false`면 턴 skip, `TurnPlay`가 Success/Failure면 `AdvanceToNextTurn`. Running은 같은 유닛 유지.
- validation: `addons/behaviortree/BehaviorTreeValidation.cs:35-144` — 구조적 child 개수/타입만 검사. 택틱 행 의미 모름.
- 설계 근거: `GameDesign/기획서/09_택틱보드.md` H-3(27~53), H-3-1(37~53), H-3-2(55~76), H-8(173~217).

## Findings

설계는 전반적으로 완결적이다. Risks 절이 주요 위험을 이미 식별했고 R1~R9와 제안 컴포넌트가 일관된다. **구현을 막는 P0/P1 설계 결함은 없다.** 다만 Step 1~3 진입 전에 명문화해야 할 seam이 몇 가지 있으며, 이는 Open Decisions에서 닫는다.

### P2

1. **[P2] approach(이동) 단계는 `CurrentTurnAction`으로 latch되지 않는다 — R7/OD2 명문화 필요**
   - 실측: `BehaviorTree_Move.PerformAction`은 tween 실행 중 `Running`을 반환하면서 `CurrentTurnAction`을 세우지 않는다(`BehaviorTree_Move.cs:73-108`). 그러면 다음 tick에 `CharacterArticle.TurnPlay`는 `CurrentTurnAction == null` 경로로 `BehaviorTree.Behave`를 재호출해 Selector를 **위에서 아래로 재평가**한다(`CharacterArticle.cs:102`).
   - 영향: 현재 수제 BT는 안정적 decorator 뒤에 Move를 두어 같은 분기가 매 tick 재선택되므로 tween이 이어진다. 그러나 R7이 요구하는 "상위 조건이 바뀌어도 preempt 금지"는 이 구조로 보장되지 않는다. 이동 중 상위 행 조건이 성립하면(예: 다른 적이 이번 tick 영창 시작) 진행 중 tween이 버려지고(누수) 한 턴에 두 판단이 실행될 수 있다(Risks 5번).
   - 결론: Task는 action 재개를 `CurrentTurnAction`에, 이동 latch를 PrioritySelector에 두라고 권장(OD2)한다. 이 리뷰는 **latch가 action과 approach 두 단계를 모두 덮어야 한다**는 점을 명시적으로 요구한다. Step 1은 action-only 상태 기계라 이 seam이 드러나지 않으므로, Step 3(Approach) 완료 조건에 "이동 Running 중 상위 preempt 없음 + tween 미누수"를 반드시 포함한다.

2. **[P2] 순수 성립 검사(R3)를 위한 mutation-free feasibility API가 아직 없다 — OD3/OD5**
   - 실측: 대상/사거리/접근 가능성은 현재 `TurnActionBase.Action`(비용 지불·큐 실행 포함)과 `BehaviorTree_Move.FindTarget`(`_aStar2D.SetPointSolid`로 grid 변형)에 얽혀 있다. `CanStart`는 base에서 null 체크뿐이라 마나/탄약/대상/사거리 판정을 담지 않는다(`TurnActionBase.cs:43-45`).
   - 영향: R3 "검사 중 자원 지불/이동/상태 효과 없음"과 R5 "실제 이동과 같은 path/range를 mutation 없이 공유"를 만족하려면, `Action` 호출 전에 `(유효 대상 존재) ∧ (행동 시작 가능) ∧ (접근 정책 충족)`을 판정하는 별도 read-only seam이 필요하다. 이 seam은 지금 존재하지 않으므로 Step 0에서 API 형태를 확정해야 한다(OD3/OD5에서 닫음).

3. **[P2] "이번 턴 이동 후 행동 가능"의 이동량 정의가 이원화되어 있다 — R5/OD5**
   - 실측: `BehaviorTree_Move`는 한 턴 1칸(`path[1]`, `BehaviorTree_Move.cs:61`), `BehaviorTree_MultipleMove`는 `Mobility+1`칸(`BehaviorTree_MultipleMove.cs:56-60`)을 이동한다.
   - 영향: `Immediate` 성립 판정(R5)은 "이번 턴 이동 후 사거리 진입"을 계산하는데, 이동량 소스가 두 개면 feasibility와 실제 실행이 어긋나 성립 후 실행 실패가 반복될 수 있다. 실증 어휘(OD7)가 어느 이동 노드를 쓰는지와 이동량 단일 소스를 OD5에서 확정한다.

### P3

4. **[P3] Row 성립의 `Failure` 의미 과부하 — OD1 문서화**
   - `BtStatus.Failure`는 이미 (a) Selector fallback 신호와 (b) `TurnPlay`의 commit 전 pay/validate 실패(`CharacterArticle.cs:113`) 두 의미로 쓰인다. 택틱 내부 결과(`Ineligible`/`CancelledAfterCommit`)를 여기에 매핑할 때, `TacticPrioritySelector`는 일반 `Selector`처럼 "Failure면 다음 자식"을 그대로 쓰면 안 된다(commit 후 실패는 fallback 금지). 경계 변환 규칙을 코드 주석/ADR에 고정한다.

5. **[P3] `TacticRowContext`의 확정 대상은 free 가드가 필요 — R9**
   - action 내부(`TurnAction_Attack._target`)는 `IsAlive` 체크로 방어하지만, RowContext가 commit→execute 경계에서 대상을 보관할 때 `IsInstanceValid`+`IsAlive`로 재확인해야 한다([[dialogue-provider-validation-pattern]] 동형). Node 참조를 턴 넘어 보관하지 않는다(R9).

## Open Decisions (Step 0 종결)

- **OD1 상태 표현 — 확정: `BtStatus` 3값 유지 + 택틱 내부 결과 enum.**
  전역 확장은 `Selector`/`Sequence`/editor/debugger의 모든 status 처리에 회귀를 만든다(`Constants.cs:10` 소비처 전역). 내부 결과(`Ineligible`/`Running`/`ActionCompleted`/`ApproachConsumed`/`CancelledAfterCommit`)를 `TacticPrioritySelector` 경계에서 Task 제안 매핑대로 변환한다.

- **OD2 행 latch 소유자 — 확정: `TacticPrioritySelector`가 Running child latch 소유, `TacticRow`가 내부 단계 소유.**
  단, 이 latch는 **action과 approach 두 단계 모두**를 덮어야 한다(Finding P2-1). `CurrentTurnAction`은 멀티턴 action 재개 용도로만 유지하고, latch 해제는 새 자기 턴(R1)에서 수행한다. `CurrentTurnAction`과 latch가 동시에 같은 행을 소유하지 않도록 소유 경계를 코드 주석에 고정한다.

- **OD3 Commit 관찰점 — 확정: adapter가 commit을 명시 보고한다.**
  action은 `CurrentTurnAction` 세팅 시점(`BehaviorTree_TurnAction.cs:29`)이 자연스러운 commit 신호다. approach는 첫 tween 시작(`BehaviorTree_Move.cs:102`)이 commit이며 `CurrentTurnAction`을 거치지 않으므로 **별도 commit 보고 seam**이 필요하다. 성립 검사(R3)는 commit 이전 read-only 경로로 분리하고, 실제 실행에서만 1회 지불한다(R3/R6).

- **OD4 조건 후보 identity — 확정: v0는 live Godot Node identity 교집합.**
  현 코드는 대상을 매 평가 재질의하며 stable id가 없다(`GetTarget`). v0 교집합은 Node 참조 동일성으로 하고, 향후 stable `unit_id` seam은 후속(Follow-ups)로 남긴다. freed 후보는 `IsInstanceValid`로 제외(Finding P3-5).

- **OD5 접근 가능성 검사 — 확정: 실제 이동과 같은 path/range를 mutation 없이 공유하는 read-only helper 신설.**
  `SkillUtil.GetAttackRangePositions`(순수)와 canonical path 규칙을 재사용하되, `FindTarget`의 `SetPointSolid` 변형 경로는 성립 검사에서 쓰지 않는다. 이동량은 단일 소스(권장: `Mobility+1`)로 통일하고 실증 이동 노드를 그 소스에 맞춘다(Finding P2-3).

- **OD6 Commit 후 대상 무효 — 확정: `CancelledAfterCommit`으로 턴 종료, fallback 금지.**
  `TacticPrioritySelector`는 이 내부 결과를 `BtStatus.Success`로 변환해 스케줄러가 `AdvanceToNextTurn`하도록 한다(`TurnHelper.cs:187-191`). 다음 행을 실행하지 않는다.

- **OD7 최소 실증 어휘 — 확정: 테스트 대역 우선 + 최소 제품 어휘.**
  Step 1~2는 test condition/action double로 상태 기계·후보 교집합을 검증한다. Step 3는 `Always`, `EnemyCasting`(→`CharacterArticle.IsCasting`, `CharacterArticle.cs:24`), `ContextTarget`, `NearestEnemy`, 그리고 `TurnAction_Attack`(근접)로 `Immediate`/`ApproachAllowed` 두 사례를 실증한다. H-4 전체 어휘는 후속.

- **OD8 ADR 필요성 — 확정: ADR-023 필요, Step 1 전 accepted.**
  status 매핑(OD1), commit 경계(OD3/OD6), context identity/lifetime(OD4/OD5)은 BT addon과 전투 전반에 걸친 장기 결정이므로 ADR로 고정한다. 다음 ADR 번호는 `40_Decisions` 최신이 ADR-022이므로 **ADR-023-Tactic-Runtime**이 맞다.

## Step Assessment

- **Step 0 (본 리뷰)**: 입력=Task/기획/코드, 출력=OD1~8 종결+본 문서. 완료 조건 충족(아래 판정). ADR-023 authoring이 유일한 선행 잔여.
- **Step 1 (Priority/Row Lifecycle)**: 경계 적절. test double 기반이라 approach latch seam(P2-1)이 드러나지 않음 → Step 1 완료 조건은 action-only latch·turn reset·인스턴스 격리로 한정하고, approach preempt는 Step 3로 명시 이월. 분할·병합 불필요.
- **Step 2 (Condition Candidates/Target Context)**: 후보 교집합·gate·tie-break·free 가드가 한 경계에 모여 적절. `OrderByCanonical` 재사용 가능(`SkillUtil.cs:16`). 유지.
- **Step 3 (Approach/Action/Wait)**: 가장 위험한 단계. P2-1(approach latch/preempt/tween 누수)과 P2-2/P2-3(mutation-free feasibility, 이동량 단일화)을 여기서 실증해야 하므로 완료 조건에 명시 추가 권장. 유지하되 범위 감시.
- **Step 4 (Lifecycle/Debug/Validation)**: death/exit/teardown reset, RowId debug payload, 택틱 child 구조 validation(`BehaviorTreeValidation` 확장). 적절.

## Verification Assessment

계획된 headless 테스트 매트릭스(Task 291~307, 14케이스)는 R1~R9를 대체로 덮는다. 아래 실패 시나리오를 명시 추가 권장:

- **approach preempt/누수 (P2-1)**: 이동 Running 중 상위 행 조건을 인위적으로 성립시켜도 같은 행 유지 + orphan tween 없음. (매트릭스 3번의 approach 버전, 현재 매트릭스엔 action 버전만 존재)
- **feasibility 무변형 (P2-2)**: 성립 검사 전후 grid solid 상태·마나·탄약·위치 불변(매트릭스 13번을 approach/skill까지 확장). `SetPointSolid` 부작용 회귀 가드.
- **이동량 경계 (P2-3)**: `Immediate` 대상이 이동량 소스 기준 경계(정확히 도달/1칸 부족)에서 성립/불성립이 갈리는 케이스(매트릭스 4·5의 경계값).
- **회귀 대역**: `BehaviorTreeValidationTest`(수제 BT 무회귀), CB-001 deterministic combat, `battle_field.tscn` 상대 1체 개편([[battlefield-skill-test-gotchas]] — 다중상대/절대 HP 단언 함정 주의), `dotnet build` + Godot `--import`([[godot-headless-verification]]).

빠진 대역 없음. 테스트 대역(test condition/action double)은 Step 1~2에서 실장 어휘 없이 상태 기계를 격리 검증 가능한 구조로 확인됨.

## Verdict

**Approved after design fixes**

설계는 구현 가능하며 P0/P1 설계 결함이 없다. 승인 조건인 "design fixes"는 아래 세 항목이며, 모두 Step 1 구현 전 문서 반영으로 닫힌다:

1. OD1~8을 위 확정안대로 Task/ADR에 반영(특히 OD2 latch 범위 = action+approach, OD3 approach commit seam, OD5 이동량 단일 소스).
2. **ADR-023-Tactic-Runtime**을 accepted 상태로 작성(status 매핑·commit 경계·context lifetime).
3. Step 1 완료 조건을 action-only로 한정하고 approach preempt/feasibility 무변형(P2-1~P2-3)을 Step 3 완료 조건·검증에 명시 이월.

이 세 반영 후 Step 1 구현으로 진행 가능하다. 남은 가정: 실증 이동 노드(Move vs MultipleMove) 선택과 이동량 소스는 OD5 권장(`Mobility+1`) 기준이며 Step 3 착수 시 재확인한다.

## Related

- [[BT-002-Tactic-Board-Runtime-Extension]]
- [[STEP_REVIEW_WORKFLOW]]
- [[Design-Review-Prompt]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[BehaviorTree-System]]
