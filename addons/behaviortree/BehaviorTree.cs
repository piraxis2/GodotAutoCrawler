using System;
using System.Collections.Generic;
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
    
    public Blackboard Blackboard { get; private set; }
    
    public string ArticleName => GetParent()?.Name;
    private bool _isUpdateRequested = false;

    public override void _Ready()
    {
        Blackboard = new Blackboard();
        
        ChildOrderChanged += OnChildOrderChanged; 
        SetTree(Root);
    }

    private void OnChildOrderChanged()
    {
        OnUpdate();
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

    public BtStatus Behave(double delta, Node owner)
    {
        return Root?.Behave(delta, owner) ?? BtStatus.Failure;
    }

    public void UpdateRequest()
    {
        if (_isUpdateRequested) return;
        _isUpdateRequested = true;
        CallDeferred(nameof(OnUpdate));
    }
    private void OnUpdate()
    {
        _isUpdateRequested = false;
        if (!IsInsideTree()) return;
        SetTree(Root);
        EmitSignal("OnUpdateTree", this);
        GD.Print($"OnUpdateTree : {Name}");
    }

    public void OnLeafNodeExecuted(BehaviorTree_Node leafNode, BtStatus status)
    {
        
    }
    
    public string GenerateMermaidGraph()
    {
        if (Root == null) return string.Empty;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("graph TD");
        GenerateMermaidGraph(Root, sb);
        DisplayServer.Singleton.ClipboardSet(sb.ToString());
        return sb.ToString();
    }

    private void GenerateMermaidGraph(BehaviorTree_Node node, StringBuilder sb)
    {
        foreach (var child in node.TreeChildren)
        {
            sb.AppendLine($"{node.Name} --> {child.Name}");
            GenerateMermaidGraph(child, sb);
        }
    }
    
    public List<BehaviorTree_Node> FindNodeByType(Type type)
    {
        return Root.FindNodeByType(type);
    }
    
    
}