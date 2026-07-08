using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem.Blocks;

[GlobalClass, Tool]
public partial class StunBlock : EffectBlock
{
    [Export] public int Turns { get; set; } = 1;
    // 0~100. 100은 항상 발동이며 roll을 하지 않아 기존 baseline RNG 스트림을 보존한다.
    [Export] public int Chance { get; set; } = 100;

    public override void EnqueuePhases(SkillContext context, SkillState state)
    {
        state.EnqueuePhase((delta, owner) =>
        {
            foreach (var target in state.ConfirmedTargets)
            {
                if (target is not CharacterArticle { IsAlive: true } character) continue;

                // 발동 판정. Chance < 100일 때만 SkillContext(단일 CombatRng)를 1회 소비한다.
                if (Chance < 100 && context.CombatRandRange(0.0, 100.0) >= Chance)
                {
                    state.Reports.Add($"stun_miss:{target.GetPath()}");
                    continue;
                }

                // windup 중이면 영창을 취소한다(이미 지불한 비용/마나는 되돌리지 않음).
                if (character.IsCasting)
                {
                    character.CancelCasting();
                    state.Reports.Add($"cast_cancel:{target.GetPath()}");
                }

                character.StatusController.ApplyStun(Turns);
                state.Reports.Add($"stun:{Turns}:{target.GetPath()}");
            }

            state.PhaseQueue.Dequeue();
            return ActionState.Executed;
        });
    }
}
