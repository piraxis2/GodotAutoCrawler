using Godot;

namespace AutoCrawler.Assets.Script;

public partial class BattleFieldScene : Node2D
{
    [Export] public BattleFieldTileMapLayer TileMapLayer { get; private set; }
    [Export] public TurnHelper TurnHelper { get; private set; }
    [Export] public ArticlesContainer Articles { get; private set; }
    [Export] public FxPlayer FxPlayer { get; private set; }

}