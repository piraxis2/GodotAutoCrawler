using Godot;

namespace AutoCrawler.addons.behaviortree;

public partial class BT_Sequence: BT_Composite
{
    public override BT_Status Tick(float delta, Node owner)
    {
        foreach (Node node in GetChildren())
        {
            if (node is BT_Node == false)
                continue;

            BT_Status status = ((BT_Node)node).Tick(delta, owner);
            if (status != BT_Status.Success)
            {
                return status;
            }
        }
        return BT_Status.Success;
    }
}