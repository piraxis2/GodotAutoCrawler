namespace AutoCrawler.Assets.Script.GameLog;

// 로그의 저장 분류(E-9). `All`은 여기에 없다 — 전체는 저장 채널이 아니라 UI 필터이고,
// GameLogModel.GetEntries의 nullable channel 인자로 표현한다.
public enum GameLogChannel
{
    Combat,
    Story,
    Reward,
    System,
}
