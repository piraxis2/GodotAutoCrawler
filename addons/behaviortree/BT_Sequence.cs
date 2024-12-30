using Godot;

namespace AutoCrawler.addons.behaviortree;

[GlobalClass, Tool]
public partial class BT_Sequence: BT_Composite
{
    protected override BT_Status OnBehave(double delta, Node owner)
    {
        foreach (BT_Node node in Children)
        {
            BT_Status status = node.Behave(delta, owner);
            if (status is BT_Status.Success) continue;
            
            return status;
        }
        return BT_Status.Success;
    }
}