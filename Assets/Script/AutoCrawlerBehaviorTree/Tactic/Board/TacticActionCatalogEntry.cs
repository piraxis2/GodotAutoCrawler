using Godot;
using AutoCrawler.Assets.Script.TurnAction;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// catalog 한 항목(ADR-024 §12). 안정 ActionId와 공유 template를 잇는다. Template은 직접 실행하지 않는 immutable
// 원본으로 취급하고, resolver가 복제해 유닛별 인스턴스를 만든다(Step 3). Step 1은 저장/재로드 왕복만 보장한다.
[GlobalClass, Tool]
public partial class TacticActionCatalogEntry : Resource
{
    [Export] public StringName ActionId { get; set; } = default;
    [Export] public TurnActionBase Template { get; set; }
}
