namespace AutoCrawler.addons.behaviortree;
using Godot;
using System;


[GlobalClass, Tool]
public abstract partial class BT_Node : Node
{
    public enum BT_Status { Success, Failure, Running }
    
    public virtual BT_Status OnBehave(double delta, Node owner)
    {
        return BT_Status.Failure;
    }

    public BT_Node GetRoot()
    {
        BT_Node node = this;
        while (node.GetParent() is BT_Node)
        {
            node = (BT_Node)node.GetParent();
        }
        return node;
    }
}