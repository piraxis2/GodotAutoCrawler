using System.Collections.Generic;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class TurnHelper : Node
{
    private readonly Dictionary<Node, ArticleBase> _articleMap = new();
    private readonly List<ITurnAffected<ArticleBase>> _turnAffectedArticleList = new();
    private BattleFieldTileMapLayer _battleFieldTileMapLayer;
    private ITurnAffected<ArticleBase> _currentTurnAffected;

    public override void _Ready()
    {
        _battleFieldTileMapLayer = GetNode<BattleFieldTileMapLayer>("TileMapLayer");
        foreach (Node child in GetChildren())
        {
            foreach (Node article in child.GetChildren())
            {
                if (article is ArticleBase articleNode)
                {
                    if (articleNode is ITurnAffected<ArticleBase> turnAffected)
                    {
                        _turnAffectedArticleList.Add(turnAffected);
                    }

                    _articleMap.Add(child, articleNode);
                }
            }
        }
        _currentTurnAffected = GetNextTurnAffected();
    }

    public override void _Process(double delta)
    {
        if (_currentTurnAffected == null)
        {
            // Game Over
            return;
        }
        
        Constants.BtStatus status = _currentTurnAffected.TurnPlay(delta);
        if(status is Constants.BtStatus.Success or Constants.BtStatus.Failure)
        {
            _currentTurnAffected = GetNextTurnAffected();
        }
    }
    
    private ITurnAffected<ArticleBase> GetNextTurnAffected()
    {
        if (_currentTurnAffected == null) return _turnAffectedArticleList[0];
        
        int currentIndex = _turnAffectedArticleList.IndexOf(_currentTurnAffected);
        currentIndex = (currentIndex + 1) % _turnAffectedArticleList.Count;
        return _turnAffectedArticleList[currentIndex];
    }
}
    