namespace AutoCrawler.addons.behaviortree;
using Godot;


[GlobalClass, Tool]
public partial class BT_Selector : BT_Composite
{
    protected override BT_Status OnBehave(double delta, Node owner)
    {
        foreach (BT_Node node in Children)
        {
            BT_Status status = node.Behave(delta, owner);
            if (status is BT_Status.Failure) continue;

            return status;
        }
        return BT_Status.Failure;
    }
        
}