using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.SkillSystem;

[GlobalClass, Tool]
public partial class SkillDefinition : Resource
{
    [Export] public StringName Id { get; set; } = default;
    [Export] public string DisplayName { get; set; } = string.Empty;
    [Export] public int Range { get; set; } = 1;
    [Export] public int ActCost { get; set; } = 1;
    [Export] public int WindupCost { get; set; } = 0;
    [Export] public int ManaCost { get; set; } = 0;
    [Export] public string AnimationName { get; set; } = "Attack";
    [Export] public SkillTargetSide TargetSide { get; set; } = SkillTargetSide.Enemy;
    [Export] public SkillTargetSelector TargetSelectorDefault { get; set; } = SkillTargetSelector.Nearest;
    // ammo(고정 보장 사용 횟수) 정책. remaining은 여기 저장하지 않고 유닛별 SkillAmmoState가 가진다. ADR-021.
    // 기본값 Unlimited + Ammo 0은 기존 .tres를 무제한으로 로드해 SK-001 baseline을 보존한다.
    [Export] public SkillAmmoResetScope AmmoResetScope { get; set; } = SkillAmmoResetScope.Unlimited;
    [Export] public int Ammo { get; set; } = 0;
    [Export] public Array<EffectBlock> Effects { get; set; } = new();

    public int MasterCost => Mathf.Max(ActCost + WindupCost, 1);

    public bool IsValidV0()
    {
        if (Id == default || string.IsNullOrWhiteSpace(Id.ToString())) return false;
        if (Range < 0) return false;
        if (ManaCost < 0) return false;
        if (Ammo < 0) return false;
        if (!System.Enum.IsDefined(typeof(SkillAmmoResetScope), AmmoResetScope)) return false;
        if (!System.Enum.IsDefined(typeof(SkillTargetSide), TargetSide)) return false;
        if (!System.Enum.IsDefined(typeof(SkillTargetSelector), TargetSelectorDefault)) return false;
        if (Effects == null) return false;
        foreach (var effect in Effects)
        {
            if (effect == null) return false;
        }

        return true;
    }
}