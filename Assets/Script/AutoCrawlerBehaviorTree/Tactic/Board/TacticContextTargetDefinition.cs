using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// `그 적`(문맥 대상). target-bearing 조건(예: enemy_casting)이 모은 후보를 소비한다. 후보 공급 조건이 하나도
// 없으면 Step 2 validator가 missing_target_context blocking Error를 낸다(ADR-024 §11).
[GlobalClass, Tool]
public partial class TacticContextTargetDefinition : TacticTargetDefinition
{
    public override StringName TypeId => "context_target";
}
