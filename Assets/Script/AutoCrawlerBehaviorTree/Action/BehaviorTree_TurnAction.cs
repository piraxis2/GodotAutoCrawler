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
    [Export] CSharpScript _actionScript;

    private TurnActionBase _turnAction;


    protected override Constants.BtStatus PerformAction(double delta, Node owner)
    {
        if (owner is ITurnAffected<ArticleBase> article)
        {
            _turnAction ??= (TurnActionBase)_actionScript?.New();
            
            if (_turnAction == null) return Constants.BtStatus.Failure;
            
            _turnAction.Init();
            TurnActionBase.ActionState actionStatus = _turnAction.Action(delta, article as ArticleBase);
            if (actionStatus is TurnActionBase.ActionState.Executed or TurnActionBase.ActionState.Running)
            {
                article.CurrentTurnAction = _turnAction;
                if (actionStatus == TurnActionBase.ActionState.Running)
                {
                    return Constants.BtStatus.Running;
                }
            }
            return Constants.BtStatus.Success;
        }
        return Constants.BtStatus.Failure;
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _turnAction?.Free();
    }
}