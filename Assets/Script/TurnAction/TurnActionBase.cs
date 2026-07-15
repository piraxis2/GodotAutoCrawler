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

    public virtual bool CanStart(CharacterArticle caster)
    {
        return caster != null;
    }

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

    // --- 명시적 대상 바인딩(BT-002 R4) ---
    // 택틱 행이 확정한 대상을 행동에 바인딩한다. 바인딩이 "활성"이면 행동은 상대를 다시 검색하지 않고 이 대상만
    // 친다 — 조건·접근·행동이 같은 대상을 공유한다. 바인딩됐는데 대상이 무효(사망/free)면 다른 적으로
    // **fallback하지 않는다**(null 반환 → 행동 실패). 바인딩 여부와 대상 유효성은 별개의 계약이다.
    // 기존 수제 BT 경로는 바인딩하지 않으므로 검색 동작이 그대로 유지된다.
    private ArticleBase _explicitTarget;
    private bool _explicitTargetBound;

    public bool IsExplicitTargetBound => _explicitTargetBound;

    public void BindExplicitTarget(ArticleBase target)
    {
        _explicitTarget = target;
        _explicitTargetBound = true;
    }

    public void ClearExplicitTarget()
    {
        _explicitTarget = null;
        _explicitTargetBound = false;
    }

    // 바인딩된 대상(유효할 때만). 바인딩됐는데 무효면 null — fallback 금지의 근거다.
    //
    // 무효 판정은 두 가지다: (1) native instance가 free됨, (2) 살아 있지만 사망 상태.
    // IsInstanceValid를 **먼저** 본다 — freed 객체에 IsAlive(ArticleStatus 접근)를 먼저 태우면 안 된다.
    protected ArticleBase BoundTarget =>
        _explicitTargetBound
        && GodotObject.IsInstanceValid(_explicitTarget)
        && _explicitTarget.IsAlive
            ? _explicitTarget
            : null;

    protected ArticleBase GetTarget(Node owner)
    {
        // 바인딩이 활성이면 상대 검색을 하지 않는다. 이 경로는 Tree 배선에 의존하지 않는다.
        if (_explicitTargetBound)
        {
            ArticleBase bound = BoundTarget;
            if (bound != null
                && owner is BehaviorTree_Action { Tree: not null } node
                && node.Tree.GetParent() is ArticleBase boundCaster)
            {
                boundCaster.DecisionFlipH(bound.TilePosition);
            }
            return bound;
        }

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