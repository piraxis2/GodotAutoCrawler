#if TOOLS
using AutoCrawler.addons.behaviortree.node;
using Godot;

namespace AutoCrawler.addons.behaviortree.debugger;

[Tool]
public partial class DebuggerTree: Tree
{
    private DebuggerTree() {}
    public DebuggerTree(BehaviorTree tree)
    {
        SetColumns(4);
        if (tree != null)
        {
            tree.OnUpdateTree += TreeUpdate;
            TreeUpdate(tree);
        }
        else
        {
            throw new System.InvalidOperationException("BehaviorTree is null");
        }
    }

    private void TreeUpdate(BehaviorTree tree)
    {
        Clear();
        TreeItem columnNameItem = CreateItem();
        columnNameItem.SetText(0,"Node Type");
        columnNameItem.SetText(1, "Node Name");
        columnNameItem.SetText(2, "Status");
        columnNameItem.SetText(3, "time");
        
        
        var rootItem = columnNameItem.CreateChild();
        rootItem.SetText(0, tree.Root.GetType().Name);
        rootItem.SetText(1, tree.Root.Name);
        MakeTree(tree.Root, rootItem);
    }
    
    private void MakeTree(BehaviorTree_Node node, TreeItem parent)
    {
        var Children = node.GetTreeChildren();
        foreach (var child in Children)
        {
            var item = parent.CreateChild();
            item.SetText(0, child.GetType().Name);
            item.SetText(1, child.Name);
            MakeTree(child, item);
        }
    }

}
#endif