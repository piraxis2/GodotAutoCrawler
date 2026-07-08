using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem;

[GlobalClass, Tool]
public abstract partial class EffectBlock : Resource
{
    public abstract void EnqueuePhases(SkillContext context, SkillState state);
}