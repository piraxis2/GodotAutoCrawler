namespace AutoCrawler.Assets.Script.GameLog;

// 로그 항목 클릭 -> 관련 창 열림을 맡는 계약(E-9 결정 ⑤, D2). GameLogEntry는 직접 창을 열거나
// 리플레이를 실행하지 않고, link 데이터만 들고 있다가 이 handler에 넘긴다.
//
// v0는 읽기 전용이라 구현/호출부가 없다(예약 계약). 실제 클릭 배선은 창 호출 인프라(W0)와
// 리플레이 마커가 갖춰진 뒤 후속 Step에서 연결한다.
public interface IGameLogInteractionHandler
{
    bool CanHandle(GameLogLink link);
    void Handle(GameLogLink link);
}
