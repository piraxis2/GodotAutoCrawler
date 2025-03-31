using AutoCrawler.Assets.Script.Article.Status.Element;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public interface IAffectedUntilTheEnd
{
    
    public void ApplyAffect<TStatus>(TStatus statusElement, ArticleStatus recipient)where TStatus : StatusElement;
    public void UnapplyAffect<TStatus>(TStatus statusElement, ArticleStatus recipient)where TStatus : StatusElement;
    
}