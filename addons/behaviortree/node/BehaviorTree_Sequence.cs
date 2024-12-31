using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public partial class BehaviorTree_Sequence: BehaviorTree_Composite
{
    protected override BtStatus OnBehave(double delta, Node owner)
    {
        foreach (BehaviorTree_Node node in Children)
        {
            BtStatus status = node.Behave(delta, owner);
            if (status is BtStatus.Success) continue;
            
            return status;
        }
        return BtStatus.Success;
    }
}