#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Battle;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BS-001 Step 2: BattleSession 시작·소유·검증.
// [A] 유효 request가 전투를 시작한다(씬 소유, TurnHelper Running, seed 주입, 세션 경로 ammo 충전).
// [B] invalid request matrix가 crash/SCRIPT ERROR 없이 Aborted/InvalidSetup을 deferred로 정확히 1회 반환.
// [C] 단일 활성 세션 가드: 유효 컨텍스트가 있으면 두 번째 시작을 거부.
// [D] lifecycle fail-closed: configure 전 start, 중복 configure, completed 뒤 start.
public partial class Bs001Step2SessionStartTest : Node
{
    private const string BattlePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private const string EmptyFactionsPath = "res://Assets/Script/Tests/bs001_step2_empty_factions.tscn";
    private const string MissingTurnHelperPath = "res://Assets/Script/Tests/bs001_step2_missing_turnhelper.tscn";
    private const string MissingArticlesPath = "res://Assets/Script/Tests/bs001_step2_missing_articles.tscn";
    private const string ForgedAllyPath = "res://Assets/Script/Tests/bs001_step2_forged_ally.tscn";
    private const string PlayerPath = "Articles/Ally/Character";

    private int _failures;

    private static readonly FieldInfo ThRunStateField =
        typeof(TurnHelper).GetField("_runState", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo ThCurrentField =
        typeof(TurnHelper).GetField("_currentTurnArticle", BindingFlags.NonPublic | BindingFlags.Instance);

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BS-001 Step2] Running BattleSession start/ownership tests...");
        try
        {
            await TestValidRequestStartsBattle();
            await TestInvalidMatrix();
            await TestForgedFixtureValidControl();
            await TestSingleActiveSessionGuard();
            await TestLifecycleFailClosed();
            await TestCompletedExceptionIsolation();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BS-001 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[BS-001 Step2] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[BS-001 Step2] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    // [A] 유효 request → 전투 시작.
    private async Task TestValidRequestStartsBattle()
    {
        GD.Print("[A] valid request starts battle");
        var cap = new Capture();
        var req = MakeRequest(GD.Load<PackedScene>(BattlePath), 555L, PlayerPath);
        var session = StartSession(req, cap);

        CheckEqual("A.state_running", session.State.ToString(), "Running");
        CheckEqual("A.no_synchronous_completion", cap.Count, 0);

        await Frames(3);
        CheckEqual("A.still_running_no_completion", cap.Count, 0); // Step 2는 승패 완료를 내지 않는다

        var battle = session.GetChildren().OfType<BattleFieldScene>().FirstOrDefault();
        Check("A.session_owns_scene", battle != null, true);

        var turnHelper = battle.TurnHelper;
        CheckEqual("A.turnhelper_running", ThRunStateField.GetValue(turnHelper)?.ToString(), "Running");
        Check("A.current_turn_set", ThCurrentField.GetValue(turnHelper) != null, true);
        Check("A.seed_injected_no_reflection", turnHelper.CombatSeed == 555L, true);

        // 세션 경로 ammo 충전(TurnHelper 밖): 상대 SkillCaster의 Battle 스킬이 충전됐다.
        var opponent = battle.GetNode<CharacterArticle>("Articles/Opponent/Character2");
        bool charged = opponent.SkillAmmoState.TryGetAmmo(new StringName("staff_chainlightning"), out int rem, out _);
        Check("A.session_charged_battle_ammo", charged, true);
        CheckEqual("A.ammo_full", rem, 2);

        await FreeSession(session);
    }

    // [B] invalid matrix → Aborted/InvalidSetup, deferred, 1회.
    private async Task TestInvalidMatrix()
    {
        GD.Print("[B] invalid request matrix");

        // B1 null scene (AddChild 전 실패)
        await RunAbortCase("B1_null_scene", MakeRequest(null, 11L, PlayerPath));

        // B2 wrong scene type (BattleFieldScene 아님)
        var wrong = new PackedScene();
        var tmp = new Node2D();
        wrong.Pack(tmp);
        tmp.Free();
        await RunAbortCase("B2_wrong_type", MakeRequest(wrong, 22L, PlayerPath));

        // B3 PC path miss (진영은 유효, PC 노드 없음)
        await RunAbortCase("B3_pc_missing", MakeRequest(GD.Load<PackedScene>(BattlePath), 33L, "Articles/Ally/Ghost"));

        // B4 PC not Ally (상대 진영 노드를 PC로 지정)
        await RunAbortCase("B4_pc_not_ally", MakeRequest(GD.Load<PackedScene>(BattlePath), 44L, "Articles/Opponent/Character2"));

        // B5 empty factions (Ally/Opponent 공백)
        await RunAbortCase("B5_empty_factions", MakeRequest(GD.Load<PackedScene>(EmptyFactionsPath), 55L, PlayerPath));

        // B6 instantiate 실패 (빈 PackedScene → Instantiate null)
        await RunAbortCase("B6_instantiate_fail", MakeRequest(new PackedScene(), 66L, PlayerPath));

        // B7 필수 TurnHelper 누락
        await RunAbortCase("B7_missing_turnhelper", MakeRequest(GD.Load<PackedScene>(MissingTurnHelperPath), 77L, PlayerPath));

        // B8 필수 ArticlesContainer 누락
        await RunAbortCase("B8_missing_articles", MakeRequest(GD.Load<PackedScene>(MissingArticlesPath), 88L, PlayerPath));

        // F PC의 부모 이름이 "Ally"지만 registry 미등록(컨테이너 밖 위조) → registry 검증으로 거부(리뷰 P2).
        // 구 코드(부모 이름만 확인)는 통과했을 케이스다. 진영이 채워져 faction 검증을 통과한 뒤 PC 단계에서 거부.
        await RunAbortCase("F_forged_ally", MakeRequest(GD.Load<PackedScene>(ForgedAllyPath), 99L, "Ally/ForgedChar"));
    }

    private async Task RunAbortCase(string name, BattleRequest req)
    {
        var cap = new Capture();
        var session = StartSession(req, cap);

        // 완료 latch는 동기 설정되지만 event는 deferred(OD4).
        CheckEqual($"{name}.state_completed", session.State.ToString(), "Completed");
        CheckEqual($"{name}.deferred_not_synchronous", cap.Count, 0);

        await Frames(3);

        CheckEqual($"{name}.completed_once", cap.Count, 1);
        Check($"{name}.outcome_aborted", cap.Last?.Outcome == BattleOutcome.Aborted, true);
        Check($"{name}.reason_invalid_setup", cap.Last?.EndReason == BattleEndReason.InvalidSetup, true);
        Check($"{name}.seed_preserved", cap.Last?.Seed == req.Seed, true);

        await FreeSession(session);
    }

    // [Fc] 대조: 같은 forged fixture에서 실제 등록된 Ally PC로는 정상 시작한다 → F의 abort가 forged PC(registry
    // 미등록) 때문임을 격리 확인(fixture의 진영/실 PC 자체는 유효).
    private async Task TestForgedFixtureValidControl()
    {
        GD.Print("[Fc] forged fixture starts with real registered PC");
        var cap = new Capture();
        var session = StartSession(MakeRequest(GD.Load<PackedScene>(ForgedAllyPath), 100L, PlayerPath), cap);

        CheckEqual("Fc.valid_running", session.State.ToString(), "Running");
        await Frames(2);
        CheckEqual("Fc.no_completion", cap.Count, 0);

        await FreeSession(session);
    }

    // [C] 단일 활성 세션 가드.
    private async Task TestSingleActiveSessionGuard()
    {
        GD.Print("[C] single active session guard");
        var cap1 = new Capture();
        var session1 = StartSession(MakeRequest(GD.Load<PackedScene>(BattlePath), 111L, PlayerPath), cap1);
        CheckEqual("C.first_running", session1.State.ToString(), "Running");

        var cap2 = new Capture();
        var session2 = StartSession(MakeRequest(GD.Load<PackedScene>(BattlePath), 222L, PlayerPath), cap2);
        await Frames(3);

        CheckEqual("C.second_completed_once", cap2.Count, 1);
        Check("C.second_aborted", cap2.Last?.Outcome == BattleOutcome.Aborted, true);
        Check("C.second_invalid_setup", cap2.Last?.EndReason == BattleEndReason.InvalidSetup, true);
        CheckEqual("C.first_still_running", session1.State.ToString(), "Running");
        CheckEqual("C.first_never_completed", cap1.Count, 0);

        await FreeSession(session2);
        await FreeSession(session1);
    }

    // [D] lifecycle fail-closed.
    private async Task TestLifecycleFailClosed()
    {
        GD.Print("[D] lifecycle fail-closed");

        // D1 Configure 전 Start.
        var d1 = new BattleSession();
        AddChild(d1);
        d1.Start();
        CheckEqual("D1.start_before_configure_noop", d1.State.ToString(), "Created");
        d1.QueueFree();

        // D2 중복 Configure는 두 번째가 거부되고 상태는 Configured 유지.
        var d2 = new BattleSession();
        d2.Configure(MakeRequest(GD.Load<PackedScene>(BattlePath), 1L, PlayerPath));
        d2.Configure(MakeRequest(GD.Load<PackedScene>(BattlePath), 2L, PlayerPath));
        CheckEqual("D2.double_configure_state", d2.State.ToString(), "Configured");
        d2.QueueFree();

        // D3 Completed 뒤 Start 재호출은 거부되고 중복 완료가 없다.
        var cap = new Capture();
        var d3 = StartSession(MakeRequest(null, 3L, PlayerPath), cap); // abort
        await Frames(3);
        CheckEqual("D3.completed_once", cap.Count, 1);
        d3.Start(); // completed 뒤 start → 거부
        await Frames(3);
        CheckEqual("D3.no_restart_completion", cap.Count, 1);
        CheckEqual("D3.state_stays_completed", d3.State.ToString(), "Completed");
        await FreeSession(d3);
    }

    // [E] Completed subscriber 예외 격리(OD2): 첫 subscriber가 던져도 두 번째가 결과를 받는다.
    private async Task TestCompletedExceptionIsolation()
    {
        GD.Print("[E] Completed subscriber exception isolation");
        var session = new BattleSession();
        bool secondReceived = false;
        BattleResult secondResult = null;

        session.Completed += _ => throw new InvalidOperationException("intentional test throw");
        session.Completed += r => { secondReceived = true; secondResult = r; };

        session.Configure(MakeRequest(null, 123L, PlayerPath)); // abort → 완료
        AddChild(session);
        session.Start();
        await Frames(3);

        Check("E.second_subscriber_received", secondReceived, true);
        Check("E.second_got_aborted_result", secondResult?.Outcome == BattleOutcome.Aborted, true);
        Check("E.seed_preserved", secondResult?.Seed == 123L, true);

        await FreeSession(session);
    }

    // --- helpers ---

    private sealed class Capture
    {
        public int Count;
        public BattleResult Last;
    }

    private BattleSession StartSession(BattleRequest req, Capture cap)
    {
        var session = new BattleSession();
        session.Completed += r => { cap.Count++; cap.Last = r; };
        session.Configure(req);
        AddChild(session);
        session.Start();
        return session;
    }

    private static BattleRequest MakeRequest(PackedScene scene, long seed, string playerPath) =>
        new() { BattleScene = scene, Seed = seed, PlayerPath = playerPath };

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task FreeSession(BattleSession session)
    {
        session.QueueFree();
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
