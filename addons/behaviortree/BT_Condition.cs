namespace AutoCrawler.addons.behaviortree;
using Godot;

public abstract partial class BT_Condition: BT_Node
{
    public override BT_Status Tick(float delta, Node owner)
    {
        return CheckCondition(delta, owner);
    }
    
    protected abstract BT_Status CheckCondition(float delta, Node owner);
}