using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class TurnHelper : Node
{
    private readonly List<ITurnAffectedArticle<ArticleBase>> _turnAffectedArticleList = new();
    private ITurnAffectedArticle<ArticleBase> _currentTurnArticle;

    private FxPlayer _fxPlayer;
    private FxPlayer FxPlayer => _fxPlayer ??= BattleFieldScene.BattleField.FxPlayer;
    private readonly RandomNumberGenerator _combatRng = new();
    
    [Export] private ArticlesContainer _articlesContainer;
    [Export] private long _combatSeed = 1;

    private bool IsGameOver => _turnAffectedArticleList.Count <= 1 || _currentTurnArticle == null || _articlesContainer.Articles["Opponent"].Count == 0 || _articlesContainer.Articles["Ally"].Count == 0;
    private bool IsPaused => Speed == 0;

    [Export] public float Speed { get; private set; } = 1.0f;
    public long CombatSeed => _combatSeed;
    
    public override void _Ready()
    {
        ResetCombatRng(_combatSeed);

        var articles = _articlesContainer?.Articles;
        if (articles == null)
            return;
        
        foreach (var (key, value) in articles)
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
       
        SortTurnAffectedArticles();

        AdvanceToNextTurn();
    }

    // 턴 순서 정규 키: Priority 오름차순 -> 스폰 순번 오름차순 (ADR-017)
    private void SortTurnAffectedArticles()
    {
        var orderedArticles = _turnAffectedArticleList
            .OrderBy(article => article.Priority)
            .ThenBy(article => article.SpawnIndex)
            .ToList();
        _turnAffectedArticleList.Clear();
        _turnAffectedArticleList.AddRange(orderedArticles);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsPaused) return;
        
        FxPlayer?.Tick(delta);
        
        if (IsGameOver)
        {
            // todo : 게임 오버 처리 
            return;
        }


        if (_currentTurnArticle is ArticleBase { IsAlive: false })
        {
            AdvanceToNextTurn();
            return;
        }
        
        BtStatus status = _currentTurnArticle.TurnPlay(delta * Speed);
        if (status is BtStatus.Success or BtStatus.Failure)
        {
            AdvanceToNextTurn();
        }
    }

    public void ResetCombatRng(long seed)
    {
        _combatSeed = seed;
        _combatRng.Seed = unchecked((ulong)seed);
    }

    public double CombatRandRange(double min, double max)
    {
        return _combatRng.RandfRange((float)min, (float)max);
    }

    public int CombatRandRange(int min, int max)
    {
        return _combatRng.RandiRange(min, max);
    }

    private void AdvanceToNextTurn()
    {
        _currentTurnArticle = GetNextTurnArticle();
        if (_articlesContainer == null || IsGameOver) return;
        _currentTurnArticle?.ApplyTurnStartEffects();
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