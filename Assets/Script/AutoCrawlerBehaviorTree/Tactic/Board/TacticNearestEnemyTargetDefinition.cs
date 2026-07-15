using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// `가장 가까운 적`. compiler가 반드시 TacticConditionAnyEnemy 후보 공급 노드를 먼저 생성한 뒤 셀렉터를
// 생성한다 — 셀렉터 단독으로는 후보가 없어 항상 불성립하기 때문이다(ADR-024 §11, 실측 TacticTargetSelector).
[GlobalClass, Tool]
public partial class TacticNearestEnemyTargetDefinition : TacticTargetDefinition
{
    public override StringName TypeId => "nearest_enemy";
}
