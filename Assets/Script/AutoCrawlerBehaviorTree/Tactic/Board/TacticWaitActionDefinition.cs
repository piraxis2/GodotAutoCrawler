using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// `대기` — 최종 하한 행(terminal_wait)의 행동. 무대상이며 항상 턴을 소비한다(런타임 TacticWait와 대응).
[GlobalClass, Tool]
public partial class TacticWaitActionDefinition : TacticActionDefinition
{
    public override StringName TypeId => "wait";
}
