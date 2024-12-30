
namespace AutoCrawler.addons.behaviortree;
using Godot;
using System;
using System.Diagnostics;


[GlobalClass, Tool]
public abstract partial class BT_Node : Node
{
    public enum BT_Status { Success, Failure, Running }
    
    public override void _Ready()
    {
        Connect("child_entered_tree", new Callable(this, nameof(OnChildEnteredTree)));
    }
    
    public virtual void OnChildEnteredTree(Node child)
    {
        if (child is not BT_Node)
        {
            RemoveChild(child);
            throw new InvalidOperationException("BT_Node 노드에는 BT_Node 노드만 추가할 수 있습니다.");
        }
    }
    
    public BT_Status Behave(double delta, Node owner)
    {
#if TOOLS
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        BT_Status status = OnBehave(delta, owner);

#if TOOLS
        stopwatch.Stop();
        GD.Print("Behave time: ", stopwatch.ElapsedMilliseconds);
#endif
        return status;
    }
    protected virtual BT_Status OnBehave(double delta, Node owner)
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