using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// 행동 definition의 typed base(ADR-024 §2). 안정 TypeId를 가진다. 스킬/공격 행동은 live TurnActionBase를
// 직접 저장하지 않고 catalog의 안정 ActionId만 저장한다(ADR-024 §2/§12) — resolver가 유닛별 새 인스턴스를
// 만든다(Step 3). concrete 행동은 파일명=클래스명 규칙에 따라 별도 파일.
[GlobalClass, Tool]
public abstract partial class TacticActionDefinition : Resource
{
    public abstract StringName TypeId { get; }
}
