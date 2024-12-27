namespace AutoCrawler.addons.behaviortree;


using System;
using Godot;

[GlobalClass, Tool]
public abstract partial class BT_Decorator : BT_Node
{
    private BT_Node _child;

    public void SetChild(BT_Node child)
    {
        if (_child != null)
        {
            throw new InvalidOperationException("BT_Decorator 노드는 하나의 자식 노드만 가질 수 있습니다.");
        }

        _child = child;
    }

    public override BT_Status OnBehave(double delta, Node owner)
    {
        if (_child == null)
        {
            throw new InvalidOperationException("BT_Decorator 노드에 자식 노드가 설정되지 않았습니다.");
        }

        return Decorate(_child);
    }

    protected abstract BT_Status Decorate(BT_Node status);
}