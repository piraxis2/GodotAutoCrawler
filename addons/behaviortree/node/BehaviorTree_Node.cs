using System.Diagnostics;
using Godot;
using Godot.Collections;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Node : Node
{

    public enum BtStatus { Success, Failure, Running }

    public BehaviorTree Tree { get; internal set; }

#if TOOLS
    public delegate void OnLogChangedEventHandler(Util.BehaviorLog log);
    public event OnLogChangedEventHandler OnLogChanged; 
    
#endif

    public abstract Array<BehaviorTree_Node> GetTreeChildren();

    public override sealed void _Ready()
    {
        Connect("child_entered_tree", new Callable(this, nameof(OnChildEnteredTree)));
        Connect("child_exiting_tree", new Callable(this, nameof(OnChildExitingTree)));
        Connect("child_order_changed", new Callable(this, nameof(OnChildOrderChanged)));
        OnReady();
    }

    protected virtual void OnReady(){}

    public void OnChildEnteredTree(Node child)
    {
        if (Tree == null)
        {
            GD.PushWarning("BehaviorTree_Node 노드는 BehaviorTree 노드에 추가되어야 합니다.");
            return;
        }
        
        if (child is not BehaviorTree_Node behaviorTreeNode)
        {
            RemoveChild(child);
            GD.PushWarning("BehaviorTree_Node 노드에는 BehaviorTree_Node 노드만 추가할 수 있습니다.");
            return;
        }

        behaviorTreeNode.Tree = Tree;
        BehaviorChildEnteredTree(child);
    }

    public void OnChildExitingTree(Node child)
    {
        if (child is not BehaviorTree_Node behaviorTreeNode)
        {
            return;
        }
        behaviorTreeNode.Tree = null;
        BehaviorChildExitingTree(child);
    }
    
    public void OnChildOrderChanged()
    {
        Tree?.OnUpdate();
    }

    public virtual void BehaviorChildEnteredTree(Node child) {}
    public virtual void BehaviorChildExitingTree(Node child) {}
    
    public BtStatus Behave(double delta, Node owner)
    {
#if TOOLS
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        BtStatus status = OnBehave(delta, owner);

#if TOOLS
        stopwatch.Stop();
        Util.BehaviorLog behaviorLog = new Util.BehaviorLog();
        behaviorLog.Time = stopwatch.ElapsedMilliseconds;
        behaviorLog.Status = status;
        OnLogChanged?.Invoke(behaviorLog);
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