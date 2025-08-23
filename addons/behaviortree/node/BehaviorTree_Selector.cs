using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public partial class BehaviorTree_Selector : BehaviorTree_Composite
{
    protected override BtStatus OnBehave(double delta, Node owner)
    {
        foreach (BehaviorTree_Node node in TreeChildren)
        {
            BtStatus status = node.Behave(delta, owner);
            if (status is BtStatus.Failure) continue;

            return status;
        }
        return BtStatus.Failure;
    }
        
}