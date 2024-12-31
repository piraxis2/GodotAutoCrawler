using System;
using AutoCrawler.addons.behaviortree.node;
using Godot;
namespace AutoCrawler.addons.behaviortree;

[GlobalClass,Tool]
public partial class BehaviorTree : Node
{
    private BehaviorTree_Node _behaviorTreeRoot = null;
    public BehaviorTree_Node Root => _behaviorTreeRoot;
    public override void _Ready()
    {
        Connect("child_entered_tree", new Callable(this, nameof(OnChildEnteredTree)));
    }
    
    
    public void OnChildEnteredTree(Node child)
    {
        if (child is not BehaviorTree_Node)
        {
            RemoveChild(child);
            throw new InvalidOperationException("BT_Node 노드에는 BT_Node 노드만 추가할 수 있습니다.");
        }

        if (child is BehaviorTree_Node behaviorTreeNode)
        {
            if (_behaviorTreeRoot == null)
            {
                _behaviorTreeRoot = behaviorTreeNode;
            }
            behaviorTreeNode.Tree = this;
        }
    }

    public void Behave(double delta, Node owner)
    {
        _behaviorTreeRoot.Behave(delta, owner);
    }
    
}