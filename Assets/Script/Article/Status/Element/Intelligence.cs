using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public partial class Intelligence : StatusElement 
{
    public int Value { get; set; } = 1;
}