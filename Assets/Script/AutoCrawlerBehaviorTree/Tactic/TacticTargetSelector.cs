using System.Collections.Generic;
using System.Linq;
using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.TurnAction.Skill;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 행의 확정 대상 셀렉터(BT-002 Step 2, ADR-023 §5, R4). 조건이 컨텍스트에 모은 후보에서 하나를 확정한다.
//
// 처리 순서: 조건 후보(컨텍스트) → 호환 필터 → ADR-017 tie-break(거리→Y→X) → 대상 하나 확정.
//  - 살아 있는 후보가 없으면(빈 교집합·gate-only·모두 freed) `Failure`로 행을 불성립시킨다.
//  - 확정 대상은 컨텍스트에 기록되어 같은 행의 접근·행동이 같은 identity를 공유한다.
//
// 처리 순서(ADR-023 §5): 조건 후보 → 호환/접근 정책 가능 후보 → tie-break → 확정. 접근 정책 feasibility는
// 대상 확정 "전에" 걸러야 여러 영창 적 중 이번 턴 방해 가능한 후보에서 선택된다(H-3-2). 그래서 접근 가능성
// 필터가 셀렉터에 있다. owner가 어댑터를 제공하지 않으면(예: Step 2 tie-break 테스트) 필터는 pass-through라
// Step 2 동작이 보존된다.
[GlobalClass, Tool]
public partial class TacticTargetSelector : BehaviorTree_Action, ITacticContextBound, ITacticTargetSelector
{
    // 이 행의 접근 정책. 컴파일러가 조건·행동 조합으로 결정하며(H-3-2), Step 3 런타임은 export로 받는다.
    [Export] public TacticApproachPolicy Policy { get; set; } = TacticApproachPolicy.ApproachAllowed;

    private TacticRowContext _context;

    public void BindContext(TacticRowContext context) => _context = context;

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        if (_context == null) return BtStatus.Failure;

        // 정규 순서의 원점은 owner의 타일 좌표다. owner가 ITacticTarget이 아니면(예: 실제 ArticleBase 배선
        // 누락) 원점을 알 수 없다. 조용히 (0,0)을 쓰면 엉뚱한 대상을 확정하므로 fail-closed한다.
        if (owner is not ITacticTarget originTarget)
        {
            GD.PushError($"TacticTargetSelector '{Name}': owner가 ITacticTarget이 아닙니다(원점 없음). 대상을 확정하지 않습니다.");
            return BtStatus.Failure;
        }

        // 행의 접근 정책을 컨텍스트에 실어 접근 노드가 같은 정책을 소비하게 한다.
        _context.Policy = Policy;

        ITacticActionAdapter adapter = (owner as ITacticActionAdapterProvider)?.TacticAdapter;
        Vector2I origin = originTarget.TilePosition;

        GodotObject chosen = _context.LiveCandidates
            .Where(candidate => candidate is ITacticTarget
                                && IsCompatible(candidate)
                                && IsActionable(adapter, owner, candidate))
            .OrderByCanonical(candidate => ((ITacticTarget)candidate).TilePosition, origin)
            .FirstOrDefault();

        if (chosen == null) return BtStatus.Failure;

        _context.ConfirmTarget(chosen);
        return BtStatus.Success;
    }

    // 행동 호환 + 접근 정책 가능 후보 필터(§5, §10). 어댑터나 행동 spec이 없으면 pass-through(Step 2 보존).
    //  - 행동을 시작할 수 없으면(비용/탄약 부족) 어떤 후보도 성립하지 않는다(R3).
    //  - Immediate: 지금 사거리 안이거나 이번 턴 이동(Mobility+1) 후 행동 가능한 후보만.
    //  - ApproachAllowed: 지금 사거리 안이거나 접근 경로가 있는 후보만.
    private bool IsActionable(ITacticActionAdapter adapter, Node owner, GodotObject candidate)
    {
        if (adapter == null) return true;

        ITacticActionSpec spec = _context.ActionSpec;
        if (spec == null) return true;

        if (!spec.CanStart(owner)) return false;

        IReadOnlyList<Vector2I> range = spec.AttackRangePositions;
        return Policy == TacticApproachPolicy.Immediate
            ? adapter.IsInRange(owner, candidate, range) || adapter.CanReachAndActThisTurn(owner, candidate, range)
            : adapter.IsInRange(owner, candidate, range) || adapter.HasApproachPath(owner, candidate, range);
    }

    // 진영/타입 등 대상 호환 seam. Step 2/3에서는 pass-through, 후속 스킬 어휘에서 override한다.
    protected virtual bool IsCompatible(GodotObject candidate) => true;
}
