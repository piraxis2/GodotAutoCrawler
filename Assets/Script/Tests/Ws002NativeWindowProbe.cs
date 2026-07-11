#if TOOLS
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// 진단 전용. native pop-out 모드에서 managed window의 transient/좌표/OS 창 상태를 stdout에 찍는다.
/// 비-headless로 실행: godot --path . res://Assets/Script/Tests/ws002_native_window_probe.tscn
/// </summary>
public partial class Ws002NativeWindowProbe : Node
{
    public override void _Ready()
    {
        CallDeferred(nameof(Run));
    }

    private void Run()
    {
        Godot.Window root = GetTree().Root;
        GD.Print($"NPROBE display={DisplayServer.GetName()} screens={DisplayServer.GetScreenCount()} " +
                 $"root.GuiEmbedSubwindows={root.GuiEmbedSubwindows} root.Pos={root.Position} root.Size={root.Size}");

        var scene = GD.Load<PackedScene>("res://Assets/Scenes/workspace.tscn");
        Node workspace = scene.Instantiate();
        workspace.Set("_restoreLayoutOnReady", false);
        AddChild(workspace);

        var manager = workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");
        manager.ApplyPreset(WorkspaceLayoutPreset.Analysis);

        GD.Print($"NPROBE IsEmbeddedMode={manager.IsEmbeddedMode()}");
        GD.Print($"NPROBE DisplayServer window list = [{string.Join(", ", DisplayServer.GetWindowList())}]");

        foreach (string id in manager.Registry.Keys)
        {
            if (!manager.TryGetWindow(id, out WorkspaceWindow w)) continue;
            GD.Print($"NPROBE window[{id}] transient={w.Transient} transientToFocused={w.TransientToFocused} " +
                     $"visible={w.Visible} embedded={w.IsEmbedded()} pos={w.Position} size={w.Size} " +
                     $"decoPos={w.GetPositionWithDecorations()}");
        }

        // 종료는 CLI --quit-after 프레임에 맡긴다(창을 몇 초 띄워 taskbar 상태를 외부에서 조회).
    }
}
#endif
