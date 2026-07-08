using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem.Blocks;

[GlobalClass, Tool]
public partial class ManaDrainBlock : EffectBlock
{
    [Export] public int Amount { get; set; } = 3;
    // 흡수한 만큼 시전자 마나를 회복한다.
    [Export] public bool RestoreToCaster { get; set; } = true;

    public override void EnqueuePhases(SkillContext context, SkillState state)
    {
        state.EnqueuePhase((delta, owner) =>
        {
            foreach (var target in state.ConfirmedTargets)
            {
                if (target is not { IsAlive: true }) continue;
                if (target.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Mana)) is not Mana targetMana) continue;

                int drained = Mathf.Min(Amount, targetMana.CurrentMana);
                if (drained <= 0) continue;

                targetMana.CurrentMana -= drained;
                state.Reports.Add($"manadrain:{drained}:{target.GetPath()}");

                if (RestoreToCaster
                    && context.Caster?.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Mana)) is Mana casterMana)
                {
                    casterMana.CurrentMana += drained;
                }
            }

            state.PhaseQueue.Dequeue();
            return ActionState.Executed;
        });
    }
}
