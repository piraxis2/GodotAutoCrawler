using Godot;
using Godot.Collections;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Action : BehaviorTree_Node
{
    public override Array<BehaviorTree_Node> GetTreeChildren()
    {
        return new Array<BehaviorTree_Node>();
    }

    protected override BtStatus OnBehave(double delta, Node owner)
    {
        return PerformAction(delta, owner);
    }

    protected abstract BtStatus PerformAction(double delta, Node owner);

    public override void BehaviorChildEnteredTree(Node child)
    {
        RemoveChild(child);
        GD.PushWarning("BT_Action 노드에는 자식 노드를 추가할 수 없습니다.");
    }
}