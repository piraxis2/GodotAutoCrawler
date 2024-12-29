using System;
using Godot;
using Godot.Collections;
using Array = Godot.Collections.Array;

namespace AutoCrawler.addons.behaviortree;

[GlobalClass, Tool]
public abstract partial class BT_Composite : BT_Node
{
    
    public Array<BT_Node> Children
    {
        get => _children;
        set
        {
            foreach (BT_Node child in _children)
            {
                RemoveChild(child);
            }
            _children = value;
            foreach (BT_Node child in _children)
            {
                AddChild(child);
            }
        }
    }
    private Array<BT_Node> _children = new Array<BT_Node>();

    public override void _Ready()
    {
        base._Ready();
        foreach (var node1 in GetChildren())
        {
            var child = (BT_Node)node1;
            if (child is BT_Node node)
            {
                Children.Add(node);
            }
        }
    }

    public override void OnChildEnteredTree(Node child)
    {
        base.OnChildEnteredTree(child);
        if (child is BT_Node node)
        {
            Children.Add(node);
        }
    }

    public BT_Node FindNode(string name)
    {
        if (name == Name)
            return this;
        foreach (BT_Node child in Children)
        {
            if (child.Name == name)
            {
                return child;
            }

            if (child is BT_Composite node)
            {
                BT_Node found = node.FindNode(name);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    public Array<BT_Node> FindNodeByType(Type type)
    {
        Array<BT_Node> foundNodes = new Array<BT_Node>();
        if (GetType() == type)
        {
            foundNodes.Add(this);
        }

        foreach (BT_Node child in Children)
        {
            if (child.GetType() == type)
            {
                foundNodes.Add(child);
            }

            if (child is BT_Composite compositeChild)
            {
                Array<BT_Node> foundChildren = compositeChild.FindNodeByType(type);
                if (foundChildren.Count > 0)
                {
                    foundNodes.AddRange(foundChildren);
                }
            }
        }

        return foundNodes;
    }
}