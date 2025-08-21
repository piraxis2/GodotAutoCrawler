using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public partial class CharacterArticle : ArticleBase, ITurnAffectedArticle<ArticleBase>
{
    public TurnActionBase CurrentTurnAction { get; set; }
    private BehaviorTree _behaviorTree;
    public BehaviorTree BehaviorTree => _behaviorTree ??= GetNode<BehaviorTree>("BehaviorTree");
    public int Priority { get; set; }

    private HashSet<Vector2I> _attackRangePositions;
    private HashSet<Vector2I> AttackRangePositions
    {
        get
        {
            if (_attackRangePositions != null) return _attackRangePositions;
            _attackRangePositions = BehaviorTree.FindNodeByType(typeof(BehaviorTree_TurnAction))
                .OfType<BehaviorTree_TurnAction>()
                .Where(action => action.TurnAction is ISkill<TurnActionBase> skill)
                .Select(action => ((ISkill<TurnActionBase>)action.TurnAction).AttackRangePositions)
                .OrderByDescending(positions => positions.Count)
                .FirstOrDefault();
            return _attackRangePositions;
        }
    }

    public List<Vector2I> CalculatedAttackRange => AttackRangePositions.Select(p => p + TilePosition).ToList();

    public Constants.BtStatus TurnPlay(double delta)
    {
        if (BehaviorTree == null) throw new NullReferenceException("BehaviorTree is null");
        
        // 턴마다 영향을 주는 상태를 적용
        ArticleStatus.ApplyAffectingStatuses();

        if (CurrentTurnAction == null) return BehaviorTree.Behave(delta, this);
        
        // 현재 턴 액션이 null이 아닐 경우, 액션을 실행
        TurnActionBase.ActionState actionState = CurrentTurnAction.Action(delta, this);

        if (actionState == TurnActionBase.ActionState.End) CurrentTurnAction = null;

        // 액션이 실행 중인 경우, 상태를 Running으로 반환
        // 액션이 실행 완료된 경우, 상태를 Success로 반환
        return actionState == TurnActionBase.ActionState.Running ? Constants.BtStatus.Running : Constants.BtStatus.Success;

    }

}