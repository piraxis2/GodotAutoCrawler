using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem.Blocks;

// 시전자(context.Caster)에게 피해 배율 버프를 건다. ConfirmedTargets가 아니라 시전자에 적용된다.
// Kind로 주는 피해(공격) 또는 받는 피해(방어, 예: 금강체 0.5) 배율을 선택한다.
[GlobalClass, Tool]
public partial class SelfBuffBlock : EffectBlock
{
    [Export] public float DamageMultiplier { get; set; } = 1.5f;
    [Export] public int Turns { get; set; } = 2;
    [Export] public SelfBuffKind Kind { get; set; } = SelfBuffKind.DamageDealt;

    public override void EnqueuePhases(SkillContext context, SkillState state)
    {
        state.EnqueuePhase((delta, owner) =>
        {
            if (context.Caster is CharacterArticle { IsAlive: true } caster)
            {
                if (Kind == SelfBuffKind.DamageTaken)
                    caster.StatusController.ApplyDamageTakenDebuff(DamageMultiplier, Turns);
                else
                    caster.StatusController.ApplyDamageDealtBuff(DamageMultiplier, Turns);

                state.Reports.Add($"selfbuff:{Kind}:{DamageMultiplier}x{Turns}:{caster.GetPath()}");
            }

            state.PhaseQueue.Dequeue();
            return ActionState.Executed;
        });
    }
}
