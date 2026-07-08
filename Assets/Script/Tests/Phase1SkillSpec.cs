#if TOOLS
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.SkillSystem.Blocks;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

// 07_수치표_Phase1.md의 12종 스킬 스펙(정본). 부트스트랩 생성기(Sk001Phase1DataGenerator)와
// Step6 검증 테스트(Sk001Step6DataPackTest)가 공유한다. 커밋된 .tres는 이 스펙과 일치해야 한다.
public static class Phase1SkillSpec
{
    public const string DataDir = "res://Assets/SkillData/Phase1";

    public static System.Collections.Generic.Dictionary<string, SkillDefinition> Build()
    {
        SkillDefinition Def(string id, string name, int range, int mana, int windup, string anim,
            SkillTargetSide side, params EffectBlock[] effects)
        {
            var arr = new Array<EffectBlock>();
            foreach (var e in effects) arr.Add(e);
            return new SkillDefinition
            {
                Id = new StringName(id), DisplayName = name, Range = range, ActCost = 1, WindupCost = windup,
                ManaCost = mana, AnimationName = anim, TargetSide = side,
                TargetSelectorDefault = SkillTargetSelector.Nearest, Effects = arr
            };
        }
        DamageBlock Dmg(int min, int max, SkillDamageType type, int hit) =>
            new() { MinDamage = min, MaxDamage = max, DamageType = type, HitChance = hit };

        return new System.Collections.Generic.Dictionary<string, SkillDefinition>
        {
            // 검
            ["sword_slash"] = Def("sword_slash", "베기", 1, 0, 0, "Attack", SkillTargetSide.Enemy,
                Dmg(10, 20, SkillDamageType.Physical, 95)),
            ["sword_smash"] = Def("sword_smash", "강타", 1, 15, 0, "Attack", SkillTargetSide.Enemy,
                Dmg(15, 25, SkillDamageType.Physical, 90), new KnockbackBlock { Distance = 1 }, new StunBlock { Turns = 1, Chance = 20 }),
            ["sword_ironbody"] = Def("sword_ironbody", "금강체", 0, 25, 0, "", SkillTargetSide.Self,
                new SelfBuffBlock { Kind = SelfBuffKind.DamageTaken, DamageMultiplier = 0.5f, Turns = 3 }),
            // 격투
            ["fist_jab"] = Def("fist_jab", "잽", 1, 0, 0, "Attack", SkillTargetSide.Enemy,
                Dmg(10, 16, SkillDamageType.Physical, 100)),
            ["fist_straight"] = Def("fist_straight", "스트레이트", 1, 10, 0, "Attack", SkillTargetSide.Enemy,
                Dmg(18, 28, SkillDamageType.Physical, 90)),
            ["fist_sonicblow"] = Def("fist_sonicblow", "소닉 블로", 1, 20, 1, "Attack", SkillTargetSide.Enemy,
                Dmg(30, 45, SkillDamageType.Physical, 85), new StunBlock { Turns = 1, Chance = 35 }),
            // 활
            ["bow_shot"] = Def("bow_shot", "사격", 5, 0, 0, "Attack", SkillTargetSide.Enemy,
                Dmg(10, 18, SkillDamageType.Physical, 90)),
            ["bow_aimedshot"] = Def("bow_aimedshot", "조준 사격", 5, 15, 1, "Attack", SkillTargetSide.Enemy,
                Dmg(25, 40, SkillDamageType.Physical, 100)),
            ["bow_bindingarrow"] = Def("bow_bindingarrow", "속박 화살", 5, 20, 0, "Attack", SkillTargetSide.Enemy,
                Dmg(5, 10, SkillDamageType.Physical, 85), new BindBlock { Turns = 3 }),
            // 지팡이
            ["staff_magicbolt"] = Def("staff_magicbolt", "마탄", 3, 5, 0, "", SkillTargetSide.Enemy,
                Dmg(15, 15, SkillDamageType.Magical, 95)),
            ["staff_chainlightning"] = Def("staff_chainlightning", "연쇄 뇌격", 3, 30, 1, "", SkillTargetSide.Enemy,
                new ChainBlock { ChainCount = 3, Range = 3, MaxDamage = 20 }),
            ["staff_manaabsorb"] = Def("staff_manaabsorb", "마나 흡수", 3, 0, 0, "", SkillTargetSide.Enemy,
                Dmg(8, 15, SkillDamageType.Magical, 90), new ManaDrainBlock { Amount = 16, RestoreToCaster = true }),
        };
    }

    // 전체 필드 + 이펙트 타입/파라미터를 담은 서명. 커밋 .tres 값 드리프트를 잡는 데 쓴다.
    public static string Signature(SkillDefinition d)
    {
        string Eff(EffectBlock e) => e switch
        {
            DamageBlock b => $"Damage({b.MinDamage},{b.MaxDamage},{(int)b.DamageType},{b.HitChance})",
            KnockbackBlock b => $"Knockback({b.Distance})",
            StunBlock b => $"Stun({b.Turns},{b.Chance})",
            BindBlock b => $"Bind({b.Turns})",
            SelfBuffBlock b => $"SelfBuff({b.DamageMultiplier},{b.Turns},{(int)b.Kind})",
            ManaDrainBlock b => $"ManaDrain({b.Amount},{b.RestoreToCaster})",
            ChainBlock b => $"Chain({b.ChainCount},{b.Range},{b.MaxDamage})",
            _ => $"?({e?.GetType().Name})"
        };
        var effs = new System.Collections.Generic.List<string>();
        foreach (var e in d.Effects) effs.Add(Eff(e));
        return $"{d.Id}|{d.DisplayName}|R{d.Range}|A{d.ActCost}|W{d.WindupCost}|M{d.ManaCost}"
               + $"|anim={d.AnimationName}|side={(int)d.TargetSide}|sel={(int)d.TargetSelectorDefault}"
               + $"|[{string.Join(";", effs)}]";
    }
}
#endif
