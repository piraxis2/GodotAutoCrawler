namespace AutoCrawler.addons.behaviortree;
using Godot;

public partial class BT_Selector: BT_Composite
{
    public override BT_Status Tick(float delta, Node owner)
    {
        foreach (Node node in GetChildren())
        {
            if (node is BT_Node == false)
                continue;
            
            BT_Status status = ((BT_Node)node).Tick(delta, owner);
            if (status != BT_Status.Failure)
            {
                return status;
            }
        }
        return BT_Status.Failure;
    }
        
}