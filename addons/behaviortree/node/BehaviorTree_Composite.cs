﻿using Godot;
using Godot.Collections;

namespace AutoCrawler.addons.behaviortree.node;

[GlobalClass, Tool]
public abstract partial class BehaviorTree_Composite : BehaviorTree_Node
{
    
    public Array<BehaviorTree_Node> Children
    {
        get => _children;
        set
        {
            foreach (BehaviorTree_Node child in _children)
            {
                RemoveChild(child);
            }
            _children = value;
            foreach (BehaviorTree_Node child in _children)
            {
                AddChild(child);
            }
        }
    }
    private Array<BehaviorTree_Node> _children = new Array<BehaviorTree_Node>();

    public override Array<BehaviorTree_Node> GetTreeChildren()
    {
        return Children;
    }

    public override void BehaviorChildEnteredTree(Node child)
    {
        if (child is BehaviorTree_Node node)
        {
            Children.Add(node);
        }
    }

    public override void BehaviorChildExitingTree(Node child)
    {
        if (child is BehaviorTree_Node btChild)
        {
            _children.Remove(btChild);
        }
    }

    public BehaviorTree_Node FindNode(string name)
    {
        if (name == Name)
            return this;
        foreach (BehaviorTree_Node child in Children)
        {
            if (child.Name == name)
            {
                return child;
            }

            if (child is BehaviorTree_Composite node)
            {
                BehaviorTree_Node found = node.FindNode(name);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    public Array<BehaviorTree_Node> FindNodeByType(System.Type type)
    {
        Array<BehaviorTree_Node> foundNodes = new Array<BehaviorTree_Node>();
        if (GetType() == type)
        {
            foundNodes.Add(this);
        }

        foreach (BehaviorTree_Node child in Children)
        {
            if (child.GetType() == type)
            {
                foundNodes.Add(child);
            }

            if (child is BehaviorTree_Composite compositeChild)
            {
                Array<BehaviorTree_Node> foundChildren = compositeChild.FindNodeByType(type);
                if (foundChildren.Count > 0)
                {
                    foundNodes.AddRange(foundChildren);
                }
            }
        }

        return foundNodes;
    }
}