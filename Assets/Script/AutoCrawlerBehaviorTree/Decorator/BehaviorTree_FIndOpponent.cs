using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using Godot;
using BehaviorTree_Decorator = AutoCrawler.addons.behaviortree.node.BehaviorTree_Decorator;
using BehaviorTree_Node = AutoCrawler.addons.behaviortree.node.BehaviorTree_Node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Decorator;

[GlobalClass, Tool]
public partial class BehaviorTree_FIndOpponent: BehaviorTree_Decorator
{
    private BattleFieldTileMapLayer _battleFieldTileMapLayer; 
    protected override void OnReady()
    {
    }

    protected override Constants.BtStatus Decorate(BehaviorTree_Node child, double delta, Node owner)
    {
        if (owner is CharacterArticle characterArticle)
        {
            
                
        }
        
        
        return child.Behave(delta, owner);
    }
}