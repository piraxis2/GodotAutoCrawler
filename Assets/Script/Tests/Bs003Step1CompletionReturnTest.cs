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

// BS-003 Step 1: LobbyBattleEntry completion + lobby-return seam(headless, workspace 없이).
// [A] Victory → cleanup + mount 숨김 + 결과 라벨 + LastResult, 재진입 시 LastResult 보존.
// [B] Defeat → 패배 라벨.
// [C] Aborted → 시작 실패 라벨.
// [D] 첫 턴 lethal 즉시 완료 → 동일 복귀(Defeat).
// [E] 완료 시 outgame preset 실패에도 cleanup/mount 숨김 유지(Finding 2/OD6).
public partial class Bs003Step1CompletionReturnTest : Node
{
    private const string BattlePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private const string LethalStartPath = "res://Assets/Script/Tests/bs001_step3_lethal_start.tscn";
    private const string PlayerPath = "Articles/Ally/Character";
    private const string Opponent = "Articles/Opponent/Character2";

    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BS-003 Step1] Running completion + lobby-return seam tests...");
        try
        {
            await TestVictoryReturnAndReentry();
            await TestDefeatLabel();
            await TestAbortedLabel();
            await TestLethalImmediateReturn();
            await TestPresetFailureIsolation();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BS-003 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[BS-003 Step1] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[BS-003 Step1] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    // [A] Victory → cleanup/mount 숨김/라벨/LastResult + [F] 재진입 시 LastResult 보존, 라벨 "전투 진행 중".
    private async Task TestVictoryReturnAndReentry()
    {
        GD.Print("[A] Victory → lobby return + [F] re-entry preserves LastResult");
        var (entry, container, viewport, label) = MakeEntry();

        CheckEqual("A.initial_label", label.Text, "최근 전투: 없음");
        Check("A.initial_lastresult_null", entry.LastResult == null, true);

        entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 700L, PlayerPath));
        CheckEqual("A.entering_label", label.Text, "전투 진행 중");
        Check("A.mount_shown", container.Visible, true);

        await Frames(2);
        var battle = BattleUnder(viewport);
        Kill(battle, Opponent); // 상대 전멸 → Victory

        await Frames(3);
        Check("A.not_active", entry.IsBattleActive, false);
        Check("A.mount_hidden", container.Visible, false);
        CheckEqual("A.result_label_victory", label.Text, "최근 전투: 승리");
        Check("A.lastresult_victory", entry.LastResult?.Outcome == BattleOutcome.Victory, true);
        Check("A.static_cleared", GodotObject.IsInstanceValid(BattleFieldScene.BattleField), false);
        Check("A.session_freed_after_frame", HasValidSession(viewport), false);

        // [F] 재진입: 라벨 "진행 중", LastResult 보존(초기화 안 함).
        bool reok = entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 701L, PlayerPath));
        Check("F.reentry_ok", reok, true);
        Check("F.reactive", entry.IsBattleActive, true);
        CheckEqual("F.reentry_label", label.Text, "전투 진행 중");
        Check("F.lastresult_preserved", entry.LastResult?.Outcome == BattleOutcome.Victory, true);

        await FreeEntry(entry, container, label);
    }

    // [B] Defeat.
    private async Task TestDefeatLabel()
    {
        GD.Print("[B] Defeat label");
        var (entry, container, viewport, label) = MakeEntry();

        entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 800L, PlayerPath));
        await Frames(2);
        Kill(BattleUnder(viewport), PlayerPath); // PC 사망 → Defeat

        await Frames(3);
        Check("B.not_active", entry.IsBattleActive, false);
        Check("B.mount_hidden", container.Visible, false);
        CheckEqual("B.result_label_defeat", label.Text, "최근 전투: 패배");
        Check("B.lastresult_defeat", entry.LastResult?.Outcome == BattleOutcome.Defeat, true);

        await FreeEntry(entry, container, label);
    }

    // [C] Aborted(null scene).
    private async Task TestAbortedLabel()
    {
        GD.Print("[C] Aborted label (expected GD.PushError)");
        var (entry, container, viewport, label) = MakeEntry();

        entry.TryEnterBattle(MakeRequest(null, 1L, PlayerPath)); // null scene → Aborted
        await Frames(3);

        Check("C.not_active", entry.IsBattleActive, false);
        Check("C.mount_hidden", container.Visible, false);
        CheckEqual("C.result_label_aborted", label.Text, "최근 전투: 전투 시작 실패");
        Check("C.lastresult_aborted", entry.LastResult?.Outcome == BattleOutcome.Aborted, true);

        await FreeEntry(entry, container, label);
    }

    // [D] 첫 턴 lethal 즉시 완료.
    private async Task TestLethalImmediateReturn()
    {
        GD.Print("[D] lethal turn-start immediate completion → return");
        var (entry, container, viewport, label) = MakeEntry();

        entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(LethalStartPath), 1100L, PlayerPath));
        await Frames(4);

        Check("D.not_active", entry.IsBattleActive, false);
        Check("D.mount_hidden", container.Visible, false);
        CheckEqual("D.result_label_defeat", label.Text, "최근 전투: 패배");
        Check("D.lastresult_defeat", entry.LastResult?.Outcome == BattleOutcome.Defeat, true);

        await FreeEntry(entry, container, label);
    }

    // [E] 완료 시 outgame preset 실패에도 cleanup/mount 숨김 유지. enter는 null manager(preset 미적용),
    // 완료 직전에 unknown outgame preset을 가진 detached manager를 주입한다.
    private async Task TestPresetFailureIsolation()
    {
        GD.Print("[E] outgame preset failure does not skip cleanup");
        var (entry, container, viewport, label) = MakeEntry();

        entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 900L, PlayerPath));
        await Frames(2);

        var manager = new WorkspaceWindowManager(); // detached; ApplyPreset(unknown)은 tree 접근 전 false
        SetField(entry, "_windowManager", manager);
        SetField(entry, "_outgamePresetId", "nonexistent_outgame");

        Kill(BattleUnder(viewport), Opponent);
        await Frames(3);

        Check("E.cleanup_active", entry.IsBattleActive, false);
        Check("E.cleanup_mount_hidden", container.Visible, false);
        Check("E.cleanup_session_freed", HasValidSession(viewport), false);
        CheckEqual("E.label_still_set", label.Text, "최근 전투: 승리");

        manager.Free();
        await FreeEntry(entry, container, label);
    }

    // --- helpers ---

    private (LobbyBattleEntry entry, SubViewportContainer container, SubViewport viewport, Label label) MakeEntry()
    {
        var container = new SubViewportContainer();
        var viewport = new SubViewport();
        container.AddChild(viewport);
        AddChild(container);
        var label = new Label();
        AddChild(label);

        var entry = new LobbyBattleEntry();
        SetField(entry, "_battleMount", viewport);
        SetField(entry, "_battleMountVisibility", container);
        SetField(entry, "_resultLabel", label);
        AddChild(entry);
        return (entry, container, viewport, label);
    }

    private static void SetField(LobbyBattleEntry entry, string name, object value) =>
        typeof(LobbyBattleEntry).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(entry, value);

    private static BattleSession SessionUnder(Node mount) =>
        mount.GetChildren().OfType<BattleSession>().FirstOrDefault();

    private static BattleFieldScene BattleUnder(Node mount) =>
        SessionUnder(mount).GetChildren().OfType<BattleFieldScene>().First();

    private static bool HasValidSession(Node mount) =>
        mount.GetChildren().OfType<BattleSession>().Any(GodotObject.IsInstanceValid);

    private static BattleRequest MakeRequest(PackedScene scene, long seed, string playerPath) =>
        new() { BattleScene = scene, Seed = seed, PlayerPath = playerPath };

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

    private async Task FreeEntry(LobbyBattleEntry entry, Node container, Node label)
    {
        entry.QueueFree();
        container.QueueFree();
        label.QueueFree();
        await Frames(2);
        SetBattleFieldScene(null);
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
