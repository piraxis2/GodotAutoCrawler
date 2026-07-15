using System.Collections.Generic;
using Godot;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.TurnAction;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 실제 전투 행동(BT-002 Step 3.5). 기존 `TurnActionBase`(근접 공격·스킬)를 택틱 행의 행동으로 감싼다.
//
//  - spec(§10): 사거리는 `TurnActionBase.AttackRangePositions`, 시작 가능성은 `CanStart`(비용/탄약, 지불 없음).
//    셀렉터·접근이 이 spec으로 feasibility를 계산하므로 근접/스킬마다 올바른 사거리가 적용된다.
//  - 확정 대상 공유(R4): 실행 전에 `ExplicitTarget`을 행의 확정 대상으로 지정해, 행동이 상대를 다시 검색하지
//    않고 조건·접근과 같은 대상을 친다.
//  - 결과 보존: `TurnActionBase.Action()`의 Failure/Running/Executed/End를 택틱 결과로 매핑한다.
//    멀티턴(Executed로 비용이 남음)은 `CurrentTurnAction`을 세워 다음 턴에 `CharacterArticle.TurnPlay`가
//    BT를 거치지 않고 재개한다. 그때 트리는 Running latch로 남으므로, 다음 자기 턴의
//    `ApplyTurnStartEffects -> ResetForNewTurn`이 latch/문맥을 초기화한다(ADR-023 §10).
[GlobalClass, Tool]
public partial class TacticTurnAction : TacticExecuteAction, ITurnActionProvider
{
    [Export] public TurnActionBase TurnAction { get; set; }

    // 새 자기 턴에는 이전 턴의 확정 대상 바인딩을 푼다(stale 대상으로 락온하지 않도록).
    public override void ResetForNewTurn()
    {
        base.ResetForNewTurn();
        TurnAction?.ClearExplicitTarget();
    }

    public override IReadOnlyList<Vector2I> AttackRangePositions =>
        TurnAction?.AttackRangePositions ?? System.Array.Empty<Vector2I>();

    public override bool CanStart(Node owner) =>
        TurnAction != null && owner is CharacterArticle character && TurnAction.CanStart(character);

    protected override TacticActionOutcome ExecuteAction(double delta, Node owner, GodotObject target)
    {
        if (TurnAction == null || owner is not CharacterArticle article)
        {
            GD.PushError($"TacticTurnAction '{Name}': TurnAction이 없거나 owner가 CharacterArticle이 아닙니다.");
            return TacticActionOutcome.Ineligible;
        }

        if (target is not ArticleBase targetArticle || !targetArticle.IsAlive)
        {
            // commit 여부는 base가 판단한다(여기서는 실행하지 않음).
            return Committed ? TacticActionOutcome.CancelledAfterCommit : TacticActionOutcome.Ineligible;
        }

        // 행이 확정한 대상을 행동에 바인딩한다(R4). 바인딩이 활성이면 근접 공격의 GetTarget도, 스킬의
        // SelectTarget도 이 대상만 쓰고 다른 적으로 fallback하지 않는다.
        TurnAction.BindExplicitTarget(targetArticle);

        // 행동 시작은 한 번만 초기화한다(비용/큐 리셋). 이후 tick은 진행만 이어간다.
        if (!Committed) TurnAction.Init(this);

        ActionState state = TurnAction.Action(delta, article);

        switch (state)
        {
            case ActionState.Failure:
                // 지불/검증 실패 — 상태 변경 없음. 다음 행으로 fallback한다.
                return TacticActionOutcome.Ineligible;

            case ActionState.Running:
                // 이번 tick 진행 중(애니메이션 등). 턴을 넘기지 않는다.
                article.CurrentTurnAction = TurnAction;
                return TacticActionOutcome.Running;

            case ActionState.Executed:
                // 턴을 소비했지만 비용이 남았다(멀티턴 영창). 다음 턴은 CurrentTurnAction으로 재개된다.
                article.CurrentTurnAction = TurnAction;
                return TacticActionOutcome.Completed;

            default: // End
                // 완전히 끝났다. 재개할 것이 없다.
                return TacticActionOutcome.Completed;
        }
    }
}
