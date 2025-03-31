using Godot;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;

namespace AutoCrawler.Assets.Script;

public partial class ArticlesContainer : Node
{

    private readonly Dictionary<string, List<ArticleBase>> _articles = new Dictionary<string, List<ArticleBase>>{
        {"Neutral", new List<ArticleBase>()},
        {"Opponent", new List<ArticleBase>()},
        {"Ally", new List<ArticleBase>()}
    };
    
    public Dictionary<string, List<ArticleBase>> Articles => _articles;

    public override void _Ready()
    {
        foreach (ArticleBase article in GetChildren().SelectMany(child => child.GetChildren().OfType<ArticleBase>()))
        {
            article.OnDead += () => { _articles[article.GetParent().Name].Remove(article); };
            _articles[article.GetParent().Name].Add(article);
        }
    }

    public List<ArticleBase> GetArticles(string article)
    {
        return _articles[article];
    }
    
    public List<ArticleBase> GetOpponentArticles(ArticleBase article)
    {
        return article.GetParent().Name == "Opponent" ? _articles["Ally"] : _articles["Opponent"];
    }
}