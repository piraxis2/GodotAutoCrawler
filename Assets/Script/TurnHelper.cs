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
    private readonly List<ITurnAffectedArticle<ArticleBase>> _turnAffectedArticleList = new();
    private ITurnAffectedArticle<ArticleBase> _currentTurnArticle;

    private FxPlayer _fxPlayer;
    private FxPlayer FxPlayer => _fxPlayer ??= GlobalUtil.GetBattleFieldCoreNode<FxPlayer>(this);

    private ArticlesContainer _articlesContainer;
    private ArticlesContainer ArticlesContainer => _articlesContainer ??= GlobalUtil.GetBattleFieldCoreNode<ArticlesContainer>(this);

    private bool IsGameOver => _turnAffectedArticleList.Count <= 1 || _currentTurnArticle == null || ArticlesContainer.Articles["Opponent"].Count == 0 || ArticlesContainer.Articles["Ally"].Count == 0;
    private bool IsPaused => Speed == 0;

    [Export] public float Speed { get; private set; } = 1.0f;
    

    public override void _Ready()
    {
        foreach (var (key, value) in ArticlesContainer?.Articles!)
        {
            foreach (var articleBase in value)
            {
                if (articleBase is ITurnAffectedArticle<ArticleBase> turnAffectedArticle)
                {
                    _turnAffectedArticleList.Add(turnAffectedArticle);
                    articleBase.OnDead += (ArticleBase deadArticle) =>
                    {
                        _turnAffectedArticleList.Remove(turnAffectedArticle);
                    };
                }
            }
        }
       
        _turnAffectedArticleList.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        _currentTurnArticle = GetNextTurnArticle();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsPaused) return;
        
        if (IsGameOver)
        {
            // todo : 게임 오버 처리 
            GetTree().ReloadCurrentScene();
            return;
        }

        FxPlayer?.Tick(delta);

        if (_currentTurnArticle is ArticleBase { IsAlive: false })
        {
            _currentTurnArticle = GetNextTurnArticle();
            return;
        }
        
        BtStatus status = _currentTurnArticle.TurnPlay(delta * Speed);
        if (status is BtStatus.Success or BtStatus.Failure)
        {
            _currentTurnArticle = GetNextTurnArticle();
        }
    }
    
    private ITurnAffectedArticle<ArticleBase> GetNextTurnArticle()
    {
        if (_turnAffectedArticleList.Count == 0) return null;
        if (_currentTurnArticle == null ) return _turnAffectedArticleList[0];
        
        int currentIndex = _turnAffectedArticleList.IndexOf(_currentTurnArticle);
        currentIndex = (currentIndex + 1) % _turnAffectedArticleList.Count;
        return _turnAffectedArticleList[currentIndex];
    }
}
    