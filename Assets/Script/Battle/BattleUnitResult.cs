using Godot;

namespace AutoCrawler.Assets.Script.Battle;

// 전투 한 번 안에서만 유효한 유닛 결과(scene-free 값, ADR-022 §5). 표시 이름/NodePath/Godot 객체 참조를
// 안정 식별자로 쓰지 않는다.
public sealed record BattleUnitResult
{
    // v0는 Faction + SpawnIndex로 만든다.
    public required string SessionUnitId { get; init; }
    public required string Faction { get; init; }
    public required bool Survived { get; init; }

    // Health가 없는 Article도 존재할 수 있어 nullable.
    public int? FinalHealth { get; init; }
    public required Vector2I FinalTile { get; init; }
}
