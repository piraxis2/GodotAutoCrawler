#if TOOLS
using AutoCrawler.addons.behaviortree.node;
using Godot;

namespace AutoCrawler.addons.behaviortree.debugger;

[Tool]
public partial class DebuggerTree: Tree
{
    private BehaviorTree _behaviorTree = null;
    
    public DebuggerTree()
    {
    }
    public DebuggerTree(BehaviorTree node)
    {
        _behaviorTree = node;
    }
}
#endif