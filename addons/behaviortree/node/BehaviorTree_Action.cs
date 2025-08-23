using System.Collections.Generic;
using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Action : BehaviorTree_Node
{
    public override List<BehaviorTree_Node> TreeChildren { get; } = new();

    protected override void OnTreeChanged()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
        }
    }

    protected override sealed BtStatus OnBehave(double delta, Node owner)
    {
        return PerformAction(delta, owner);
    }

    protected abstract BtStatus PerformAction(double delta, Node owner);
}