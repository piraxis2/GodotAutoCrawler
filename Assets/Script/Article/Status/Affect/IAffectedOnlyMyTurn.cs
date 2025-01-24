using AutoCrawler.Assets.Script.Article.Status.Element;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public interface IAffectedOnlyMyTurn
{
    public void ApplyOnlyMyTurn<TStatus>(TStatus statusElement, ArticleStatus recipient)where TStatus : StatusElement;
}