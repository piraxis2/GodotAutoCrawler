using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public partial class Mobility : StatusElement
{
    [Export]
    public int Value { get; set; } = 1;
    public override void Init(ArticleBase articleBase)
    {
    }
}