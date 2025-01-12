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
}