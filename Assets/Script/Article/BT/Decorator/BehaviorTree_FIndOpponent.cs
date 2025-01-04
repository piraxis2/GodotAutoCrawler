using AutoCrawler.addons.behaviortree;
using Godot;
using BehaviorTree_Decorator = AutoCrawler.addons.behaviortree.node.BehaviorTree_Decorator;
using BehaviorTree_Node = AutoCrawler.addons.behaviortree.node.BehaviorTree_Node;

namespace AutoCrawler.Assets.Script.Article.BT.Decorator;

[GlobalClass, Tool]
public partial class BehaviorTree_FIndOpponent: BehaviorTree_Decorator
{
    private BattleFieldTileMapLayer _battleFieldTileMapLayer; 
    protected override void OnReady()
    {

        
    }

    protected override Constants.BtStatus Decorate(BehaviorTree_Node child, double delta, Node owner)
    {
        
        
        
        return child.Behave(delta, owner);
    }
}