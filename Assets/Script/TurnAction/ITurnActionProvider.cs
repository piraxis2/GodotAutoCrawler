namespace AutoCrawler.Assets.Script.TurnAction;

// BT 노드가 자신이 감싼 `TurnActionBase`를 노출하는 공통 계약(BT-002 Step 3.5).
//
// 유닛의 행동 목록을 훑어야 하는 경로(전투 시작 ammo 충전, 사용 가능한 공격/사거리 조회)는 특정 wrapper
// 타입이 아니라 이 인터페이스로 순회한다. 그래야 수제 BT의 `BehaviorTree_TurnAction`과 택틱 보드의
// `TacticTurnAction`이 함께 발견되고, 새 wrapper가 생겨도 수집 경로가 조용히 누락되지 않는다.
public interface ITurnActionProvider
{
    TurnActionBase TurnAction { get; }
}
