namespace AutoCrawler.addons.behaviortree;
using Godot;


[GlobalClass, Tool]
public partial class BT_Selector: BT_Composite
{
    public override BT_Status Tick(float delta, Node owner)
    {
        foreach (BT_Node node in Children)
        {
            BT_Status status = node.Tick(delta, owner);
            if (status != BT_Status.Failure)
                return status;
        }
        return BT_Status.Failure;
    }
        
}