using System.Collections.Generic;
using Godot;
using AutoCrawler.Assets.Script;

namespace AutoCrawler.Assets.Script.Util;

public static class GlobalUtil
{
    public static BattleFieldScene GetBattleField(Node targetNode)
    {
        var battleFieldScene = targetNode.GetTree().CurrentScene as BattleFieldScene;
        return battleFieldScene;
    }
    
    public static T GetBattleFieldCoreNode<T>(Node targetNode) where T : Node
    {
        var currentScene = targetNode.GetTree().CurrentScene;
        if (currentScene is BattleFieldScene)
        {
            return typeof(T) switch
            {
                var t when t == typeof(BattleFieldTileMapLayer) => currentScene.GetNode<T>("TileMapLayer"),
                var t when t == typeof(TurnHelper) => currentScene.GetNode<T>("TurnHelper"),
                var t when t == typeof(ArticlesContainer) => currentScene.GetNode<T>("Articles"),
                var t when t == typeof(FxPlayer) => currentScene.GetNode<T>("FxPlayer"),
                _ => null
            };
        }
        return null;
    }


}