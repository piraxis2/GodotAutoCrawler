using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem.Blocks;

[GlobalClass, Tool]
public partial class DamageBlock : EffectBlock
{
    [Export] public int MinDamage { get; set; } = 10;
    [Export] public int MaxDamage { get; set; } = 20;
    [Export] public SkillDamageType DamageType { get; set; } = SkillDamageType.Physical;
    // 0~100. 100은 항상 명중이며 hit roll을 하지 않아 기존 baseline RNG 스트림을 보존한다.
    [Export] public int HitChance { get; set; } = 100;

    public override void EnqueuePhases(SkillContext context, SkillState state)
    {
        state.EnqueuePhase((delta, owner) =>
        {
            foreach (var target in state.ConfirmedTargets)
            {
                if (target is not { IsAlive: true }) continue;

                // 명중 판정. hit_chance < 100일 때만 SkillContext(단일 CombatRng)를 1회 소비한다.
                // RNG 순서는 명중 -> (명중 시) 크리티컬 -> 피해 롤로 고정된다.
                if (HitChance < 100 && context.CombatRandRange(0.0, 100.0) >= HitChance)
                {
                    state.Reports.Add($"miss:{DamageTag()}:{target.GetPath()}");
                    continue;
                }

                ApplyDamage(owner, target);
                state.Reports.Add($"damage:{DamageTag()}:{target.GetPath()}");
            }

            state.PhaseQueue.Dequeue();
            return ActionState.Executed;
        });
    }

    // 피해 적용 seam. Step 3 명중 판정만 SkillContext CombatRng로 옮겼고, 피해 금액 산출 + Floater/Hit UI는
    // 여전히 legacy Damage.CreateDamage<T>()/ApplyImmediately에 묶여 있다. 이 호출부가 UI 결합 제거 및
    // headless 피해 result API로 넘길 migration 경계다(ADR-018 / SK-001 Step 3 참고).
    private void ApplyDamage(ArticleBase owner, ArticleBase target)
    {
        switch (DamageType)
        {
            case SkillDamageType.Physical:
                target.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<PhysicalDamage>(owner.ArticleStatus, MinDamage, MaxDamage));
                break;
            // legacy MagicBolt parity: magical damage ignores MinDamage and uses fixed MaxDamage.
            case SkillDamageType.Magical:
                target.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<MagicalDamage>(owner.ArticleStatus, MaxDamage, MaxDamage));
                break;
        }
    }

    private string DamageTag() => DamageType == SkillDamageType.Magical ? "magical" : "physical";
}