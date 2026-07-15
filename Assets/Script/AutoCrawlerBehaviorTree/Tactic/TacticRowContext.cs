using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 행이 컨텍스트를 주입할 대상(조건·타깃 셀렉터·행동)이 구현하는 seam(BT-002 Step 2). 노드끼리 전역
// Blackboard 문자열 key로 임시 대상을 주고받지 않고, 행이 소유한 유닛별 컨텍스트를 공유한다(ADR-023 §5).
public interface ITacticContextBound
{
    void BindContext(TacticRowContext context);
}

// 한 행의 실행 문맥(BT-002 Step 2, ADR-023 §5). 조건이 만든 후보 집합과 확정 대상을 행 수명 동안 보관하고,
// 새 자기 턴에 폐기한다. 전역 상태가 아니라 행 인스턴스가 소유하므로 같은 구조의 두 유닛도 문맥을 공유하지
// 않는다(R9). 후보 identity는 Godot 인스턴스 id로 비교하고, freed 후보는 IsInstanceValid로 제외한다.
public sealed class TacticRowContext
{
    // null = 아직 어떤 target-bearing 조건도 후보를 만들지 않음(gate-only). 빈 리스트 = 교집합이 비었음.
    private List<GodotObject> _candidates;

    public GodotObject ConfirmedTarget { get; private set; }

    // 이 행의 접근 정책(셀렉터가 자신의 export에서 설정, 접근 노드가 소비). 기본은 더 관대한 ApproachAllowed.
    public TacticApproachPolicy Policy { get; set; } = TacticApproachPolicy.ApproachAllowed;

    // 이 행의 행동이 공급하는 사거리/시작 가능성(ADR-023 §10 per-action feasibility 바인딩). 행동 노드가
    // BindContext에서 자신을 등록하고, 셀렉터·접근이 이것으로 feasibility를 계산한다.
    public ITacticActionSpec ActionSpec { get; set; }

    // target-bearing 조건의 후보 기여. 첫 조건은 집합을 세우고, 이후 조건은 identity 교집합을 취한다(R3/R4).
    // 들어오는 후보 중 freed는 먼저 걸러 stale 참조를 남기지 않는다.
    public void ContributeCandidates(IEnumerable<GodotObject> candidates)
    {
        var incoming = candidates.Where(GodotObject.IsInstanceValid).ToList();
        if (_candidates == null)
        {
            _candidates = incoming;
            return;
        }

        var incomingIds = incoming.Select(c => c.GetInstanceId()).ToHashSet();
        _candidates = _candidates
            .Where(c => GodotObject.IsInstanceValid(c) && incomingIds.Contains(c.GetInstanceId()))
            .ToList();
    }

    // 살아 있는 후보만. 매 조회 시 freed를 다시 걸러 평가 사이 대상이 free돼도 stale 후보가 남지 않는다(R9).
    public IReadOnlyList<GodotObject> LiveCandidates =>
        _candidates?.Where(GodotObject.IsInstanceValid).ToList() ?? new List<GodotObject>();

    // 확정 대상이 아직 유효한지(조건·셀렉터·행동이 같은 identity를 보되 free 뒤에는 무효로 본다).
    public bool HasValidConfirmedTarget => ConfirmedTarget != null && GodotObject.IsInstanceValid(ConfirmedTarget);

    public void ConfirmTarget(GodotObject target)
    {
        ConfirmedTarget = target;
    }

    // 새 자기 턴 reset(R1). 후보와 확정 대상을 폐기한다.
    public void Reset()
    {
        _candidates = null;
        ConfirmedTarget = null;
    }
}
