using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// runtime BehaviorTree와 독립적으로 살아남는 action template source(ADR-024 §12). CharacterArticle 또는 미래
// PartySnapshot이 소유하며, compiler가 교체할 BehaviorTree root의 자식이 아니다. apply가 root를 교체·free해도
// catalog는 유지되므로, root 교체 뒤 재컴파일과 A→B→A 전환이 catalog만으로 성립한다.
//
// resolver(Step 3)는 여기서 ActionId를 조회해 매 compile마다 격리된 새 TurnActionBase 인스턴스를 만든다 —
// TurnActionBase는 _usedCost/ActionQueue/explicit target 같은 mutable 상태를 가진 Resource라 인스턴스를
// 공유하면 유닛 간 오염이 생기기 때문이다(ADR-024 §3).
[GlobalClass, Tool]
public partial class TacticActionCatalog : Resource
{
    // 안정 revision. instance id/resource path가 아니라 선언 값이다(ADR-024 §7). catalog가 바뀌면 이 값을 올려
    // applied CompileInputSignature(= board SourceSignature ⊕ CatalogRevision)와 불일치시켜 재컴파일이
    // 필요해진다(ADR-024 §9). 명시적 재적용 전에는 stale snapshot을 최신 적용본으로 표시하지 않는다.
    [Export] public StringName CatalogRevision { get; set; } = default;

    [Export] public Array<TacticActionCatalogEntry> Entries { get; set; } = new();
}
