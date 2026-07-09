namespace AutoCrawler.Assets.Script.GameLog;

// 노출 범위(E-9). Importance(시각 강조)와 섞지 않는다 — Severity만 필터/노출 판단에 쓴다.
public enum GameLogSeverity
{
    // 기본 뷰(전체 탭)에 보이는 사건.
    Event,

    // 채널 탭에서만 보이는 상세.
    Detail,

    // debug toggle 또는 개발 빌드에서만 보이는 내부 추적.
    Trace,
}
