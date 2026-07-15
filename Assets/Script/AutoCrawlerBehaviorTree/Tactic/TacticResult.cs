using AutoCrawler.addons.behaviortree;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 택틱 행 실행의 내부 결과(BT-002 Step 0 OD1). 전역 BtStatus를 늘리지 않고 택틱 노드 경계에서만 쓰며,
// TacticPrioritySelector가 BtStatus로 변환해 기존 composite/editor/debugger에 노출한다.
//
//   Ineligible           -> 성립 실패(비용/대상/경로/조건). 다음 행으로 fallback (commit 전에만 허용).
//   Running              -> 실행 중. 같은 행/대상을 다음 tick에 재개하며 상위 행으로 preempt 금지(R7).
//   ActionCompleted      -> 행동을 실행하고 턴을 소비. 턴 종료.
//   ApproachConsumed     -> 접근으로 턴을 소비(도달 실패). 턴 종료. (Step 3에서 실증)
//   CancelledAfterCommit -> commit 이후 대상 무효/취소/실패. 턴 종료, 다음 행 금지(R6).
public enum TacticResult
{
    Ineligible,
    Running,
    ActionCompleted,
    ApproachConsumed,
    CancelledAfterCommit
}

public static class TacticResultExtensions
{
    // Step 0 확정 매핑(OD1). Ineligible만 Failure(=Selector fallback), 나머지 commit/완료 결과는 모두
    // Success로 접혀 스케줄러가 턴을 종료(AdvanceToNextTurn)하게 한다. commit 후 결과가 Success로 변환되므로
    // 다음 행 fallback이 구조적으로 일어나지 않는다(R6).
    public static BtStatus ToBtStatus(this TacticResult result) => result switch
    {
        TacticResult.Ineligible => BtStatus.Failure,
        TacticResult.Running => BtStatus.Running,
        _ => BtStatus.Success
    };
}
