using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 행의 행동 단계를 순서대로 실행하는 시퀀스(BT-002 Step 3). 보통 [TacticApproach, TacticExecuteAction]이며
// 행의 단일 행동(마지막 자식)으로 배치된다. 접근이 사거리 진입을 마치면 같은 턴에 행동으로 이어진다.
//
// 결과 전파(R6):
//  - 자식이 Running이면 그 단계에 고정하고 Running.
//  - 접근이 `ApproachConsumed`(이번 턴 접근으로 소비)면 이후 행동을 실행하지 않고 턴을 종료한다.
//  - 자식이 `Ineligible`이면, 이미 commit(이동/지불)이 있었으면 `CancelledAfterCommit`(fallback 금지),
//    없었으면 `Ineligible`(다음 행 fallback)로 승격/유지한다.
//  - 모든 단계가 완료되면 `ActionCompleted`.
[GlobalClass, Tool]
public partial class TacticActionSequence : BehaviorTree_Composite, ITacticNode, ITacticContextBound
{
    private int _phase;
    private bool _committed;

    public TacticResult LastResult { get; private set; } = TacticResult.Ineligible;

    protected override void OnInit(Node owner) => ResetForNewTurn();

    public void BindContext(TacticRowContext context)
    {
        foreach (var child in TreeChildren)
        {
            if (child is ITacticContextBound bound) bound.BindContext(context);
        }
    }

    public void ResetForNewTurn()
    {
        _phase = 0;
        _committed = false;
        LastResult = TacticResult.Ineligible;
        foreach (var child in TreeChildren)
        {
            if (child is ITacticNode tacticChild) tacticChild.ResetForNewTurn();
        }
    }

    protected override BtStatus OnBehave(double delta, Node owner)
    {
        var children = TreeChildren;
        if (children.Count == 0)
        {
            LastResult = TacticResult.Ineligible;
            return BtStatus.Failure;
        }

        // 모든 단계는 commit 구분을 보고하는 택틱 노드여야 한다. 일반 BT 노드는 상태를 바꾸고 Failure를 반환해도
        // 다음 단계가 계속 실행돼 commit 계약을 우회할 수 있으므로, 실행 전에 fail-closed한다(구조 오류는 Step 4
        // validation이 표면화).
        foreach (var child in children)
        {
            if (child is not ITacticNode)
            {
                GD.PushError($"TacticActionSequence '{Name}': 자식 '{child.Name}'이 ITacticNode가 아닙니다. 실행하지 않습니다.");
                _phase = children.Count;
                LastResult = TacticResult.Ineligible;
                return BtStatus.Failure;
            }
        }

        for (int i = _phase; i < children.Count; i++)
        {
            BehaviorTree_Node node = children[i];
            node.Behave(delta, owner);
            TacticResult result = (node as ITacticNode)?.LastResult ?? TacticResult.ActionCompleted;

            if (node is ITacticCommitting committing && committing.Committed) _committed = true;

            switch (result)
            {
                case TacticResult.Running:
                    _phase = i;   // 같은 단계를 다음 tick에 재개한다.
                    LastResult = TacticResult.Running;
                    return BtStatus.Running;

                case TacticResult.ActionCompleted:
                    continue;     // 이 단계 완료 — 다음 단계로.

                case TacticResult.ApproachConsumed:
                    _phase = children.Count;
                    LastResult = TacticResult.ApproachConsumed;   // 접근으로 턴 종료, 이후 단계 실행 안 함.
                    return BtStatus.Success;

                case TacticResult.CancelledAfterCommit:
                    _phase = children.Count;
                    LastResult = TacticResult.CancelledAfterCommit;
                    return BtStatus.Success;

                default: // Ineligible
                    _phase = children.Count;
                    // commit 이후의 실패는 다음 행으로 새면 안 된다(R6).
                    LastResult = _committed ? TacticResult.CancelledAfterCommit : TacticResult.Ineligible;
                    return LastResult.ToBtStatus();
            }
        }

        _phase = children.Count;
        LastResult = TacticResult.ActionCompleted;
        return BtStatus.Success;
    }
}
