using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// 대상 definition의 typed base(ADR-024 §2/§11). 안정 TypeId를 가지며 "후보를 어떻게 공급받는가"를 선언한다.
// null Target(targetless)은 wait 같은 무대상 행동에 쓴다. concrete 대상은 파일명=클래스명 규칙에 따라 별도 파일.
[GlobalClass, Tool]
public abstract partial class TacticTargetDefinition : Resource
{
    public abstract StringName TypeId { get; }
}
