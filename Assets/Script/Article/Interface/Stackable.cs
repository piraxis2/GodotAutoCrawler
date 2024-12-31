using Godot;

namespace AutoCrawler.Assets.Script.Article.Interface;

public interface IStackable
{
    public Article UpperArticle { get; set; }
}