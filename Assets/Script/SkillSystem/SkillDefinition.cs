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
    [Export] public Array<EffectBlock> Effects { get; set; } = new();

    public int MasterCost => Mathf.Max(ActCost + WindupCost, 1);

    public bool IsValidV0()
    {
        if (Id == default || string.IsNullOrWhiteSpace(Id.ToString())) return false;
        if (Range < 0) return false;
        if (ManaCost < 0) return false;
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