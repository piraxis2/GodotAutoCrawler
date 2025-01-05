using Godot;

namespace AutoCrawler.Assets.Script.Util;

public partial class GlobalUtil : Node
{
    public static GlobalUtil Singleton { get; private set; }

    public override void _EnterTree()
    {
        Singleton ??= this;
    }
}