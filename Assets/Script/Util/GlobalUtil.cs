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
        var battleFieldScene = GetBattleField(targetNode);
        return battleFieldScene?.GetBattleFieldCoreNode<T>();
    }
}