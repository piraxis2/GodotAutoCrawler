using System.Collections.Generic;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class TurnHelper : Node
{
    private static TurnHelper _instance = null;
    public static TurnHelper Instance => _instance ??= GlobalUtil.Singleton.GetNode<TurnHelper>("/root/GlobalUtil");
    
    private readonly Dictionary<Node, ArticleBase> _articleMap = new();
    private readonly List<ITurnAffected<ArticleBase>> _turnAffectedArticleList = new();
    private ITurnAffected<ArticleBase> _currentTurnArticle;

    public override void _Ready()
    {
        Node articleContainer = GetNodeOrNull<Node>("Articles");
        if (articleContainer != null)
        {
            foreach (Node child in articleContainer.GetChildren())
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
        }
        else
        {
            throw new System.NullReferenceException("Articles node is not found");
        }

        _currentTurnArticle = GetNextTurnArticle();
    }

    public override void _Process(double delta)
    {
        if (_currentTurnArticle == null)
        {
            // Game Over
            return;
        }
        
        Constants.BtStatus status = _currentTurnArticle.TurnPlay(delta);
        if(status is Constants.BtStatus.Success or Constants.BtStatus.Failure)
        {
            _currentTurnArticle = GetNextTurnArticle();
        }
    }
    
    private ITurnAffected<ArticleBase> GetNextTurnArticle()
    {
        if (_currentTurnArticle == null) return _turnAffectedArticleList[0];
        
        int currentIndex = _turnAffectedArticleList.IndexOf(_currentTurnArticle);
        currentIndex = (currentIndex + 1) % _turnAffectedArticleList.Count;
        return _turnAffectedArticleList[currentIndex];
    }
}
    