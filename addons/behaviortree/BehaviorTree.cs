using System.Linq;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using Godot;
using Godot.Collections;

namespace AutoCrawler.addons.behaviortree;

[GlobalClass,Tool]
public partial class BehaviorTree : Node
{
    public BehaviorTree_Node Root { get; private set; }

    [Signal]
    public delegate void OnUpdateTreeEventHandler(BehaviorTree tree);
    
    public string ArticleName => GetParent()?.Name;

    public override void _Ready()
    {
        Connect("child_entered_tree", new Callable(this, nameof(OnSetRoot)));
        var children = GetChildren(true);
        if (children.Count > 0)
        {
            foreach (BehaviorTree_Node child in children.OfType<BehaviorTree_Node>())
            {
                child.Tree = this;
            }
        }
    }
    
    public void OnSetRoot(Node child)
    {
        if (child is BehaviorTree_Node behaviorTreeNode)
        {
            if (Root == null)
            {
                Root = behaviorTreeNode;
            }
            else
            {
                RemoveChild(child);
                GD.PushWarning("BehaviorTree 노드에는 BehaviorTree_Node 노드를 하나만 추가할 수 있습니다.");
                return;
            }
            behaviorTreeNode.Tree = this;
        }
        else
        {
            RemoveChild(child);
            GD.PushWarning("BehaviorTree 노드에는 BehaviorTree_Node 노드만 추가할 수 있습니다.");
        }
    }

    public void Behave(double delta, Node owner)
    {
        Root.Behave(delta, owner);
    }
    
    public void OnUpdate()
    {
        GD.Print($"OnUpdateTree : {Name}");
        EmitSignal("OnUpdateTree", this);
    }

}