namespace AutoCrawler.Assets.Script.GameLog;

// 로그 항목 클릭이 열 대상의 종류(E-9 인터랙션, 목표 상태). v0는 예약만 하고 실제 창을 열지 않는다.
public enum GameLogLinkType
{
    None,
    ReplayMarker,
    UnitDetail,
    TacticBoardBlock,
    DialogueArchive,
    ItemDetail,
}
