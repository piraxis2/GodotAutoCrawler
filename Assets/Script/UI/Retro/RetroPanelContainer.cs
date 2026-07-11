using Godot;

namespace AutoCrawler.Assets.Script.UI.Retro;

public partial class RetroPanelContainer : PanelContainer
{
    public override void _Ready()
    {
        Theme ??= RetroWin98Style.LoadTheme();
    }
}
