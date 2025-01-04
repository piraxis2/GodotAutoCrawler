using System.Linq;
using System.Text;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using Godot;
using Godot.Collections;

namespace AutoCrawler.addons.behaviortree;

[GlobalClass,Tool]
public partial class BehaviorTree : Node
{
    public BehaviorTree_Node Root => GetChildCount() > 0 ? GetChild(0) as BehaviorTree_Node : null;

    [Signal]
    public delegate void OnUpdateTreeEventHandler(BehaviorTree tree);
    
    public string ArticleName => GetParent()?.Name;

    public override void _Ready()
    {
        ChildOrderChanged += OnChildOrderChanged; 
        SetTree(Root);
    }

    private void OnChildOrderChanged()
    {
        if (IsInsideTree())
        {
            SetTree(Root);
        }
    }

    private void SetTree(BehaviorTree_Node node)
    {
        if (node == null) return;
        
        node.Tree = this;
        foreach (var child in node.GetChildren())
        {
            if (child is BehaviorTree_Node behaviorTreeNode)
            {
                SetTree(behaviorTreeNode);
            }
        }
    }

    public Constants.BtStatus Behave(double delta, Node owner)
    {
        return Root.Behave(delta, owner);
    }
    
    public void OnUpdate()
    {
        GD.Print($"OnUpdateTree : {Name}");
        EmitSignal("OnUpdateTree", this);
    }

    public void OnLeafNodeExecuted(BehaviorTree_Node leafNode, Constants.BtStatus status)
    {
        
    }
    
    public string GenerateMermaidGraph()
    {
        if (Root == null) return string.Empty;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("graph TD");
        GenerateMermaidGraph(Root, sb);

        return sb.ToString();
    }

    private void GenerateMermaidGraph(BehaviorTree_Node node, StringBuilder sb)
    {
        foreach (var child in node.GetTreeChildren())
        {
            sb.AppendLine($"{node.Name} --> {child.Name}");
            GenerateMermaidGraph(child, sb);
        }
    }
}