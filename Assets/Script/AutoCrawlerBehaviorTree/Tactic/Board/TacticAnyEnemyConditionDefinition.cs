using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// `적이 있음` — 살아 있는 상대 전체를 문맥 후보로 공급한다(NearestEnemy 대상의 후보 소스, ADR-024 §11).
[GlobalClass, Tool]
public partial class TacticAnyEnemyConditionDefinition : TacticConditionDefinition
{
    public override StringName TypeId => "any_enemy";
}
