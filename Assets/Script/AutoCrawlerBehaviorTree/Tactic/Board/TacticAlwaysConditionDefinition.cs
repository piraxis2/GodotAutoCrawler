using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// `항상` — targetless gate. 후보를 만들지 않는다(런타임 TacticConditionAlways와 대응).
[GlobalClass, Tool]
public partial class TacticAlwaysConditionDefinition : TacticConditionDefinition
{
    public override StringName TypeId => "always";
}
