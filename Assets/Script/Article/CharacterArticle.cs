using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Article.Status;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public partial class CharacterArticle : ArticleBase, ITurnAffectedArticle<ArticleBase>
{
    public TurnActionBase CurrentTurnAction { get; set; }
    public ITurnActionState CurrentTurnActionState { get; set; }
    public StatusController StatusController { get; } = new();

    // 영창 중인지 외부(HUD/조건 어휘/영창 취소)에서 SkillState 구체 타입 없이 조회하는 창구.
    public bool IsCasting => CurrentTurnActionState?.IsCasting ?? false;

    public override float DamageDealtMultiplier => StatusController.DamageDealtMultiplier;
    public override float DamageTakenMultiplier => StatusController.DamageTakenMultiplier;

    // 영창/현재 액션 폐기. 스턴 취소가 소비하며 이미 지불한 비용/마나는 되돌리지 않는다.
    public void CancelCasting()
    {
        CurrentTurnAction = null;
        CurrentTurnActionState = null;
    }
    private BehaviorTree _behaviorTree;
    public BehaviorTree BehaviorTree => _behaviorTree ??= GetNode<BehaviorTree>("BehaviorTree");
    public int Priority { get; set; }

    private IReadOnlyList<Vector2I> _attackRangePositions;
    private IReadOnlyList<Vector2I> AttackRangePositions
    {
        get
        {
            if (_attackRangePositions != null) return _attackRangePositions;
            _attackRangePositions = BehaviorTree.FindNodeByType(typeof(BehaviorTree_TurnAction))
                .OfType<BehaviorTree_TurnAction>()
                .Select(action => action.TurnAction.AttackRangePositions)
                .OrderByDescending(positions => positions.Count)
                .FirstOrDefault();
            return _attackRangePositions;
        }
    }

    public List<Vector2I> CalculatedAttackRange => AttackRangePositions.Select(p => p + TilePosition).ToList();

    public void ApplyTurnStartEffects()
    {
        StatusController.OnTurnStart();
        ArticleStatus.ApplyTurnStartStatusElements();
        if (ArticleStatus.HasLivingHealth()) ArticleStatus.ApplyAffectingStatuses();
    }

    public BtStatus TurnPlay(double delta)
    {
        if (BehaviorTree == null) throw new NullReferenceException("BehaviorTree is null");

        // 스턴 턴은 영창을 이어가지 않고 즉시 턴을 넘긴다(턴당 1회 소비).
        if (StatusController.ConsumeStunForTurn())
        {
            CancelCasting();
            return BtStatus.Success;
        }

        if (CurrentTurnAction == null) return BehaviorTree.Behave(delta, this);
        
        // 현재 턴 액션이 null이 아닐 경우, 액션을 실행
        ActionState actionState = CurrentTurnAction.Action(delta, this);

        if (actionState is ActionState.End or ActionState.Failure) CurrentTurnAction = null;

        // Running: 실행 중, Failure: 지불/검증 실패(다음 행 fallback), 그 외: 완료(Success)
        return actionState switch
        {
            ActionState.Running => BtStatus.Running,
            ActionState.Failure => BtStatus.Failure,
            _ => BtStatus.Success
        };

    }

}
