using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// catalog의 ActionId를 참조하는 행동(ADR-024 §12). live TurnActionBase가 아니라 안정 ID만 저장한다. resolver가
// TacticActionCatalog에서 이 ActionId를 조회해 유닛별 격리된 TurnActionBase 인스턴스를 만든다(Step 3).
// v1 기본 공격의 안정 ID는 "default_attack".
[GlobalClass, Tool]
public partial class TacticCatalogActionDefinition : TacticActionDefinition
{
    public override StringName TypeId => "catalog_action";

    [Export] public StringName ActionId { get; set; } = default;
}
