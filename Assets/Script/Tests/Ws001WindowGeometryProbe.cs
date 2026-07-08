#if TOOLS
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// 진단 전용 probe. 실제 GUI 실행에서 managed window의 client rect와 decoration 포함 rect를 찍는다.
/// 헤드리스로는 decoration 크기를 알 수 없으므로 --headless 없이 실행해야 한다.
/// </summary>
public partial class Ws001WindowGeometryProbe : Node
{
    public override void _Ready()
    {
        CallDeferred(nameof(Probe));
    }

    private void Probe()
    {
        var scene = GD.Load<PackedScene>("res://Assets/Scenes/workspace.tscn");
        Node workspace = scene.Instantiate();
        AddChild(workspace);

        var manager = workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");

        Godot.Window root = GetTree().Root;
        GD.Print($"PROBE root.Position={root.Position} decoPos={root.GetPositionWithDecorations()} " +
                 $"size={root.Size} decoSize={root.GetSizeWithDecorations()}");

        int screenCount = DisplayServer.GetScreenCount();
        GD.Print($"PROBE screen_count={screenCount}");
        for (int i = 0; i < screenCount; i++)
        {
            GD.Print($"PROBE screen[{i}] pos={DisplayServer.ScreenGetPosition(i)} " +
                     $"size={DisplayServer.ScreenGetSize(i)} " +
                     $"usable={DisplayServer.ScreenGetUsableRect(i)} scale={DisplayServer.ScreenGetScale(i)}");
        }

        GD.Print($"PROBE work_area={manager.GetWorkArea()}");
        manager.UseLayoutStorePath("user://ws001_probe_layout.cfg");
        if (Godot.FileAccess.FileExists("user://ws001_probe_layout.cfg"))
            DirAccess.RemoveAbsolute("user://ws001_probe_layout.cfg");

        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        CallDeferred(nameof(ReportWindows), workspace, "after_outgame_preset");
    }

    private void ReportWindows(Node workspace, string phase)
    {
        var manager = workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");
        Rect2I workArea = manager.GetWorkArea();

        foreach (string id in manager.Registry.Keys)
        {
            if (!manager.TryGetWindow(id, out WorkspaceWindow window)) continue;
            Rect2I decoRect = WorkspaceWindowManager.GetDecorationRect(window);
            GD.Print($"PROBE[{phase}] window[{id}] visible={window.Visible} content_mode='{window.ContentMode}' " +
                     $"decoRect={decoRect} reachable={WorkspaceGeometry.IsReachable(decoRect, workArea)}");
        }

        if (phase == "after_outgame_preset")
        {
            // 화면 밖으로 강제로 밀어낸 뒤 gather가 회수하는지 본다.
            manager.TryGetWindow("world", out WorkspaceWindow world);
            world.Position = new Vector2I(-5000, -5000);
            manager.TryGetWindow("log", out WorkspaceWindow log);
            log.Position = new Vector2I(60000, 60000);
            GD.Print($"PROBE forced world/log off-screen, gathered={manager.GatherWindows()} windows");
            CallDeferred(nameof(ReportWindows), workspace, "after_gather");
            return;
        }

        if (phase == "after_gather")
        {
            // 현재 배치를 저장하고, 창을 흩뜨린 뒤 다시 로드해 복원되는지 본다.
            manager.TryGetWindow("world", out WorkspaceWindow world);
            Vector2I savedWorldPos = world.Position;
            GD.Print($"PROBE save={manager.SaveLayout()} worldPosBeforeReload={savedWorldPos}");
            world.Position = new Vector2I(100, 100);
            manager.HideWindow("situation");
            LayoutLoadStatus status = manager.LoadLayout();
            GD.Print($"PROBE reload status={status} worldPosAfterReload={world.Position} " +
                     $"situationVisible={manager.IsWindowVisible("situation")}");
            CallDeferred(nameof(ReportWindows), workspace, "after_reload");
            return;
        }

        DirAccess.RemoveAbsolute("user://ws001_probe_layout.cfg");
        GetTree().Quit(0);
    }
}
#endif
