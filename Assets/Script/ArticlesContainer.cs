using Godot;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;

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
        foreach (ArticleBase article in GetChildren().SelectMany(child => child.GetChildren().OfType<ArticleBase>()))
        {
            article.OnDead += deadArticle => { Articles[deadArticle.GetParent().Name].Remove(deadArticle); };
            Articles[article.GetParent().Name].Add(article);
        }
    }

    public List<ArticleBase> GetOpponentArticles(ArticleBase article)
    {
        return article.GetParent().Name == "Opponent" ? Articles["Ally"] : Articles["Opponent"];
    }
}