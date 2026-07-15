using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// `적이 영창 중` — 영창 중인 상대만 문맥 후보로 공급한다(영창 방해 행의 후보 소스, H-3-2).
[GlobalClass, Tool]
public partial class TacticEnemyCastingConditionDefinition : TacticConditionDefinition
{
    public override StringName TypeId => "enemy_casting";
}
