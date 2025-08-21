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

    protected override Constants.BtStatus PerformAction(double delta, Node owner)
    {
        if (owner is ITurnAffectedArticle<ArticleBase> article)
        {
            if (TurnAction == null) return Constants.BtStatus.Failure;
            
            TurnAction.Init(this);
            TurnActionBase.ActionState actionStatus = TurnAction.Action(delta, article as ArticleBase);
            if (actionStatus is TurnActionBase.ActionState.Executed or TurnActionBase.ActionState.Running)
            {
                article.CurrentTurnAction?.Finish(this);
                article.CurrentTurnAction = TurnAction;
                if (actionStatus == TurnActionBase.ActionState.Running)
                {
                    return Constants.BtStatus.Running;
                }
            }
            return Constants.BtStatus.Success;
        }
        return Constants.BtStatus.Failure;
    }
}