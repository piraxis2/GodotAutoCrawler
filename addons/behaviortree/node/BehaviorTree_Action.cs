using System;
using Godot;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Action : BehaviorTree_Node
{
    protected override BtStatus OnBehave(double delta, Node owner)
    {
        return PerformAction(delta, owner);
    }

    protected abstract BtStatus PerformAction(double delta, Node owner);

    public override void OnChildEnteredTree(Node child)
    {
        base.OnChildEnteredTree(child);
        RemoveChild(child);
        throw new InvalidOperationException("BT_Action 노드에는 자식 노드를 추가할 수 없습니다.");
    }
}