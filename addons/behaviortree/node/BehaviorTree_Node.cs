
using System;
using System.Diagnostics;
using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Node : Node
{
    public enum BtStatus { Success, Failure, Running }

    public BehaviorTree Tree { get; internal set; }

    public override void _Ready()
    {
        Connect("child_entered_tree", new Callable(this, nameof(OnChildEnteredTree)));
        Connect("child_exiting_tree", new Callable(this, nameof(OnChildExitingTree)));
    }
    
    public virtual void OnChildEnteredTree(Node child)
    {
        if (child is not BehaviorTree_Node)
        {
            RemoveChild(child);
            throw new InvalidOperationException("BT_Node 노드에는 BT_Node 노드만 추가할 수 있습니다.");
        }
    }

    public virtual void OnChildExitingTree(Node child) {}
    
    public BtStatus Behave(double delta, Node owner)
    {
#if TOOLS
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        BtStatus status = OnBehave(delta, owner);

#if TOOLS
        stopwatch.Stop();
        GD.Print("Behave time: ", stopwatch.ElapsedMilliseconds);
#endif
        return status;
    }
    protected virtual BtStatus OnBehave(double delta, Node owner)
    {
        return BtStatus.Failure;
    }

    public BehaviorTree_Node GetRoot()
    {
        return Tree.Root;
    }
}