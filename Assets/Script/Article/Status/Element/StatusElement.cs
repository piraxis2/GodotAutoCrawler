using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public abstract partial class StatusElement : Resource
{
    public abstract void Init(ArticleBase articleBase);
    // public abstract void Upgrade();
}