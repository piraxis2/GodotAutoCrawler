using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Article.Status;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public partial class CharacterArticle : ArticleBase, ITurnAffectedArticle<ArticleBase>
{
    public TurnActionBase CurrentTurnAction { get; set; }
    public ITurnActionState CurrentTurnActionState { get; set; }
    public StatusController StatusController { get; } = new();
    // 유닛별 스킬 ammo remaining. StatusController와 동형의 순수 C# 상태로, .tres에 직렬화되지 않는다. ADR-021.
    public SkillAmmoState SkillAmmoState { get; } = new();

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

    private IReadOnlyList<BehaviorTree_TurnAction> UsableTurnActions => BehaviorTree
        .FindNodeByType(typeof(BehaviorTree_TurnAction))?
        .OfType<BehaviorTree_TurnAction>()
        .Where(action => action.TurnAction?.CanStart(this) == true)
        .ToList() ?? [];

    public bool HasUsableAttack => UsableTurnActions.Count > 0;

    private IReadOnlyList<Vector2I> AttackRangePositions => UsableTurnActions
        .Select(action => action.TurnAction.AttackRangePositions)
        .OrderByDescending(positions => positions.Count)
        .FirstOrDefault() ?? [];

    public List<Vector2I> CalculatedAttackRange => AttackRangePositions.Select(p => p + TilePosition).ToList();

    // 전투 시작 reset(ADR-021 §4): BT의 TurnAction_Skill 정의를 순회해 제한 스킬 ammo를 Ammo로 충전한다.
    // Unlimited은 Charge가 무시(무제한). Battle/Expedition(예약)은 이월 없이 매 전투 full charge → SkillAmmoState가
    // 매번 max로 재설정된다. remaining은 SkillAmmoState에만 살고 SkillDefinition Resource에는 쓰지 않는다.
    // BT 순회는 raw 노드 워크를 쓴다: BehaviorTree.Root(=GetChild(0)) 하위만 보는 FindNodeByType과 달리, Root 밖에
    // 배선된 스킬 행이나 battle-start 시점의 TreeChildren 미갱신에도 영향받지 않는다.
    public void ChargeBattleAmmo()
    {
        var bt = GetNodeOrNull<BehaviorTree>("BehaviorTree");
        if (bt == null) return;

        var actions = new List<BehaviorTree_TurnAction>();
        CollectTurnActionNodes(bt, actions);
        foreach (var node in actions)
        {
            if (node.TurnAction is TurnAction_Skill { Definition: { } def } && def.IsValidV0())
            {
                SkillAmmoState.Charge(def.Id, def.Ammo, def.AmmoResetScope);
            }
        }
    }

    private static void CollectTurnActionNodes(Node node, List<BehaviorTree_TurnAction> acc)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is BehaviorTree_TurnAction action) acc.Add(action);
            CollectTurnActionNodes(child, acc);
        }
    }

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
