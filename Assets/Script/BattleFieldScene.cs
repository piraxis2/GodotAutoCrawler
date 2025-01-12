using Godot;

namespace AutoCrawler.Assets.Script;

public partial class BattleFieldScene : Node2D 
{
    public T GetBattleFieldCoreNode<T>() where T : Node
    {
        return typeof(T) switch
        {
            var t when t == typeof(BattleFieldTileMapLayer) => GetNode<T>("TileMapLayer"),
            var t when t == typeof(TurnHelper) => GetNode<T>("TurnHelper"),
            var t when t == typeof(ArticlesContainer) => GetNode<T>("Articles"),
            _ => null
        };
    }
}