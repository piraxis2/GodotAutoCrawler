using System.Collections.Generic;

namespace AutoCrawler.Assets.Script.Battle;

// 전투 사실만 담는 결과(ADR-022 §5). 보상/XP/주차/WorldState/저장/UI 전환은 호출자 소유다.
public sealed record BattleResult
{
    public required BattleOutcome Outcome { get; init; }
    public required BattleEndReason EndReason { get; init; }
    public required long Seed { get; init; }
    public required bool PlayerAlive { get; init; }
    public required int SurvivingAllies { get; init; }
    public required int SurvivingOpponents { get; init; }
    public required IReadOnlyList<BattleUnitResult> Units { get; init; }
}
