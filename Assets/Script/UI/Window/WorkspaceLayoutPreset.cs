using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

/// <summary>World 창의 content mode다. Godot <c>Window.ModeEnum</c>과 다른 개념이다(D4).</summary>
public static class WorldContentMode
{
    public const string Hub = "hub";
    public const string Battle = "battle";
    public const string Replay = "replay";
}

/// <summary>
/// preset이 창 하나에 적용할 값이다. <see cref="Offset"/>은 절대 좌표가 아니라
/// **대상 screen work area 원점 기준 오프셋**이다. screen 원점이 (0, 0)이 아니기 때문이다(Step 1 실측).
/// </summary>
public readonly struct WindowLayout
{
    public WindowLayout(bool visible, Vector2I offset, Vector2I size, string contentMode = null)
    {
        Visible = visible;
        Offset = offset;
        Size = size;
        ContentMode = contentMode;
    }

    public bool Visible { get; }
    public Vector2I Offset { get; }
    public Vector2I Size { get; }

    /// <summary>World 창에만 의미가 있다. null이면 content mode를 건드리지 않는다.</summary>
    public string ContentMode { get; }
}

/// <summary>
/// D3. preset은 고정 화면이 아니라 창들의 {visible, position, size, content_mode}를 적용하는 명령이다.
/// W0에서는 "동작 확인 가능한 임시 배치"만 둔다(OD4). 좌표 값보다 적용/저장/복원 동작이 중요하다.
/// </summary>
public static class WorkspaceLayoutPreset
{
    public const string Outgame = "outgame";
    public const string Battle = "battle";
    public const string Analysis = "analysis";

    private static readonly Dictionary<string, Dictionary<string, WindowLayout>> Presets = new()
    {
        [Outgame] = new Dictionary<string, WindowLayout>
        {
            ["world"] = new(true, new Vector2I(20, 20), new Vector2I(640, 480), WorldContentMode.Hub),
            ["situation"] = new(true, new Vector2I(680, 20), new Vector2I(360, 240)),
            ["weekly_action"] = new(true, new Vector2I(680, 280), new Vector2I(360, 280)),
            ["calendar"] = new(true, new Vector2I(20, 520), new Vector2I(640, 160)),
            ["log"] = new(true, new Vector2I(1060, 20), new Vector2I(360, 560))
        },
        [Battle] = new Dictionary<string, WindowLayout>
        {
            ["world"] = new(true, new Vector2I(20, 20), new Vector2I(1020, 660), WorldContentMode.Battle),
            ["situation"] = new(false, new Vector2I(680, 20), new Vector2I(360, 240)),
            ["weekly_action"] = new(false, new Vector2I(680, 280), new Vector2I(360, 280)),
            ["calendar"] = new(false, new Vector2I(20, 520), new Vector2I(640, 160)),
            ["log"] = new(true, new Vector2I(1060, 20), new Vector2I(360, 660))
        },
        [Analysis] = new Dictionary<string, WindowLayout>
        {
            ["world"] = new(true, new Vector2I(20, 20), new Vector2I(700, 460), WorldContentMode.Replay),
            ["situation"] = new(false, new Vector2I(680, 20), new Vector2I(360, 240)),
            ["weekly_action"] = new(false, new Vector2I(680, 280), new Vector2I(360, 280)),
            ["calendar"] = new(true, new Vector2I(20, 500), new Vector2I(700, 180)),
            ["log"] = new(true, new Vector2I(740, 20), new Vector2I(400, 660))
        }
    };

    public static IEnumerable<string> PresetIds => Presets.Keys;

    /// <summary>unknown preset id는 fail-closed다(false, layouts = null).</summary>
    public static bool TryGetPreset(string presetId, out IReadOnlyDictionary<string, WindowLayout> layouts)
    {
        layouts = null;
        if (string.IsNullOrEmpty(presetId)) return false;
        if (!Presets.TryGetValue(presetId, out Dictionary<string, WindowLayout> found)) return false;

        layouts = found;
        return true;
    }
}
