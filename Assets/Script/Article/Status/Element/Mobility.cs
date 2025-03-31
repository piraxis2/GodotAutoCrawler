using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public partial class Mobility : StatusElement
{
    [Export]
    public int Value { get; set; } = 1;

    protected override void OnInit(ArticleBase articleBase)
    {
    }
}