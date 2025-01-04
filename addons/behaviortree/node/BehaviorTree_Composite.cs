using System.Collections.Generic;
using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Composite : BehaviorTree_Node
{
    private List<BehaviorTree_Node> _children = new List<BehaviorTree_Node>();

    public override List<BehaviorTree_Node> GetTreeChildren()
    {
        return _children;
    }

    protected override void OnTreeChanged()
    {
        _children.Clear();
        foreach (var child in GetChildren())
        {
            if (child is BehaviorTree_Node node)
            {
                _children.Add(node);
            }
        }
    }
}