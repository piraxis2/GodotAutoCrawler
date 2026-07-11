using Godot;

namespace AutoCrawler.Assets.Script.UI.Retro;

public partial class RetroVBoxPanel : VBoxContainer
{
    public override void _Ready()
    {
        Theme ??= RetroWin98Style.LoadTheme();
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized || what == NotificationThemeChanged) QueueRedraw();
    }

    public override void _Draw()
    {
        RetroWin98Style.DrawBeveledPanel(this);
    }
}
