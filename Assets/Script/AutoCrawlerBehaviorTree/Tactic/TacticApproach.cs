using System.Collections.Generic;
using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 확정 대상을 향한 접근(BT-002 Step 3, 기획 H-3-2, ADR-023 §6). 대상은 컨텍스트에서 공유받고 이동은 owner의
// 어댑터에 위임한다.
//
//  - 이미 사거리 안이면 이동 없이 `ActionCompleted`(=시퀀스에 "행동으로 진행" 신호)를 낸다.
//  - 이동이 필요하면 어댑터가 한 틱 접근을 실행한다(첫 이동이 commit). Moving=Running, Arrived=진행,
//    Exhausted=이번 턴 이동력 소진.
//  - 이동력 소진 시: `ApproachAllowed`는 `ApproachConsumed`(접근으로 턴 종료), `Immediate`는 셀렉터가
//    도달 가능 후보만 통과시켰으므로 정상적으로는 도달하며, 그럼에도 못 하면 `CancelledAfterCommit`.
//
// commit 여부(이동했는가)를 ITacticCommitting으로 보고해, 시퀀스가 commit 후 실패를 fallback 금지로 승격한다.
[GlobalClass, Tool]
public partial class TacticApproach : BehaviorTree_Action, ITacticNode, ITacticContextBound, ITacticCommitting
{
    private TacticRowContext _context;
    private Node _owner;
    private bool _moving;   // 이동 tween이 진행 중인가(취소 대상).

    public bool Committed { get; private set; }
    public TacticResult LastResult { get; private set; } = TacticResult.Ineligible;

    public void BindContext(TacticRowContext context) => _context = context;

    public void ResetForNewTurn()
    {
        // 진행 중 접근이 있으면 어댑터에 취소를 알려 이동 tween 누수를 막는다(P2-1).
        if (_moving && _owner is ITacticActionAdapterProvider provider)
        {
            provider.TacticAdapter?.CancelApproach(_owner);
        }
        _moving = false;
        Committed = false;
        LastResult = TacticResult.Ineligible;
    }

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        _owner = owner;
        ITacticActionAdapter adapter = (owner as ITacticActionAdapterProvider)?.TacticAdapter;
        if (adapter == null)
        {
            GD.PushError($"TacticApproach '{Name}': owner가 어댑터를 제공하지 않습니다. 접근을 실행하지 않습니다.");
            LastResult = TacticResult.Ineligible;
            return BtStatus.Failure;
        }

        if (_context == null || !_context.HasValidConfirmedTarget)
        {
            // 확정 대상이 없거나 free됨. commit 전이면 불성립, 후면 취소로 턴 종료.
            _moving = false;
            LastResult = Committed ? TacticResult.CancelledAfterCommit : TacticResult.Ineligible;
            return LastResult.ToBtStatus();
        }

        // 사거리는 행의 행동이 공급한다(ADR-023 §10). spec이 없으면 어디까지 접근해야 할지 알 수 없다.
        ITacticActionSpec spec = _context.ActionSpec;
        if (spec == null)
        {
            GD.PushError($"TacticApproach '{Name}': 행에 행동 spec이 없습니다(사거리 불명). 접근하지 않습니다.");
            LastResult = TacticResult.Ineligible;
            return BtStatus.Failure;
        }

        GodotObject target = _context.ConfirmedTarget;
        IReadOnlyList<Vector2I> range = spec.AttackRangePositions;

        if (adapter.IsInRange(owner, target, range))
        {
            // 이동 없이 사거리 안 — 행동으로 진행.
            _moving = false;
            LastResult = TacticResult.ActionCompleted;
            return BtStatus.Success;
        }

        ApproachStep step = adapter.Approach(owner, target, range, delta);
        Committed = true;   // 이동을 실행함(commit).
        switch (step)
        {
            case ApproachStep.Moving:
                _moving = true;
                LastResult = TacticResult.Running;
                break;
            case ApproachStep.Arrived:
                _moving = false;
                LastResult = TacticResult.ActionCompleted;   // 사거리 진입 — 같은 턴 행동으로 진행.
                break;
            default: // Exhausted
                _moving = false;
                LastResult = _context.Policy == TacticApproachPolicy.ApproachAllowed
                    ? TacticResult.ApproachConsumed
                    : TacticResult.CancelledAfterCommit;
                break;
        }
        return LastResult.ToBtStatus();
    }
}
