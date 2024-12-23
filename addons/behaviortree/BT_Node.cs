namespace AutoCrawler.addons.behaviortree;
using Godot;
using System;


[GlobalClass, Tool]
public abstract partial class BT_Node : Node
{
    public enum BT_Status { Success, Failure, Running }
    
    public virtual BT_Status Tick(float delta, Node owner)
    {
        return BT_Status.Failure;
    }
}