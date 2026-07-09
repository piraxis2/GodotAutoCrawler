namespace AutoCrawler.Assets.Script.GameLog;

// 시각 강조(E-9). 노출 범위(Severity)와 독립이다 — Importance는 표시 강조에만 쓰고
// entry가 어느 뷰에 보이는지 판단에는 절대 쓰지 않는다.
public enum GameLogImportance
{
    Normal,
    Warning,
    Critical,
}
