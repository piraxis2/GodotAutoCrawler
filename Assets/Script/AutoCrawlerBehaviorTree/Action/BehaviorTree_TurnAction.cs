using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;

[GlobalClass, Tool]
public partial class BehaviorTree_TurnAction : BehaviorTree_Action
{
    [Export]
    public TurnActionBase TurnAction { get; private set; }

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        if (owner is ITurnAffectedArticle<ArticleBase> article)
        {
            if (TurnAction == null) return BtStatus.Failure;
            
            TurnAction.Init(this);
            ActionState actionStatus = TurnAction.Action(delta, article as ArticleBase);
            // 지불/검증 실패는 CurrentTurnAction을 세우지 않고 Selector 다음 행으로 넘긴다.
            if (actionStatus == ActionState.Failure) return BtStatus.Failure;
            if (actionStatus is ActionState.Executed or ActionState.Running)
            {
                article.CurrentTurnAction?.Finish(this);
                article.CurrentTurnAction = TurnAction;
                if (actionStatus == ActionState.Running)
                {
                    return BtStatus.Running;
                }
            }
            return BtStatus.Success;
        }
        return BtStatus.Failure;
    }
}