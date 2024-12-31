using System;
using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Decorator : BehaviorTree_Node
{
    public BehaviorTree_Node Child { get; private set; }

    public override void OnChildEnteredTree(Node child)
    {
        base.OnChildEnteredTree(child);
        if (Child == null)
        {
            Child = child as BehaviorTree_Node;
        }
        else
        {
            RemoveChild(child);
            throw new InvalidOperationException("BT_Decorator 노드에는 자식 노드를 하나만 추가할 수 있습니다.");
        }
    }

    public override void OnChildExitingTree(Node child)
    {
        if (child is BehaviorTree_Node)
        {
            Child = null;
        }
    }

    protected override BtStatus OnBehave(double delta, Node owner)
    {
        if (Child == null)
        {
            throw new InvalidOperationException("BT_Decorator 노드에 자식 노드가 설정되지 않았습니다.");
        }
        return Decorate(Child);
    }

    protected abstract BtStatus Decorate(BehaviorTree_Node node);
}