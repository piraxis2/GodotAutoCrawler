using Godot;

namespace AutoCrawler.Assets.Script.Battle;

// U1 축소 request(ADR-022 §4). 최종적으로 encounter/modifier/party snapshot으로 확장하되 값 객체 경계를 유지한다.
// 새 전투 seed 생성 정책은 호출자 소유다. BattleSession은 받은 seed를 사용하고 결과에 보존한다.
public sealed record BattleRequest
{
    public required PackedScene BattleScene { get; init; }
    public required long Seed { get; init; }

    // 전투 씬 루트 기준 PC 경로(예: "Articles/Ally/Character"). U1 임시 계약이며 장기적으로 PartySnapshot의
    // 안정적인 unit_id로 대체한다.
    public required NodePath PlayerPath { get; init; }
}
