namespace AutoCrawler.Assets.Script.Battle;

// 전투 결과 대분류(BS-001, ADR-022 §5). U1이 실제 정상 종료로 내는 조합은 Victory/OpponentsEliminated,
// Defeat/PlayerDefeated이며 설정 실패는 Aborted/InvalidSetup이다. Retreat는 타입 자리만 확보하고 동작은 후속이다.
public enum BattleOutcome
{
    Victory,
    Defeat,
    Retreat,
    Aborted,
}
