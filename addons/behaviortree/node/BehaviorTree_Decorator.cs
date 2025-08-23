using System.Collections.Generic;
using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Decorator : BehaviorTree_Node
{
    private BehaviorTree_Node Child { get; set; }

    public override List<BehaviorTree_Node> TreeChildren => Child != null ? new List<BehaviorTree_Node> { Child } : new List<BehaviorTree_Node>();

    protected override void OnTreeChanged()
    {
        Child = null;
        foreach (var child in GetChildren())
        {
            if (Child == null)
            {
                if (child is BehaviorTree_Node node)
                {
                    Child = node;
                }
            }
            else
            {
                RemoveChild(child);
            }
        }
    }

    protected override BtStatus OnBehave(double delta, Node owner)
    {
        if (Child == null)
        {
            throw new System.InvalidOperationException("BT_Decorator 노드에 자식 노드가 설정되지 않았습니다.");
        }

        return Decorate(Child, delta, owner);
    }

    protected abstract BtStatus Decorate(BehaviorTree_Node child, double delta, Node owner);
}