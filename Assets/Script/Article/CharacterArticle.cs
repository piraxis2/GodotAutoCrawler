using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;

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

    public int MaxStrikingDistance
    {
        get
        {
            if (BehaviorTree == null) throw new NullReferenceException("BehaviorTree is null");

            var actions = BehaviorTree.FindNodeByType(typeof(BehaviorTree_TurnAction));
            int maxDistance = 1;
            foreach (BehaviorTree_TurnAction action in actions)
            {
                if (action.TurnAction is ISkill<TurnActionBase> skillBase)
                {
                    if (maxDistance < skillBase.Distance)
                    {
                        maxDistance = skillBase.Distance;
                    }
                }
            }
            return maxDistance;
        }
    }

    private HashSet<Vector2I> _attackRangePositions;
    private HashSet<Vector2I> AttackRangePositions
    {
        get
        {
            if (_attackRangePositions == null)
            {
                HashSet<Vector2I> GetAdjacentTiles(Vector2I position)
                {
                    return new HashSet<Vector2I>
                    {
                        position + Vector2I.Right, // 동
                        position + Vector2I.Left, // 서
                        position + Vector2I.Down, // 남
                        position + Vector2I.Up, // 북
                    };
                }

                HashSet<Vector2I> strikingArea = new(GetAdjacentTiles(Vector2I.Zero));
                HashSet<Vector2I> completedArea = new() { Vector2I.Zero };

                for (int i = 1; i < MaxStrikingDistance; i++)
                {
                    foreach (var area in strikingArea.ToList())
                    {
                        if (completedArea.Add(area))
                        {
                            strikingArea.UnionWith(GetAdjacentTiles(area));
                        }
                    }
                }

                _attackRangePositions = strikingArea;
            }
            return _attackRangePositions;
        }
    }

    public List<Vector2I> CalculatedAttackRange => AttackRangePositions.Select(p => p + TilePosition).ToList();

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