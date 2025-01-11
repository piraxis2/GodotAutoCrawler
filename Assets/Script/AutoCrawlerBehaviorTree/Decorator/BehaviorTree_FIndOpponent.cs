using System.Collections.Generic;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Util;
using Godot;
using BehaviorTree_Decorator = AutoCrawler.addons.behaviortree.node.BehaviorTree_Decorator;
using BehaviorTree_Node = AutoCrawler.addons.behaviortree.node.BehaviorTree_Node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Decorator;

[GlobalClass, Tool]
public partial class BehaviorTree_FIndOpponent: BehaviorTree_Decorator
{
    BattleFieldTileMapLayer _tileMapLayer;
    protected override void OnReady()
    {
        _tileMapLayer = GlobalUtil.GetBattleField(this)?.GetBattleFieldCoreNode<BattleFieldTileMapLayer>();
    }
    
    protected override Constants.BtStatus Decorate(BehaviorTree_Node child, double delta, Node owner)
    {
        if (owner is CharacterArticle characterArticle)
        {
            foreach (var attackRangePosition in characterArticle.AttackRangePositions)
            {
                // _tileMapLayer.GetArticle(attackRangePosition)
            }
        }
        
        return child.Behave(delta, owner);
    }
}