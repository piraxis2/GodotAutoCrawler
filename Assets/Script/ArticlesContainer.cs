using Godot;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;

namespace AutoCrawler.Assets.Script;

public partial class ArticlesContainer : Node 
{
    public Dictionary<string, List<ArticleBase>> Articles { get; } = new()
    {
        {"Neutral", [] },
        {"Opponent", [] },
        {"Ally", [] }
    };

    public override void _Ready()
    {
        int spawnIndex = 0;
        foreach (ArticleBase article in GetChildren().SelectMany(child => child.GetChildren().OfType<ArticleBase>()))
        {
            article.SpawnIndex = spawnIndex++;
            article.OnDead += deadArticle => { Articles[deadArticle.GetParent().Name].Remove(deadArticle); };
            Articles[article.GetParent().Name].Add(article);
        }
    }

    public List<ArticleBase> GetOpponentArticles(ArticleBase article)
    {
        return article.GetParent().Name == "Opponent" ? Articles["Ally"] : Articles["Opponent"];
    }

    // 턴 참가자(ITurnAffectedArticle) 조회 공개 API(BS-001 Step 2). BattleSession이 참가자 준비/Configure에 쓴다.
    public IEnumerable<ITurnAffectedArticle<ArticleBase>> GetTurnParticipants()
    {
        return Articles.Values
            .SelectMany(list => list)
            .OfType<ITurnAffectedArticle<ArticleBase>>();
    }
}