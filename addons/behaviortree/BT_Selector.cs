namespace AutoCrawler.addons.behaviortree;
using Godot;


[GlobalClass, Tool]
public partial class BT_Selector : BT_Composite
{
    public override BT_Status OnBehave(float delta, Node owner)
    {
        foreach (BT_Node node in Children)
        {
            BT_Status status = node.OnBehave(delta, owner);
            if (status is BT_Status.Failure or BT_Status.Running) continue;

            return status;
        }
        return BT_Status.Failure;
    }
        
}