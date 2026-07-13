#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.Assets.Script.Battle;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BS-002 Step 2: workspace 로비 버튼 wiring(headless). 실제 전투 표시/렌더는 GUI smoke 전용(Finding 2)이라
// 여기서는 버튼 Pressed → battle preset → entry → session Running + mount 배선까지만 관측한다.
public partial class Bs002Step2WorkspaceTest : Node
{
    private const string WorkspacePath = "res://Assets/Scenes/workspace.tscn";

    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BS-002 Step2] Running workspace lobby-button wiring tests...");
        try
        {
            await TestButtonEntersBattle();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BS-002 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[BS-002 Step2] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[BS-002 Step2] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private async Task TestButtonEntersBattle()
    {
        var workspace = GD.Load<PackedScene>(WorkspacePath).Instantiate();
        workspace.Set("_restoreLayoutOnReady", false);
        AddChild(workspace);
        await Frames(1);

        var manager = workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");
        var entry = workspace.GetNode<LobbyBattleEntry>("LobbyBattleEntry");
        var button = workspace.GetNode<Button>("Windows/WorldWindow/Panels/HubPanels/EnterBattleButton");
        var mount = workspace.GetNode<SubViewport>("Windows/WorldWindow/BattleMount/BattleViewport");
        var mountContainer = workspace.GetNode<Control>("Windows/WorldWindow/BattleMount");
        manager.TryGetWindow("world", out WorkspaceWindow world);

        // 로비 상태(hub): 버튼 노출, 아직 전투 아님, mount 숨김.
        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        CheckEqual("wiring.world_hub_before", world.ContentMode, WorldContentMode.Hub);
        Check("wiring.not_active_before", entry.IsBattleActive, false);
        Check("wiring.mount_hidden_before", mountContainer.Visible, false);
        Check("wiring.button_wired", button != null, true);

        // 로비 버튼 클릭 → battle preset + entry.
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);

        Check("A.active", entry.IsBattleActive, true);
        var session = mount.GetChildren().OfType<BattleSession>().FirstOrDefault();
        Check("A.session_mounted", session != null, true);
        CheckEqual("A.session_running", session.State.ToString(), "Running");
        Check("A.battle_scene_present", session.GetChildren().OfType<BattleFieldScene>().Any(), true);
        CheckEqual("A.world_battle_mode", world.ContentMode, WorldContentMode.Battle);
        Check("A.mount_visible", mountContainer.Visible, true);
        Check("A.static_set", GodotObject.IsInstanceValid(BattleFieldScene.BattleField), true);
        Check("A.static_is_session_scene",
            session.GetChildren().OfType<BattleFieldScene>().First() == BattleFieldScene.BattleField, true);

        // 빠른 중복 클릭 → 전투/session 미복제.
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        CheckEqual("B.single_session", mount.GetChildren().OfType<BattleSession>().Count(), 1);
        Check("B.still_active", entry.IsBattleActive, true);

        workspace.QueueFree();
        await Frames(2);
        SetBattleFieldScene(null);
    }

    // --- helpers ---

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }

    private void Check(string name, bool actual, bool expected)
    {
        if (actual == expected) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }
}
#endif
