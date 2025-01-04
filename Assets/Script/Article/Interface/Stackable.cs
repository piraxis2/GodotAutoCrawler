using Godot;

namespace AutoCrawler.Assets.Script.Article.Interface;

public interface IStackable
{
    public ArticleBase UpperArticleBase { get; set; }
}