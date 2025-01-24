using AutoCrawler.Assets.Script.Article.Status.Element;
namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public interface IAffectedImmediately
{
    public void ApplyImmediately<TStatus>(TStatus statusElement, ArticleStatus recipient)where TStatus : StatusElement;
}