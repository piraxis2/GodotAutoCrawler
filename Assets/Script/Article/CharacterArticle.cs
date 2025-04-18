﻿using System;
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
        
        ArticleStatus.ApplyAffectingStatuses();

        if (CurrentTurnAction != null)
        {
            TurnActionBase.ActionState actionState = CurrentTurnAction.Action(delta, this);

            if (actionState == TurnActionBase.ActionState.End) CurrentTurnAction = null;

            return actionState == TurnActionBase.ActionState.Running ? Constants.BtStatus.Running : Constants.BtStatus.Success;
        }

        return BehaviorTree.Behave(delta, this);
    }

}