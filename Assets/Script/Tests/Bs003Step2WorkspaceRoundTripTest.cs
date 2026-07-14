#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.Battle;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BS-003 Step 2: workspace 로비↔전투 왕복 + 재전투(headless). 실제 결과 문구/화면 전환은 GUI smoke 전용이라
// 여기서는 버튼 → 전투 → 완료 → World hub 복귀 + 결과 라벨 + 재전투 Running까지 관측한다.
public partial class Bs003Step2WorkspaceRoundTripTest : Node
{
    private const string WorkspacePath = "res://Assets/Scenes/workspace.tscn";
    private const string Opponent = "Articles/Opponent/Character2";
    private const string Pc = "Articles/Ally/Character";

    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BS-003 Step2] Running workspace round-trip + re-entry tests...");
        try
        {
            await TestRoundTripAndReentry();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BS-003 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[BS-003 Step2] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[BS-003 Step2] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private async Task TestRoundTripAndReentry()
    {
        var workspace = GD.Load<PackedScene>(WorkspacePath).Instantiate();
        workspace.Set("_restoreLayoutOnReady", false);
        AddChild(workspace);
        await Frames(1);

        var manager = workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");
        var entry = workspace.GetNode<LobbyBattleEntry>("LobbyBattleEntry");
        var button = workspace.GetNode<Button>("Windows/WorldWindow/Panels/HubPanels/EnterBattleButton");
        var resultLabel = workspace.GetNode<Label>("Windows/WorldWindow/Panels/HubPanels/ResultLabel");
        var mount = workspace.GetNode<SubViewport>("Windows/WorldWindow/BattleMount/BattleViewport");
        var mountContainer = workspace.GetNode<Control>("Windows/WorldWindow/BattleMount");
        var hubPanels = workspace.GetNode<Control>("Windows/WorldWindow/Panels/HubPanels");
        manager.TryGetWindow("world", out WorkspaceWindow world);

        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame); // 로비 baseline
        CheckEqual("setup.world_hub", world.ContentMode, WorldContentMode.Hub);
        CheckEqual("setup.result_label_initial", resultLabel.Text, "최근 전투: 없음");
        Check("setup.mount_hidden", mountContainer.Visible, false);

        // --- 1차 전투: 버튼 → battle → Victory → hub 복귀 ---
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check("A1.active", entry.IsBattleActive, true);
        CheckEqual("A1.world_battle", world.ContentMode, WorldContentMode.Battle);
        Check("A1.mount_shown", mountContainer.Visible, true);
        CheckEqual("A1.result_inprogress", resultLabel.Text, "전투 진행 중");

        Kill(BattleUnder(mount), Opponent);
        await Frames(4);

        Check("A1.not_active", entry.IsBattleActive, false);
        CheckEqual("A1.world_hub", world.ContentMode, WorldContentMode.Hub);
        Check("A1.mount_hidden", mountContainer.Visible, false);
        Check("A1.hub_visible", hubPanels.Visible, true);
        CheckEqual("A1.result_victory", resultLabel.Text, "최근 전투: 승리");
        Check("A1.no_session", HasValidSession(mount), false);
        Check("A1.static_cleared", GodotObject.IsInstanceValid(BattleFieldScene.BattleField), false);

        // --- 2차 전투: 같은 버튼 → 두 번째 session 하나만 Running → Victory → hub 복귀 ---
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check("A2.active", entry.IsBattleActive, true);
        CheckEqual("A2.single_session", mount.GetChildren().OfType<BattleSession>().Count(), 1);
        CheckEqual("A2.world_battle", world.ContentMode, WorldContentMode.Battle);

        Kill(BattleUnder(mount), Opponent);
        await Frames(4);

        Check("A2.not_active", entry.IsBattleActive, false);
        CheckEqual("A2.world_hub", world.ContentMode, WorldContentMode.Hub);
        Check("A2.mount_hidden", mountContainer.Visible, false);
        CheckEqual("A2.result_victory", resultLabel.Text, "최근 전투: 승리");
        Check("A2.no_session", HasValidSession(mount), false);
        Check("A2.static_cleared", GodotObject.IsInstanceValid(BattleFieldScene.BattleField), false);

        workspace.QueueFree();
        await Frames(2);
        SetBattleFieldScene(null);
    }

    // --- helpers ---

    private static BattleFieldScene BattleUnder(Node mount) =>
        mount.GetChildren().OfType<BattleSession>().First().GetChildren().OfType<BattleFieldScene>().First();

    private static bool HasValidSession(Node mount) =>
        mount.GetChildren().OfType<BattleSession>().Any(GodotObject.IsInstanceValid);

    private static void Kill(BattleFieldScene battle, string path)
    {
        var article = battle.GetNode<CharacterArticle>(path);
        (article.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Health)) as Health).CurrentHealth = 0;
    }

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
