using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// 저장 가능한 택틱보드 초안(BT-003 Step 1, ADR-024 §1). 실행 상태(live Node/target, 선택 latch, 확정 대상,
// action queue, Blackboard/debug)를 담지 않는 순수 데이터 Resource다. 전투용 CompiledBoardSnapshot과는 다른
// 표현이며, 편집 Resource를 실행 중 다시 읽지 않는다.
//
// 행 배열 순서가 우선순위의 유일한 source of truth다(ADR-024 §2). SchemaVersion은 v1만 유효하지만 Step 1은
// 값을 보존만 하고 거부하지 않는다 — unknown/future 거부는 Step 2 validator 소관이다(ADR-024 §10).
[GlobalClass, Tool]
public partial class TacticBoardDefinition : Resource
{
    [Export] public int SchemaVersion { get; set; } = 1;
    [Export] public StringName BoardId { get; set; } = default;
    [Export] public string DisplayName { get; set; } = string.Empty;
    [Export] public Array<TacticRowDefinition> Rows { get; set; } = new();
}
