#if TOOLS
using System.Collections.Generic;
using AutoCrawler.addons.behaviortree.node;
using Godot;

namespace AutoCrawler.addons.behaviortree.debugger;

/// <summary>
/// 에디터 프로세스 안에 실제로 열린 scene의 BehaviorTree Node 구조를 보여주는 로컬 구조 보기입니다.
/// 플레이 프로세스에서 넘어오는 원격 structure/tick payload 디버깅은 BehaviorTreeDebugGraphView가 담당합니다.
/// </summary>
[Tool]
public partial class DebuggerTree: Tree
{
    private DebuggerTree() {}
    public DebuggerTree(BehaviorTree tree)
    {
        SetColumns(4);
        if (GodotObject.IsInstanceValid(tree))
        {
            tree.OnUpdateTree += TreeUpdate;
            TreeUpdate(tree);
        }
        else
        {
            throw new System.InvalidOperationException("BehaviorTree is null or invalid");
        }
    }

    private void TreeUpdate(BehaviorTree tree)
    {
        if (!GodotObject.IsInstanceValid(tree))
        {
            GetNode<TabContainer>("..")?.RemoveChild(this);
            return;
        }
        if (!tree.IsInsideTree())
        {
            tree.OnUpdateTree -= TreeUpdate;
            GetNode<TabContainer>("..")?.RemoveChild(this);
            return;
        }
        Clear();
        TreeItem columnNameItem = CreateItem();
        
        columnNameItem.SetText(0,"Node Type");
        columnNameItem.SetText(1, "Node Name");
        columnNameItem.SetText(2, "Status");
        columnNameItem.SetText(3, "time");
        
        
        if (tree.Root == null)
        {
            TreeItem errorItem = columnNameItem.CreateChild();
            errorItem.SetText(0, "Warning");
            errorItem.SetText(1, "루트 노드가 지정되지 않았습니다.");
            return;
        }

        TreeItem rootItem = columnNameItem.CreateChild();
        rootItem.SetText(0, tree.Root.GetType().Name);
        rootItem.SetText(1, tree.Root.Name);
        MakeTree(tree.Root, rootItem);
    }
    
    private void MakeTree(BehaviorTree_Node node, TreeItem parent)
    {
        if (node == null) return;
        foreach (var child in node.TreeChildren)
        {
            if (child == null) continue;
            var item = parent.CreateChild();
            item.SetText(0, child.GetType().Name);
            item.SetText(1, child.Name);
            item.SetChecked(2, true);
            MakeTree(child, item);
        }
    }
}
#endif
