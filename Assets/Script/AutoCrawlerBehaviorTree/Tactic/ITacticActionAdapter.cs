using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 행의 접근 정책(기획 H-3-2, ADR-023 §6).
//  - Immediate: 현재 위치 또는 이번 턴 이동 후 행동까지 가능한 후보만 성립. 불가능하면 접근하지 않고 다음 행.
//  - ApproachAllowed: 유효 대상과 경로가 있으면 성립. 이번 턴 도달 못 하면 접근으로 턴을 소비한다.
public enum TacticApproachPolicy
{
    Immediate,
    ApproachAllowed
}

// 한 틱 접근 실행의 결과.
//  - Moving: 아직 이동 중(다음 tick 재개, Running).
//  - Arrived: 이동 후 사거리 진입(같은 턴 행동으로 이어짐).
//  - Exhausted: 이번 턴 이동력을 다 썼지만 사거리에 못 들어옴.
public enum ApproachStep
{
    Moving,
    Arrived,
    Exhausted
}

// 실제 행동 실행 결과. 실제 TurnActionBase.Action()의 Failure/Running/Executed/End를 택틱 경계로 보존한다.
//  - Ineligible: commit 전 지불/검증 실패. 상태 변경 없음 -> 다음 행 fallback.
//  - Running: 행동이 시작돼 이번 tick 진행 중. 턴을 넘기지 않는다.
//  - Completed: 행동을 실행하고 턴을 소비함(멀티턴이면 CurrentTurnAction으로 다음 턴 재개).
//  - CancelledAfterCommit: commit 이후 대상 무효/취소 -> 턴 종료, fallback 금지.
public enum TacticActionOutcome
{
    Ineligible,
    Running,
    Completed,
    CancelledAfterCommit
}

// per-action feasibility 바인딩(ADR-023 §10). 사거리와 시작 가능성(비용/탄약)은 행의 **구체 행동**마다 다르므로,
// 행동 노드가 자신의 spec을 행 컨텍스트에 공급하고 셀렉터·접근이 그것으로 feasibility를 계산한다.
// 두 메서드 모두 순수해야 한다 — 지불·이동·상태 변경을 일으키지 않는다(R3).
public interface ITacticActionSpec
{
    // 이 행동이 닿는 상대 위치 오프셋(owner 기준 상대 좌표, ADR-017 정규 순서).
    IReadOnlyList<Vector2I> AttackRangePositions { get; }
    // 지금 이 행동을 시작할 수 있는가(비용/탄약/마나). 지불하지 않는다.
    bool CanStart(Node owner);
}

// 택틱 런타임이 전장(타일/AStar/상대/이동)에 접근하는 seam(BT-002 Step 3~3.5). 행동의 사거리는 owner가 아니라
// 행의 spec이 공급하므로 feasibility 메서드는 range 오프셋을 인자로 받는다(§10).
//
// 성립 검사(IsInRange/CanReachAndActThisTurn/HasApproachPath)는 전장 상태(타일 점유·유닛 위치·자원)를 변형하지
// 않아야 한다. 내부 AStar 사본을 쓰는 것은 무방하다. 실제 이동은 Approach에서만 발생한다(R3/R6).
public interface ITacticActionAdapter
{
    // 살아 있는 상대 후보(제품 어휘 조건이 후보를 모을 때 쓴다).
    IReadOnlyList<GodotObject> GetLivingOpponents(Node owner);

    // --- mutation-free feasibility ---
    bool IsInRange(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets);
    // 이번 턴 이동력(단일 소스 Mobility+1) 안에서 사거리 진입이 가능한가(Immediate 성립 판정).
    bool CanReachAndActThisTurn(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets);
    // 대상 사거리까지 이르는 경로가 (이번 턴을 넘겨서라도) 존재하는가(ApproachAllowed 성립 판정).
    bool HasApproachPath(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets);

    // --- commit ---
    // 접근을 실제 실행한다(첫 이동이 commit). 이번 턴 이동력 안에서 진행하며, 이동 연출이 남아 있으면 Moving.
    ApproachStep Approach(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets, double delta);
    // 진행 중 접근을 취소한다(이동 tween Kill로 누수 방지). 새 자기 턴 reset에서 호출된다.
    void CancelApproach(Node owner);
    // 새 자기 턴에 이동력 예산을 초기화한다.
    void ResetTurn(Node owner);
}

// owner(CharacterArticle 또는 테스트 대역)가 자신의 전장 어댑터를 택틱 노드에 제공하는 seam. 노드는 Behave의
// owner에서 어댑터를 얻는다. 어댑터가 없으면 셀렉터의 feasibility 필터는 pass-through다(Step 2 보존).
public interface ITacticActionAdapterProvider
{
    ITacticActionAdapter TacticAdapter { get; }
}

// 이번 턴에 상태를 변경(이동/지불)했는지 보고하는 seam. 행동 시퀀스가 commit 이후 실패를 fallback 금지
// 대상(CancelledAfterCommit)으로 승격하는 데 쓴다(R6).
public interface ITacticCommitting
{
    bool Committed { get; }
}
