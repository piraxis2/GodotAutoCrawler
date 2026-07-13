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

// BS-002 Step 1: LobbyBattleEntry seam.
// [A] valid request → mount 아래 session Running + active.
// [B] null request 동기 거부 / invalid setup은 deferred Aborted cleanup(active/Node 무잔존).
// [C] active 중 두 번째 호출 거부, 첫 session 무영향.
// [D] 자연 완료(Victory) 시 구독/active/session Node 정리.
// [E] mount 미배선이면 거부.
public partial class Bs002Step1EntryTest : Node
{
    private const string BattlePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private const string LethalStartPath = "res://Assets/Script/Tests/bs001_step3_lethal_start.tscn";
    private const string PlayerPath = "Articles/Ally/Character";

    private int _failures;

    private static readonly FieldInfo MountField =
        typeof(LobbyBattleEntry).GetField("_battleMount", BindingFlags.NonPublic | BindingFlags.Instance);

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BS-002 Step1] Running LobbyBattleEntry seam tests...");
        try
        {
            await TestValidEnter();
            await TestNullAndInvalidRequest();
            await TestDuplicateBlocked();
            await TestNaturalCompletionCleanup();
            await TestNoMountRejected();
            await TestPresetFailureFailClosed();
            await TestLethalStartCleanup();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BS-002 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[BS-002 Step1] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[BS-002 Step1] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    // [A] valid request → session Running under mount.
    private async Task TestValidEnter()
    {
        GD.Print("[A] valid request enters battle");
        var mount = new SubViewport();
        AddChild(mount);
        var entry = MakeEntry(mount);

        bool ok = entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 700L, PlayerPath));
        Check("A.entered", ok, true);
        Check("A.active", entry.IsBattleActive, true);

        var session = SessionUnder(mount);
        Check("A.session_mounted", session != null, true);

        await Frames(2);
        CheckEqual("A.session_running", session.State.ToString(), "Running");
        Check("A.battle_scene_present", session.GetChildren().OfType<BattleFieldScene>().Any(), true);
        Check("A.static_set", GodotObject.IsInstanceValid(BattleFieldScene.BattleField), true);

        await FreeEntry(entry, mount);
    }

    // [B] null request 동기 거부 + invalid setup deferred cleanup.
    private async Task TestNullAndInvalidRequest()
    {
        GD.Print("[B] null request rejected / invalid setup deferred cleanup");
        var mount = new SubViewport();
        AddChild(mount);
        var entry = MakeEntry(mount);

        // 동기 거부: null request.
        Check("B.null_rejected", entry.TryEnterBattle(null), false);
        Check("B.null_not_active", entry.IsBattleActive, false);

        // deferred abort: null scene → 세션 생성(검증은 세션 소관) → Aborted → cleanup.
        bool okBad = entry.TryEnterBattle(MakeRequest(null, 1L, PlayerPath));
        Check("B.invalid_entered", okBad, true);
        Check("B.invalid_active_immediately", entry.IsBattleActive, true);

        await Frames(3);

        Check("B.invalid_cleaned_active", entry.IsBattleActive, false);
        Check("B.invalid_no_session_leak", HasValidSession(mount), false);

        await FreeEntry(entry, mount);
    }

    // [C] active 중 두 번째 호출 거부.
    private async Task TestDuplicateBlocked()
    {
        GD.Print("[C] duplicate entry blocked");
        var mount = new SubViewport();
        AddChild(mount);
        var entry = MakeEntry(mount);

        entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 111L, PlayerPath));
        var session1 = SessionUnder(mount);

        bool ok2 = entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 222L, PlayerPath));
        Check("C.second_rejected", ok2, false);
        Check("C.still_active", entry.IsBattleActive, true);
        CheckEqual("C.single_session", mount.GetChildren().OfType<BattleSession>().Count(), 1);

        await Frames(2);
        CheckEqual("C.first_running", session1.State.ToString(), "Running");

        await FreeEntry(entry, mount);
    }

    // [D] 자연 완료(Victory) → cleanup.
    private async Task TestNaturalCompletionCleanup()
    {
        GD.Print("[D] natural completion cleanup");
        var mount = new SubViewport();
        AddChild(mount);
        var entry = MakeEntry(mount);

        entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 700L, PlayerPath));
        await Frames(2);

        var session = SessionUnder(mount);
        var battle = session.GetChildren().OfType<BattleFieldScene>().First();
        Kill(battle, "Articles/Opponent/Character2"); // 상대 전멸 → Victory

        await Frames(4);

        Check("D.cleaned_active", entry.IsBattleActive, false);
        Check("D.session_freed", HasValidSession(mount), false);
        Check("D.static_cleared", GodotObject.IsInstanceValid(BattleFieldScene.BattleField), false);

        await FreeEntry(entry, mount);
    }

    // [E] mount 미배선 → 거부.
    private async Task TestNoMountRejected()
    {
        GD.Print("[E] no mount rejected");
        var entry = new LobbyBattleEntry();
        AddChild(entry);

        bool ok = entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 1L, PlayerPath));
        Check("E.no_mount_rejected", ok, false);
        Check("E.not_active", entry.IsBattleActive, false);

        entry.QueueFree();
        await Frames(2);
        SetBattleFieldScene(null);
    }

    // [F] manager가 있을 때 preset 실패(unknown preset)면 세션을 만들지 않고 fail-closed(리뷰 P2).
    private async Task TestPresetFailureFailClosed()
    {
        GD.Print("[F] preset failure fail-closed");
        var mount = new SubViewport();
        AddChild(mount);
        var entry = MakeEntry(mount);

        // 배선 오류를 모사: manager는 있고 preset id는 unknown → ApplyPreset false.
        var manager = new WorkspaceWindowManager();
        SetField(entry, "_windowManager", manager);
        SetField(entry, "_battlePresetId", "nonexistent_preset");

        bool ok = entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(BattlePath), 1L, PlayerPath));
        Check("F.preset_fail_rejected", ok, false);
        Check("F.not_active", entry.IsBattleActive, false);
        Check("F.no_session", HasValidSession(mount), false);

        manager.Free();
        await FreeEntry(entry, mount);
    }

    // [G] Start() 직후 완료 예약(첫 턴 lethal turn-start)도 entry가 정상 정리한다(리뷰 P3, bs001 lethal fixture 재사용).
    private async Task TestLethalStartCleanup()
    {
        GD.Print("[G] immediate-completion (lethal turn-start) cleanup");
        var mount = new SubViewport();
        AddChild(mount);
        var entry = MakeEntry(mount);

        bool ok = entry.TryEnterBattle(MakeRequest(GD.Load<PackedScene>(LethalStartPath), 1100L, PlayerPath));
        Check("G.entered", ok, true);
        Check("G.active_immediately", entry.IsBattleActive, true); // 완료는 deferred라 즉시엔 active

        await Frames(4);

        Check("G.cleaned_active", entry.IsBattleActive, false);
        Check("G.session_freed", HasValidSession(mount), false);
        Check("G.static_cleared", GodotObject.IsInstanceValid(BattleFieldScene.BattleField), false);

        await FreeEntry(entry, mount);
    }

    // --- helpers ---

    private static void SetField(LobbyBattleEntry entry, string name, object value) =>
        typeof(LobbyBattleEntry).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(entry, value);

    private LobbyBattleEntry MakeEntry(Node mount)
    {
        var entry = new LobbyBattleEntry();
        MountField.SetValue(entry, mount);
        AddChild(entry);
        return entry;
    }

    private static BattleSession SessionUnder(Node mount) =>
        mount.GetChildren().OfType<BattleSession>().FirstOrDefault();

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

    private async Task FreeEntry(LobbyBattleEntry entry, Node mount)
    {
        entry.QueueFree();
        mount.QueueFree();
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
