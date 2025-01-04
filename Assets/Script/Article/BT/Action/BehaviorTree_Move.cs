using AutoCrawler.addons.behaviortree;
using Godot;
using BehaviorTree_Action = AutoCrawler.addons.behaviortree.node.BehaviorTree_Action;

namespace AutoCrawler.Assets.Script.Article.BT.Action;

[GlobalClass, Tool]
public partial class BehaviorTree_Move : BehaviorTree_Action
{
    private AStarGrid2D _aStar2D;
    protected override Constants.BtStatus PerformAction(double delta, Node owner)
    {
        return Constants.BtStatus.Failure;
    }
}