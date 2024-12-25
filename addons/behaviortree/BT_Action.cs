namespace AutoCrawler.addons.behaviortree;
using Godot;

[GlobalClass, Tool]
public abstract partial class BT_Action: BT_Node
{
    public override BT_Status OnBehave(float delta, Node owner)
    {
        return PerformAction(delta, owner);
    }
    
    protected abstract BT_Status PerformAction(float delta, Node owner);
}