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

    public int MinStrikingDistance
    {
        get
        {
            if (BehaviorTree == null) throw new NullReferenceException("BehaviorTree is null");

            var skills = BehaviorTree.FindNodeByType(typeof(BehaviorTree_TurnAction));
            return skills?.OfType<BehaviorTree_TurnAction>()
                .Select(x => x.TurnAction)
                .OfType<ISkill<TurnActionBase>>()
                .Select(x => x.Distance)
                .Min() ?? 1;
        }
    }

    public HashSet<Vector2I> AttackRangePositions
    {
        get
        {
            HashSet<Vector2I> GetAdjacentTiles(Vector2I position)
            {
                return new HashSet<Vector2I>
                {
                    position + new Vector2I(1, 0), // 동
                    position + new Vector2I(-1, 0), // 서
                    position + new Vector2I(0, 1), // 남
                    position + new Vector2I(0, -1) // 북
                };
            }

            HashSet<Vector2I> strikingArea = new(GetAdjacentTiles(TilePosition));
            HashSet<Vector2I> completedArea = new() { TilePosition };

            for (int i = 1; i < MinStrikingDistance; i++)
            {
                foreach (var area in strikingArea.ToList())
                {
                    if (completedArea.Add(area))
                    {
                        strikingArea.UnionWith(GetAdjacentTiles(area));
                    }
                }
            }

            return strikingArea;
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