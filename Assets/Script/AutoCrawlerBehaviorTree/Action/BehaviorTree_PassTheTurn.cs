using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;

public partial class BehaviorTree_PassTheTurn: BehaviorTree_TurnAction
{
    protected override Constants.BtStatus PerformAction(double delta, Node owner)
    {
        return Constants.BtStatus.Success;
    }
}