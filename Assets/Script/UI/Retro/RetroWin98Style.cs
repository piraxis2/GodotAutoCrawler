using Godot;

namespace AutoCrawler.Assets.Script.UI.Retro;

internal static class RetroWin98Style
{
    public const string ThemePath = "res://Assets/UI/Theme/RetroWin98Theme.tres";
    private static readonly StringName ThemeType = nameof(RetroVBoxPanel);

    public static Theme LoadTheme()
    {
        return GD.Load<Theme>(ThemePath);
    }

    public static void DrawBeveledPanel(Control control)
    {
        Rect2 rect = new(Vector2.Zero, control.Size);
        if (rect.Size.X <= 0 || rect.Size.Y <= 0) return;

        Color face = GetColor(control, "face", new Color(0.75f, 0.75f, 0.75f));
        Color topLeftOuter = GetColor(control, "top_left_outer", Colors.White);
        Color topLeftInner = GetColor(control, "top_left_inner", new Color(0.86f, 0.86f, 0.86f));
        Color bottomRightOuter = GetColor(control, "bottom_right_outer", new Color(0.12f, 0.12f, 0.12f));
        Color bottomRightInner = GetColor(control, "bottom_right_inner", new Color(0.45f, 0.45f, 0.45f));

        control.DrawRect(rect, face);

        float right = rect.Size.X - 1;
        float bottom = rect.Size.Y - 1;

        control.DrawLine(new Vector2(0, 0), new Vector2(right, 0), topLeftOuter);
        control.DrawLine(new Vector2(0, 0), new Vector2(0, bottom), topLeftOuter);
        control.DrawLine(new Vector2(1, 1), new Vector2(right - 1, 1), topLeftInner);
        control.DrawLine(new Vector2(1, 1), new Vector2(1, bottom - 1), topLeftInner);

        control.DrawLine(new Vector2(0, bottom), new Vector2(right, bottom), bottomRightOuter);
        control.DrawLine(new Vector2(right, 0), new Vector2(right, bottom), bottomRightOuter);
        control.DrawLine(new Vector2(1, bottom - 1), new Vector2(right - 1, bottom - 1), bottomRightInner);
        control.DrawLine(new Vector2(right - 1, 1), new Vector2(right - 1, bottom - 1), bottomRightInner);
    }

    private static Color GetColor(Control control, string name, Color fallback)
    {
        var colorName = new StringName(name);
        return control.HasThemeColor(colorName, ThemeType)
            ? control.GetThemeColor(colorName, ThemeType)
            : fallback;
    }
}
