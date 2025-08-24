using AutoCrawler.addons.behaviortree;
using Godot;

using BehaviorTree_Decorator = AutoCrawler.addons.behaviortree.node.BehaviorTree_Decorator;
using BehaviorTree_Node = AutoCrawler.addons.behaviortree.node.BehaviorTree_Node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Decorator;

public partial class BehaviorTree_HasMultiOpponent : BehaviorTree_Decorator
{
    [Export] private int _targetCount = 2;
    private BehaviorTree_Decorator _behaviorTreeImplementation;
    protected override bool IsValid(BehaviorTree_Node child, double delta, Node owner)
    {
        return false;
    }
    
}