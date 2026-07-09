namespace AutoCrawler.Assets.Script.GameLog;

// 클릭 대상 예약 데이터(E-9 결정 ⑤). 직접 메서드를 들고 있지 않는다 — 실제 동작은
// IGameLogInteractionHandler가 맡는다(D2). v0에서는 typed field만 두고 string dictionary(Payload)는 피한다.
public sealed class GameLogLink
{
    public GameLogLink(GameLogLinkType type, string targetId = null)
    {
        Type = type;
        TargetId = targetId;
    }

    public GameLogLinkType Type { get; }

    // 대상 id. 링크 종류에 따라 의미가 다르다(리플레이 마커 시각, 유닛 id 등). 예약 필드.
    public string TargetId { get; }
}
