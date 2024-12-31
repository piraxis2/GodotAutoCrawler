using Godot;
using BehaviorTree_Decorator = AutoCrawler.addons.behaviortree.node.BehaviorTree_Decorator;
using BehaviorTree_Node = AutoCrawler.addons.behaviortree.node.BehaviorTree_Node;

namespace AutoCrawler.Assets.Script.Article.BT.Decorator;

[GlobalClass, Tool]
public partial class BehaviorTree_AutoCrwaler_FIndOpponent: BehaviorTree_Decorator 
{
    protected override BtStatus Decorate(BehaviorTree_Node node)
    {
        return BtStatus.Failure;
    }
}