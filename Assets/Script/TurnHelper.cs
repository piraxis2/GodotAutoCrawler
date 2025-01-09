using System.Collections.Generic;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Util;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script;

public partial class TurnHelper : Node
{
    private readonly List<ITurnAffected<ArticleBase>> _turnAffectedArticleList = new();
    private ITurnAffected<ArticleBase> _currentTurnArticle;

    public override void _Ready()
    {
        Node articleContainer = GetNode("../Articles");

        var articles = (Array<Node>)articleContainer.Call("getAllArticles");
        foreach (Node article in articles)
        {
            ArticleBase articleBase = (ArticleBase)article;
            if (articleBase is ITurnAffected<ArticleBase> turnAffectedArticle)
            {
                _turnAffectedArticleList.Add(turnAffectedArticle);
            }
        }
       
        _turnAffectedArticleList.Sort((a, b) => a.Priority.CompareTo(b.Priority));

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
        if (_turnAffectedArticleList.Count == 0) return null;
        if (_currentTurnArticle == null ) return _turnAffectedArticleList[0];
        
        int currentIndex = _turnAffectedArticleList.IndexOf(_currentTurnArticle);
        currentIndex = (currentIndex + 1) % _turnAffectedArticleList.Count;
        return _turnAffectedArticleList[currentIndex];
    }
}
    