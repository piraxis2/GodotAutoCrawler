using Godot;

namespace AutoCrawler.Assets.Script;

public partial class BattleFieldScene : Node2D 
{
    [Export]
    BattleFieldTileMapLayer TileMapLayer { get; set; }
    [Export]
    TurnHelper TurnHelper { get; set; }
    [Export]
    ArticlesContainer Articles { get; set; }
    [Export]
    FxPlayer FxPlayer { get; set; }
    
    public T GetBattleFieldCoreNode<T>() where T : Node
    {
        return typeof(T) switch
        {
            var t when t == typeof(BattleFieldTileMapLayer) => TileMapLayer as T,
            var t when t == typeof(TurnHelper) => TurnHelper as T,
            var t when t == typeof(ArticlesContainer) => Articles as T,
            var t when t == typeof(FxPlayer) => FxPlayer as T, 
            _ => null
        };
    }
}