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
    [Export] double _speed = 1;

    public override void _Ready()
    {
        ArticlesContainer articlesContainer = GlobalUtil.GetBattleField(this)?.GetBattleFieldCoreNode<ArticlesContainer>();
        foreach (var (key, value) in articlesContainer?.Articles!)
        {
            foreach (var articleBase in value)
            {
                if (articleBase is ITurnAffected<ArticleBase> turnAffectedArticle)
                {
                    _turnAffectedArticleList.Add(turnAffectedArticle);
                }
            }
        }
       
        _turnAffectedArticleList.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        _currentTurnArticle = GetNextTurnArticle();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_currentTurnArticle == null)
        {
            // Game Over
            return;
        }

        Constants.BtStatus status = _currentTurnArticle.TurnPlay(delta * _speed);
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
    