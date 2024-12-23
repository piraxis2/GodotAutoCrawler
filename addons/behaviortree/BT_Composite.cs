using System;
using Godot;
namespace AutoCrawler.addons.behaviortree;

[GlobalClass, Tool]
public abstract partial class BT_Composite : BT_Node
{
    protected System.Collections.Generic.List<BT_Node> Children = new System.Collections.Generic.List<BT_Node>();

    public void AddNode(BT_Node node)
    {
        Children.Add(node);
    }

    public BT_Node FindNode(string name)
    {
        if (name == Name)
            return this;
        foreach (BT_Node child in Children)
        {
            if (child.Name == name)
                return child;

            if (child is BT_Composite node)
            {
                BT_Node found = node.FindNode(name);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    public System.Collections.Generic.List<BT_Node> FindNodeByType(Type type)
    {
        System.Collections.Generic.List<BT_Node> foundNodes = new System.Collections.Generic.List<BT_Node>();
        if (GetType() == type)
            foundNodes.Add(this);
        foreach (BT_Node child in Children)
        {
            if (child.GetType() == type)
                foundNodes.Add(child);

            if (child is BT_Composite compositeChild)
            {
                System.Collections.Generic.List<BT_Node> foundChildren = compositeChild.FindNodeByType(type);
                if (foundChildren.Count > 0)
                    foundNodes.AddRange(foundChildren);
            }
        }

        return foundNodes;
    }
}