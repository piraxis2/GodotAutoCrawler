using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem.Blocks;

[GlobalClass, Tool]
public partial class BindBlock : EffectBlock
{
    [Export] public int Turns { get; set; } = 1;

    public override void EnqueuePhases(SkillContext context, SkillState state)
    {
        state.EnqueuePhase((delta, owner) =>
        {
            foreach (var target in state.ConfirmedTargets)
            {
                if (target is not CharacterArticle { IsAlive: true } character) continue;

                character.StatusController.ApplyBind(Turns);
                state.Reports.Add($"bind:{Turns}:{target.GetPath()}");
            }

            state.PhaseQueue.Dequeue();
            return ActionState.Executed;
        });
    }
}
