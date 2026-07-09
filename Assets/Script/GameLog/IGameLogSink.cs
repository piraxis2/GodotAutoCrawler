namespace AutoCrawler.Assets.Script.GameLog;

// 사건을 GameLog로 흘려보내는 최소 계약(E-9, D3). 전투/대화/아웃게임/시스템이 각자 UI를 알지 않고
// 같은 sink로 entry를 보낸다. v0는 Sink만 둔다 — 각 시스템이 독립 발행을 시작하면 IGameLogSource를 추가한다.
public interface IGameLogSink
{
    void Append(GameLogEntry entry);
}
