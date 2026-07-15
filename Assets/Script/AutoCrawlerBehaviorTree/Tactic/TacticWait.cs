using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 최종 하한 대기(BT-002 Step 3, 기획 H-3 최하단 기본 행, R8). 어떤 공격·접근도 불가능할 때 턴을 확실하게
// 소비한다. 항상 성립하며 상태를 바꾸지 않는다 — 살아 있는 상대가 있어도 무한 `Failure`가 되지 않도록 하는
// 보드의 안전망이다.
[GlobalClass, Tool]
public partial class TacticWait : BehaviorTree_Action, ITacticNode
{
    public TacticResult LastResult { get; private set; } = TacticResult.Ineligible;

    public void ResetForNewTurn() => LastResult = TacticResult.Ineligible;

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        LastResult = TacticResult.ActionCompleted;   // 대기는 정상적인 턴 소비다.
        return BtStatus.Success;
    }
}
