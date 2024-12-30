using System;

namespace AutoCrawler.addons.behaviortree;
using Godot;

[GlobalClass, Tool]
public abstract partial class BT_Action : BT_Node
{
    protected override BT_Status OnBehave(double delta, Node owner)
    {
        return PerformAction(delta, owner);
    }

    protected abstract BT_Status PerformAction(double delta, Node owner);

    public override void OnChildEnteredTree(Node child)
    {
        base.OnChildEnteredTree(child);
        RemoveChild(child);
        throw new InvalidOperationException("BT_Action 노드에는 자식 노드를 추가할 수 없습니다.");
    }
}