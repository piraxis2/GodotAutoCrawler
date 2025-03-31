using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public abstract partial class StatusElement : Resource
{
    protected ArticleBase Owner;
    public void Init(ArticleBase articleBase)
    {
        Owner = articleBase;
        OnInit(articleBase);
    }
    protected virtual void OnInit(ArticleBase articleBase) {}

        
}