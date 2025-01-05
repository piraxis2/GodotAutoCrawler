using System;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.TurnAction;

namespace AutoCrawler.Assets.Script.Article;

public partial class CharacterArticle : ArticleBase, ITurnAffected<ArticleBase>
{
    private BehaviorTree _behaviorTree;
    private TurnActionBase _currentTurnAction;
    public TurnActionBase CurrentTurnAction
    {
        get => _currentTurnAction;
        set => _currentTurnAction = value;
    }

    public BehaviorTree BehaviorTree => _behaviorTree ??= GetNode<BehaviorTree>("BehaviorTree");
    public int Priority { get; set; }

    public int MinStrikingDistance
    {
        get
        {
            var skills = BehaviorTree.FindNodeByType(typeof(BehaviorTree_Skill));
            return skills?.OfType<BehaviorTree_Skill>()
                .Select(skill => skill.StrikingDistance)
                .DefaultIfEmpty(0)
                .Min() ?? 1;
        }
    }

    public Constants.BtStatus TurnPlay(double delta)
    {
        if (BehaviorTree == null) throw new NullReferenceException("BehaviorTree is null");

        if (CurrentTurnAction != null)
        {
            TurnActionBase.ActionState actionState = CurrentTurnAction.Action(delta, this);
            
            if (actionState == TurnActionBase.ActionState.End) _currentTurnAction = null;
            
            return actionState == TurnActionBase.ActionState.Running ? Constants.BtStatus.Running : Constants.BtStatus.Success;
        }
       
        return BehaviorTree.Behave(delta, this);
    }
}