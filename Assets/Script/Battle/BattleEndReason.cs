namespace AutoCrawler.Assets.Script.Battle;

// 종료 이유(BS-001, ADR-022 §5). U1 실사용: OpponentsEliminated, PlayerDefeated, InvalidSetup.
// RoundLimit/ManualRetreat/SceneRemoved는 타입 자리만 확보하고 동작은 후속이다.
public enum BattleEndReason
{
    OpponentsEliminated,
    PlayerDefeated,
    RoundLimit,
    ManualRetreat,
    InvalidSetup,
    SceneRemoved,
}
