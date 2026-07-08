using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction;

public enum ActionState
{
    Executed,
    Running,
    End,
    // 지불/검증 실패로 행동을 시작하지 못함. BT는 Selector 다음 행으로 넘어간다(상태 변경 없음).
    Failure
}

[GlobalClass, Tool]
public abstract partial class TurnActionBase : Resource 
{
    protected Queue<Func<double, ArticleBase, ActionState>> ActionQueue = [];
    
    // Action 사거리
    protected abstract int Range { get; }
    // Action 범위
    protected abstract int Scale { get; }
    
    // Action이 닿는 위치 (ADR-017 정규 순서로 정렬됨)
    private IReadOnlyList<Vector2I> _attackRangePositions;
    public IReadOnlyList<Vector2I> AttackRangePositions => _attackRangePositions ??= SkillUtil.GetAttackRangePositions(Range);


    protected virtual int MasterCost => 1;
    
    private int _usedCost = 0;

    protected int Cost => MasterCost - _usedCost;

    public virtual void Init(Node owner)
    {
        _usedCost = 0;
        ActionQueue.Clear();
        OnInit(owner);
    }
    public virtual void Finish(Node owner)
    {
        _usedCost = 0;
        ActionQueue.Clear();
        OnFinish(owner);
    }

    protected ArticleBase GetTarget(Node owner)
    {
        ArticleBase ownerArticle = (ArticleBase)((BehaviorTree_Action)owner).Tree.GetParent();
        List<Vector2I> calculatedAttackRange = AttackRangePositions.Select(p => p + ownerArticle.TilePosition).ToList();
        BattleFieldTileMapLayer tileMapLayer = BattleFieldScene.BattleField.BattleFieldTileMap;
        ArticleBase target = tileMapLayer?.GetArticles(calculatedAttackRange)?
            .Where(t => t is { IsAlive: true } && t.IsOpponent(ownerArticle))
            .OrderByCanonical(t => t.TilePosition, ownerArticle.TilePosition)
            .FirstOrDefault();
        if (target != null) ownerArticle.DecisionFlipH(target.TilePosition);
        return target;
    }
    protected virtual void OnInit(Node owner){}
    
    protected virtual void OnFinish(Node owner){}
    
    protected virtual void OnUsedCostChanged(int oldCost, int newCost){}

    public virtual ActionState Action(double delta, ArticleBase owner)
    {
        if (Cost <= 0) return ActionState.End;

        ActionState status = ActionExecute(delta, owner);

        if (status != ActionState.Running)
        {
            OnUsedCostChanged(_usedCost++, _usedCost);
        }
        
        return Cost <= 0 ? ActionState.End : status;
    }

    protected virtual ActionState ActionExecute(double delta, ArticleBase owner)
    {
        return ActionQueue.Peek()(delta, owner);
    }
}