using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem;

[GlobalClass, Tool]
public partial class TurnAction_Skill : TurnActionBase
{
    [Export] public SkillDefinition Definition { get; set; }

    protected override int Range => Definition?.Range ?? 0;
    protected override int Scale => 1;

    public override bool CanStart(CharacterArticle caster)
    {
        if (caster == null || Definition == null || !Definition.IsValidV0()) return false;
        if (Definition.AmmoResetScope != SkillAmmoResetScope.Unlimited && !caster.SkillAmmoState.HasAmmo(Definition.Id)) return false;
        if (Definition.ManaCost > 0 && !CanAffordMana(caster, Definition.ManaCost)) return false;
        return true;
    }

    public override void Init(Node owner)
    {
        // 유효성은 런타임 Failure 게이트가 아니라 진입 전제조건이다(ADR-021 §3). caster 조회 실패/null/invalid
        // definition은 저작·구조 오류이므로 상태 없이 반환 → Action이 End를 낸다(SK-001 계약). caster 조회가
        // 실패하면 Failed state를 붙일 caster 자체가 없어 상태 기반 Failure도 불가능하다. ammo/마나/무대상만 Failure.
        if (!TryGetCaster(owner, out var caster) || Definition == null || !Definition.IsValidV0())
        {
            return;
        }

        var state = new SkillState(this, Definition);
        bool ammoLimited = Definition.AmmoResetScope != SkillAmmoResetScope.Unlimited;

        // 게이트 순서(ADR-021 §3): ammo -> 마나 지불 가능성 -> 대상 락온. 셋 중 하나라도 막히면 상태 변경/소모
        // 없이 fail-closed하고 Action이 ActionState.Failure로 되돌려 BT Selector 다음 행으로 넘긴다.

        // (1) ammo 게이트: 제한 스킬이 remaining 0이면 무소모 Failure.
        if (ammoLimited && !caster.SkillAmmoState.HasAmmo(Definition.Id))
        {
            state.Failed = true;
            state.Reports.Add($"ammo:{Definition.Id}:empty");
            caster.CurrentTurnActionState = state;
            return;
        }

        // (2) 마나 지불 가능성만 확인한다. 실제 소모는 대상 락온 뒤 커밋에서 한다.
        if (Definition.ManaCost > 0 && !CanAffordMana(caster, Definition.ManaCost))
        {
            state.Failed = true;
            caster.CurrentTurnActionState = state;
            return;
        }

        // (3) 대상 락온: 시전 시작 시점에 사거리 내 유효 대상이 하나도 없으면 무소모 Failure.
        //     (락온 이후 여러 턴 windup 중 대상이 이동/사망해 빗나가는 것은 의도된 전황이며 환불하지 않는다.)
        var context = SkillContext.FromBattleField(caster);
        ArticleBase target = SelectTarget(caster, context);
        if (target == null)
        {
            state.Failed = true;
            caster.CurrentTurnActionState = state;
            return;
        }
        state.ConfirmedTargets.Add(target);
        caster.DecisionFlipH(target.TilePosition);

        // (4) 커밋(시전 시작 = 대상 락온): ammo 1 + 마나를 함께 소모한다. 이후 빗나감/스턴 취소는 환불 없음.
        if (ammoLimited && caster.SkillAmmoState.Consume(Definition.Id))
        {
            caster.SkillAmmoState.TryGetAmmo(Definition.Id, out int remaining, out _);
            state.Reports.Add($"ammo:{Definition.Id}:consumed:{remaining}");
        }
        if (Definition.ManaCost > 0)
        {
            TrySpendMana(caster, Definition.ManaCost);
        }

        if (Definition.WindupCost > 0)
        {
            state.IsCasting = true;
            for (int i = 0; i < Definition.WindupCost; i++)
            {
                state.EnqueuePhase(CastingPhase);
            }
        }
        
        if (!string.IsNullOrWhiteSpace(Definition.AnimationName))
        {
            state.EnqueuePhase(StartPhase);
            state.EnqueuePhase(RunAnimationPhase);
        }
        
        foreach (var effect in Definition.Effects)
        {
            effect.EnqueuePhases(context, state);
        }

        caster.CurrentTurnActionState = state;
    }

    public override void Finish(Node owner)
    {
        if (TryGetCaster(owner, out var caster) && caster.CurrentTurnActionState is SkillState state && state.OwnerAction == this)
        {
            caster.CurrentTurnActionState = null;
        }
    }

    public override ActionState Action(double delta, ArticleBase owner)
    {
        if (owner is not CharacterArticle character
            || character.CurrentTurnActionState is not SkillState state
            || state.OwnerAction != this
            || state.Definition != Definition
            || state.Completed)
        {
            if (owner is CharacterArticle { CurrentTurnAction: var current } staleCharacter && current == this)
            {
                staleCharacter.CurrentTurnAction = null;
            }
            return ActionState.End;
        }

        // 지불/검증 실패 상태는 실행 없이 Failure로 닫고 Selector 다음 행으로 넘긴다.
        if (state.Failed)
        {
            return ClearCurrentAction(character, ActionState.Failure);
        }

        if (state.RemainingCost <= 0)
        {
            return ClearCurrentAction(character, ActionState.End);
        }

        if (state.PhaseQueue.Count == 0)
        {
            owner.AnimationPlayer.Play("Idle");
            state.MarkCostUsed();
            return ClearCurrentAction(character, ActionState.End);
        }

        ActionState status = state.PhaseQueue.Peek()(delta, owner);

        if (status != ActionState.Running && state.PhaseQueue.Count == 0)
        {
            owner.AnimationPlayer.Play("Idle");
            state.MarkCostUsed();
            return ClearCurrentAction(character, ActionState.End);
        }

        if (state.Completed)
        {
            return ClearCurrentAction(character, ActionState.End);
        }

        return status;
    }

    private ActionState ClearCurrentAction(CharacterArticle character, ActionState result)
    {
        character.CurrentTurnActionState = null;
        if (character.CurrentTurnAction == this)
        {
            character.CurrentTurnAction = null;
        }
        return result;
    }

    private ActionState CastingPhase(double delta, ArticleBase owner)
    {
        if (owner is not CharacterArticle { CurrentTurnActionState: SkillState state })
        {
            return ActionState.Executed;
        }

        string castAnim = state.UsedCost == 0 ? "Cast" : "Casting";
        if (owner.AnimationPlayer.CurrentAnimation == castAnim)
        {
            if (owner.AnimationPlayer.CurrentAnimationPosition < owner.AnimationPlayer.CurrentAnimationLength)
            {
                owner.AnimationPlayer.Seek(owner.AnimationPlayer.CurrentAnimationPosition + delta);
                if (owner.AnimationPlayer.CurrentAnimationPosition + delta < owner.AnimationPlayer.CurrentAnimationLength)
                {
                    return ActionState.Running;
                }
            }
            state.PhaseQueue.Dequeue();
            state.MarkCostUsed();
            if (state.UsedCost >= state.Definition.WindupCost)
            {
                state.IsCasting = false;
            }
            return ActionState.Executed;
        }

        owner.AnimationPlayer.Play(castAnim, -1, 0);
        return ActionState.Running;
    }

    private ActionState StartPhase(double delta, ArticleBase owner)
    {
        string animation = Definition?.AnimationName;
        if (!string.IsNullOrWhiteSpace(animation))
        {
            owner.AnimationPlayer.Play(animation, -1, 0);
        }

        if (owner is CharacterArticle { CurrentTurnActionState: SkillState state2 })
        {
            state2.PhaseQueue.Dequeue();
        }
        return ActionState.Running;
    }

    private ActionState RunAnimationPhase(double delta, ArticleBase owner)
    {
        string animation = Definition?.AnimationName;
        if (!string.IsNullOrWhiteSpace(animation)
            && owner.AnimationPlayer.CurrentAnimation == animation
            && owner.AnimationPlayer.CurrentAnimationPosition < owner.AnimationPlayer.CurrentAnimationLength)
        {
            owner.AnimationPlayer.Seek(owner.AnimationPlayer.CurrentAnimationPosition + delta);
            return ActionState.Running;
        }

        if (owner is CharacterArticle { CurrentTurnActionState: SkillState state })
        {
            state.PhaseQueue.Dequeue();
        }
        return ActionState.Running;
    }

    private ArticleBase SelectTarget(ArticleBase caster, SkillContext context)
    {
        // Self는 side/selector 어느 쪽으로 지정돼도 사거리/필터를 우회해 시전자를 확정한다.
        if (Definition.TargetSide == SkillTargetSide.Self
            || Definition.TargetSelectorDefault == SkillTargetSelector.Self)
        {
            return caster;
        }

        List<Vector2I> calculatedAttackRange = AttackRangePositions.Select(p => p + caster.TilePosition).ToList();

        var targets = context.TileMapLayer?.GetArticles(calculatedAttackRange)?
            .Where(t => t is { IsAlive: true });

        if (targets == null) return null;

        if (Definition.TargetSide == SkillTargetSide.Enemy)
        {
            targets = targets.Where(t => t.IsOpponent(caster));
        }
        else if (Definition.TargetSide == SkillTargetSide.Ally)
        {
            targets = targets.Where(t => !t.IsOpponent(caster) && t != caster);
        }

        // 동점자는 ADR-017 정규 순서(Manhattan 거리 -> Y -> X)로 고정한다. ThenByCanonical을 공유해 유클리드 편차를 막는다.
        return Definition.TargetSelectorDefault switch
        {
            SkillTargetSelector.LowestHp => targets
                .OrderBy(t => CurrentHealthOf(t) ?? int.MaxValue)
                .ThenByCanonical(t => t.TilePosition, caster.TilePosition)
                .FirstOrDefault(),
            SkillTargetSelector.HighestHp => targets
                .OrderByDescending(t => CurrentHealthOf(t) ?? -1)
                .ThenByCanonical(t => t.TilePosition, caster.TilePosition)
                .FirstOrDefault(),
            _ => targets
                .OrderByCanonical(t => t.TilePosition, caster.TilePosition)
                .FirstOrDefault(),
        };
    }

    private static int? CurrentHealthOf(ArticleBase article)
    {
        return (article.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Health)) as Health)?.CurrentHealth;
    }

    // 지불 가능성만 확인한다(소모 안 함). 마나 원소가 없는 유닛은 지불 불가로 fail-closed(ADR-018 follow-up 유지).
    private static bool CanAffordMana(ArticleBase caster, int cost)
    {
        if (caster.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Mana)) is not Mana mana)
        {
            return false;
        }

        return mana.CanAfford(cost);
    }

    private static bool TrySpendMana(ArticleBase caster, int cost)
    {
        if (caster.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Mana)) is not Mana mana)
        {
            return false;
        }

        return mana.TrySpend(cost);
    }

    private static bool TryGetCaster(Node owner, out CharacterArticle caster)
    {
        caster = null;
        if (owner is BehaviorTree_Action action && action.Tree?.GetParent() is CharacterArticle treeCaster)
        {
            caster = treeCaster;
            return true;
        }

        return false;
    }
}