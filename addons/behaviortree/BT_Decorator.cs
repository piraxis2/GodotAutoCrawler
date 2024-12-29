namespace AutoCrawler.addons.behaviortree;


using System;
using Godot;

[GlobalClass, Tool]
public abstract partial class BT_Decorator : BT_Node
{
    public BT_Node Child { get; private set; }

    public override void OnChildEnteredTree(Node child)
    {
        base.OnChildEnteredTree(child);
        if (Child == null)
        {
            Child = child as BT_Node;
        }
        else
        {
            RemoveChild(child);
            throw new InvalidOperationException("BT_Decorator 노드에는 자식 노드를 하나만 추가할 수 있습니다.");
        }
    }

    public override BT_Status OnBehave(double delta, Node owner)
    {
        if (Child == null)
        {
            throw new InvalidOperationException("BT_Decorator 노드에 자식 노드가 설정되지 않았습니다.");
        }
        return Decorate(Child);
    }

    protected abstract BT_Status Decorate(BT_Node node);
}