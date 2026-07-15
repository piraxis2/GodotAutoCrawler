using System.Collections.Generic;
using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 확정 대상에게 행동을 실행하는 택틱 행동의 공통 base(BT-002 Step 3~3.5, R6).
//
// per-action feasibility 바인딩(ADR-023 §10): 이 노드가 자신의 사거리/시작 가능성을 행 컨텍스트에 spec으로
// 공급하고, 셀렉터·접근이 그것으로 후보 feasibility를 계산한다.
//
// 결과 보존: 구현체의 실행 결과(Ineligible/Running/Completed/CancelledAfterCommit)를 그대로 택틱 결과로 옮긴다.
//  - Ineligible: commit 전 지불/검증 실패 -> 다음 행 fallback.
//  - Running: 진행 중 -> 턴 유지.
//  - Completed: 턴 소비(멀티턴이면 CurrentTurnAction으로 다음 턴 재개).
//  - CancelledAfterCommit: commit 후 무효 -> 턴 종료, fallback 금지.
[GlobalClass, Tool]
public abstract partial class TacticExecuteAction : BehaviorTree_Action,
    ITacticNode, ITacticContextBound, ITacticCommitting, ITacticActionSpec
{
    private TacticRowContext _context;

    public bool Committed { get; private set; }
    public TacticResult LastResult { get; private set; } = TacticResult.Ineligible;

    // --- ITacticActionSpec: 이 행동의 사거리와 시작 가능성(순수) ---
    public abstract IReadOnlyList<Vector2I> AttackRangePositions { get; }
    public abstract bool CanStart(Node owner);

    // 실제 실행(지불 + 효과). 결과를 보존해 반환한다.
    protected abstract TacticActionOutcome ExecuteAction(double delta, Node owner, GodotObject target);

    public void BindContext(TacticRowContext context)
    {
        _context = context;
        // 행의 feasibility가 이 행동의 사거리/비용을 보게 한다(§10).
        context.ActionSpec = this;
    }

    public virtual void ResetForNewTurn()
    {
        Committed = false;
        LastResult = TacticResult.Ineligible;
    }

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        ITacticActionAdapter adapter = (owner as ITacticActionAdapterProvider)?.TacticAdapter;
        if (adapter == null)
        {
            GD.PushError($"TacticExecuteAction '{Name}': owner가 어댑터를 제공하지 않습니다. 행동을 실행하지 않습니다.");
            LastResult = TacticResult.Ineligible;
            return BtStatus.Failure;
        }

        if (_context == null || !_context.HasValidConfirmedTarget)
        {
            // commit 후 대상 무효면 취소로 턴 종료, 전이면 불성립.
            LastResult = Committed ? TacticResult.CancelledAfterCommit : TacticResult.Ineligible;
            return LastResult.ToBtStatus();
        }

        GodotObject target = _context.ConfirmedTarget;

        // mutation-free 사전 게이트: 아직 commit 전인데 사거리 밖이면 실행(지불)하지 않고 불성립.
        if (!Committed && !adapter.IsInRange(owner, target, AttackRangePositions))
        {
            LastResult = TacticResult.Ineligible;
            return BtStatus.Failure;
        }

        TacticActionOutcome outcome = ExecuteAction(delta, owner, target);
        Committed = outcome != TacticActionOutcome.Ineligible;
        LastResult = outcome switch
        {
            TacticActionOutcome.Ineligible => TacticResult.Ineligible,
            TacticActionOutcome.Running => TacticResult.Running,
            TacticActionOutcome.CancelledAfterCommit => TacticResult.CancelledAfterCommit,
            _ => TacticResult.ActionCompleted
        };
        return LastResult.ToBtStatus();
    }
}
