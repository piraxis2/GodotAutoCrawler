namespace AutoCrawler.addons.behaviortree;
using Godot;

[GlobalClass]
public abstract partial class BT_Action: BT_Node
{
    public override BT_Status Tick(float delta, Node owner)
    {
        return PerformAction(delta, owner);
    }
    
    protected abstract BT_Status PerformAction(float delta, Node owner);
}