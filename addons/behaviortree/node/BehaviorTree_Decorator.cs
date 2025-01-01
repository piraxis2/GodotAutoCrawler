using Godot;
using Godot.Collections;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Decorator : BehaviorTree_Node
{
    private BehaviorTree_Node Child { get; set; }

    public override Array<BehaviorTree_Node> GetTreeChildren()
    {
        if (Child != null)
        {
            return new Array<BehaviorTree_Node> { Child };
        }
        return new Array<BehaviorTree_Node>();
    }
    
    public override void BehaviorChildEnteredTree(Node child)
    {
        if (Child == null)
        {
            Child = child as BehaviorTree_Node;
        }
        else
        {
            RemoveChild(child);
            GD.PushWarning("BT_Decorator 노드에는 자식 노드를 하나만 추가할 수 있습니다.");
        }
    }

    public override void BehaviorChildExitingTree(Node child)
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
            throw new System.InvalidOperationException("BT_Decorator 노드에 자식 노드가 설정되지 않았습니다.");
        }
        return Decorate(Child);
    }

    protected abstract BtStatus Decorate(BehaviorTree_Node node);
}