using AutoCrawler.addons.behaviortree;
using Godot;

namespace AutoCrawler.Assets.Script.Article.BT.Action;

[GlobalClass, Tool]
public partial class BT_AutoCrwaler_Move : BT_Action
{
    private AStarGrid2D _aStar2D;
    protected override BT_Status PerformAction(double delta, Node owner)
    {
        return BT_Status.Failure;
    }
}