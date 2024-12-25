using AutoCrawler.addons.behaviortree;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Action;

[GlobalClass, Tool]
public partial class BT_Move : BT_Action
{
    private AStar2D _aStar2D;
    protected override BT_Status PerformAction(float delta, Node owner)
    {
        return BT_Status.Failure;
    }
}