using Godot;

namespace AutoCrawler.addons.behaviortree;


[GlobalClass]
public partial class BT_Sequence: BT_Composite
{
    public override BT_Status Tick(float delta, Node owner)
    {
        foreach (BT_Node node in Children)
        {
            BT_Status status = node.Tick(delta, owner);
            if (status != BT_Status.Success)
                return status;
        }
        return BT_Status.Success;
    }
}