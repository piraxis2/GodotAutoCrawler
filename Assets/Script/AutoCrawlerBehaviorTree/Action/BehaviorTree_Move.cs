using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;

[GlobalClass, Tool]
public partial class BehaviorTree_Move : BehaviorTree_Action
{
    protected override Constants.BtStatus PerformAction(double delta, Node owner)
    {
        return Constants.BtStatus.Failure;
    }
}