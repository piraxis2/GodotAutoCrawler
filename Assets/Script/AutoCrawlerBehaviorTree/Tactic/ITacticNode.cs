namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 택틱 런타임 노드가 부모에게 노출하는 계약(BT-002 Step 1).
// - LastResult: 직전 Behave의 내부 결과. 부모(Selector/Row)가 BtStatus만으로는 구분할 수 없는
//   Ineligible vs CancelledAfterCommit 등 commit 경계를 읽는 창구다.
// - ResetForNewTurn: 새 자기 턴 시작 시 latch/단계/후보를 제거한다(R1). TacticPrioritySelector가
//   턴 경계에서 하위로 전파해, _status 타이밍에 의존하지 않는 명시적 reset을 보장한다.
public interface ITacticNode
{
    TacticResult LastResult { get; }
    void ResetForNewTurn();
}
