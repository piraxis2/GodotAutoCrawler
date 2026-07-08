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

    public override void Init(Node owner)
    {
        if (!TryGetCaster(owner, out var caster) || Definition == null || !Definition.IsValidV0())
        {
            return;
        }

        var state = new SkillState(this, Definition);

        // 지불 게이트: 마나가 부족하면 상태 변경 없이 fail-closed. Action이 ActionState.Failure로 되돌린다.
        if (Definition.ManaCost > 0 && !TrySpendMana(caster, Definition.ManaCost))
        {
            state.Failed = true;
            caster.CurrentTurnActionState = state;
            return;
        }

        var context = SkillContext.FromBattleField(caster);
        ArticleBase target = SelectTarget(caster, context);
        if (target != null)
        {
            state.ConfirmedTargets.Add(target);
            caster.DecisionFlipH(target.TilePosition);
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
            return ActionState.End;
        }

        // 지불/검증 실패 상태는 실행 없이 Failure로 닫고 Selector 다음 행으로 넘긴다.
        if (state.Failed)
        {
            character.CurrentTurnActionState = null;
            return ActionState.Failure;
        }

        if (state.RemainingCost <= 0)
        {
            character.CurrentTurnActionState = null;
            return ActionState.End;
        }

        if (state.PhaseQueue.Count == 0)
        {
            owner.AnimationPlayer.Play("Idle");
            state.MarkCostUsed();
            character.CurrentTurnActionState = null;
            return ActionState.End;
        }

        ActionState status = state.PhaseQueue.Peek()(delta, owner);

        if (status != ActionState.Running && state.PhaseQueue.Count == 0)
        {
            owner.AnimationPlayer.Play("Idle");
            state.MarkCostUsed();
            character.CurrentTurnActionState = null;
            return ActionState.End;
        }

        if (state.Completed)
        {
            character.CurrentTurnActionState = null;
            return ActionState.End;
        }

        return status;
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