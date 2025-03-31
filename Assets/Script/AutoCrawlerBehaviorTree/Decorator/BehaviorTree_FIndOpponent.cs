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
        _tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(this);
    }
    
    protected override Constants.BtStatus Decorate(BehaviorTree_Node child, double delta, Node owner)
    {
        return !IsOpponentInAttackRange(owner as CharacterArticle) ? child.Behave(delta, owner) : Constants.BtStatus.Failure;
    }

    private bool IsOpponentInAttackRange(CharacterArticle ownerCharacterArticle)
    {
        if (ownerCharacterArticle == null)
        {
            return false;
        }

        foreach (var position in ownerCharacterArticle.CalculatedAttackRange)
        {
            var article = _tileMapLayer.GetArticle(position);
            if (article != null)
            {
                if (ownerCharacterArticle.IsOpponent(article))
                {
                    return true;        
                }
            }
        }
        return false;
    }
}