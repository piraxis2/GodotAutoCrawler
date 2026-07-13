#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.Battle;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BS-001 Step 3: 완료 판정·결과 캡처·연속 재실행.
// [A] 상대 전멸 = Victory/OpponentsEliminated. 완료 event deferred·정확히 1회. 결과가 queue_free 뒤에도 보존.
// [B] 지정 PC 사망 = Defeat/PlayerDefeated.
// [C] PC 사망 + 다른 Ally 생존 = Defeat(생존 여부 무관).
// [D] 동시 사망(mutual kill) = Defeat 우선(OD5).
// [G] 같은 프로세스 2회 연속 실행(완료 handler 내 재진입, stale singleton 없음, seed/상태 격리).
public partial class Bs001Step3CompletionTest : Node
{
    private const string BattlePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private const string TwoAlliesPath = "res://Assets/Script/Tests/bs001_step3_two_allies.tscn";
    private const string LethalStartPath = "res://Assets/Script/Tests/bs001_step3_lethal_start.tscn";
    private const string PlayerPath = "Articles/Ally/Character";
    private const string PcId = "Ally:0";

    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BS-001 Step3] Running completion/result/re-entry tests...");
        try
        {
            await TestVictory();
            await TestDefeatSingleAlly();
            await TestDefeatWithSurvivingAlly();
            await TestMutualKillDefeatPrecedence();
            await TestLethalTurnStart();
            await TestSequentialReentry();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BS-001 Step3] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[BS-001 Step3] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[BS-001 Step3] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    // [A] 상대 전멸 → Victory. deferred·1회·결과 보존·정적 정리.
    private async Task TestVictory()
    {
        GD.Print("[A] opponents eliminated -> Victory");
        var cap = new Capture();
        var session = StartSession(MakeRequest(GD.Load<PackedScene>(BattlePath), 700L, PlayerPath), cap);
        var battle = BattleOf(session);

        Kill(battle, "Articles/Opponent/Character2");
        CheckEqual("A.deferred_not_synchronous", cap.Count, 0); // 사망 콜스택에서 동기 완료하지 않는다

        await Frames(4);

        CheckEqual("A.completed_once", cap.Count, 1);
        Check("A.outcome_victory", cap.Last?.Outcome == BattleOutcome.Victory, true);
        Check("A.reason_opponents_eliminated", cap.Last?.EndReason == BattleEndReason.OpponentsEliminated, true);
        Check("A.player_alive", cap.Last?.PlayerAlive == true, true);
        CheckEqual("A.surviving_opponents", cap.Last?.SurvivingOpponents ?? -1, 0);
        CheckEqual("A.surviving_allies", cap.Last?.SurvivingAllies ?? -1, 1);
        CheckEqual("A.seed_preserved", cap.Last?.Seed ?? -1, 700L);
        CheckEqual("A.session_completed", session.State.ToString(), "Completed");

        var opp = cap.Last.Units.First(u => u.Faction == "Opponent");
        Check("A.opponent_dead", opp.Survived == false, true);
        CheckEqual("A.opponent_final_health_zero", opp.FinalHealth, (int?)0);
        var pc = cap.Last.Units.First(u => u.SessionUnitId == PcId);
        Check("A.pc_survived", pc.Survived, true);

        // 정적 context가 teardown으로 정리됐다(완료 event 전에 RemoveChild → _ExitTree).
        Check("A.static_cleared", GodotObject.IsInstanceValid(BattleFieldScene.BattleField), false);

        // 결과는 값 스냅샷 → 사망 유닛이 완전히 queue_free된 뒤에도 보존된다.
        var oppTileBefore = opp.FinalTile;
        await Frames(4);
        var oppAfter = cap.Last.Units.First(u => u.Faction == "Opponent");
        Check("A.result_preserved_after_free", oppAfter.Survived == false && oppAfter.FinalHealth == 0
            && oppAfter.FinalTile == oppTileBefore, true);

        await FreeSession(session);
    }

    // [B] 지정 PC 사망 → Defeat (단일 Ally).
    private async Task TestDefeatSingleAlly()
    {
        GD.Print("[B] player defeated -> Defeat");
        var cap = new Capture();
        var session = StartSession(MakeRequest(GD.Load<PackedScene>(BattlePath), 800L, PlayerPath), cap);
        var battle = BattleOf(session);

        Kill(battle, PlayerPath);
        await Frames(4);

        CheckEqual("B.completed_once", cap.Count, 1);
        Check("B.outcome_defeat", cap.Last?.Outcome == BattleOutcome.Defeat, true);
        Check("B.reason_player_defeated", cap.Last?.EndReason == BattleEndReason.PlayerDefeated, true);
        Check("B.player_not_alive", cap.Last?.PlayerAlive == false, true);
        CheckEqual("B.surviving_allies", cap.Last?.SurvivingAllies ?? -1, 0);
        var pc = cap.Last.Units.First(u => u.SessionUnitId == PcId);
        Check("B.pc_dead", pc.Survived == false, true);
        CheckEqual("B.pc_final_health_zero", pc.FinalHealth, (int?)0);

        await FreeSession(session);
    }

    // [C] PC 사망 + 다른 Ally 생존 → Defeat (다른 Ally 생존 여부 무관).
    private async Task TestDefeatWithSurvivingAlly()
    {
        GD.Print("[C] player defeated while ally survives -> Defeat");
        var cap = new Capture();
        var session = StartSession(MakeRequest(GD.Load<PackedScene>(TwoAlliesPath), 900L, PlayerPath), cap);
        var battle = BattleOf(session);

        Kill(battle, PlayerPath); // PC만 죽인다(Character3는 생존)
        await Frames(4);

        CheckEqual("C.completed_once", cap.Count, 1);
        Check("C.outcome_defeat", cap.Last?.Outcome == BattleOutcome.Defeat, true);
        Check("C.player_not_alive", cap.Last?.PlayerAlive == false, true);
        CheckEqual("C.surviving_allies_one", cap.Last?.SurvivingAllies ?? -1, 1); // 다른 Ally 생존
        var pc = cap.Last.Units.First(u => u.SessionUnitId == PcId);
        Check("C.pc_dead", pc.Survived == false, true);

        await FreeSession(session);
    }

    // [D] 동시 사망(PC + 마지막 상대) → Defeat 우선(OD5). 상대를 먼저 죽여 Victory가 먼저 detection돼도 Defeat.
    private async Task TestMutualKillDefeatPrecedence()
    {
        GD.Print("[D] mutual kill -> Defeat precedence");
        var cap = new Capture();
        var session = StartSession(MakeRequest(GD.Load<PackedScene>(BattlePath), 1000L, PlayerPath), cap);
        var battle = BattleOf(session);

        // 같은 프레임(동기 블록)에 상대 → PC 순으로 죽인다. 상대 사망이 Victory를 먼저 latch하지만 deferred
        // 판정이 PC 사망을 먼저 평가해 Defeat가 된다.
        Kill(battle, "Articles/Opponent/Character2");
        Kill(battle, PlayerPath);
        await Frames(4);

        CheckEqual("D.completed_once", cap.Count, 1);
        Check("D.outcome_defeat_precedence", cap.Last?.Outcome == BattleOutcome.Defeat, true);
        Check("D.reason_player_defeated", cap.Last?.EndReason == BattleEndReason.PlayerDefeated, true);
        Check("D.player_not_alive", cap.Last?.PlayerAlive == false, true);

        await FreeSession(session);
    }

    // [H] 첫 턴 시작 효과(음수 HealthRegen)로 PC가 StartBattle 중 죽어도 완료가 누락되지 않는다(리뷰 P1).
    // StartBattle이 외부 코드(ApplyTurnStartEffects)를 실행하기 전에 세션이 Running으로 전환돼야 사망이 완료로 이어진다.
    private async Task TestLethalTurnStart()
    {
        GD.Print("[H] lethal turn-start effect completes (deferred, once)");
        var cap = new Capture();
        var session = StartSession(MakeRequest(GD.Load<PackedScene>(LethalStartPath), 1100L, PlayerPath), cap);

        // StartBattle 중 PC가 이미 죽어 완료가 예약됐지만, 완료 자체는 deferred다(동기 완료 아님).
        CheckEqual("H.deferred_not_synchronous", cap.Count, 0);
        CheckEqual("H.running_before_lethal_effect", session.State.ToString(), "Running");

        await Frames(4);

        CheckEqual("H.completed_once", cap.Count, 1);
        Check("H.outcome_defeat", cap.Last?.Outcome == BattleOutcome.Defeat, true);
        Check("H.reason_player_defeated", cap.Last?.EndReason == BattleEndReason.PlayerDefeated, true);
        Check("H.player_not_alive", cap.Last?.PlayerAlive == false, true);
        Check("H.static_cleared", GodotObject.IsInstanceValid(BattleFieldScene.BattleField), false);
        var pc = cap.Last.Units.First(u => u.SessionUnitId == PcId);
        Check("H.pc_dead", pc.Survived == false, true);

        await FreeSession(session);
    }

    // [G] 2회 연속 실행: session1 완료 handler 안에서 session2를 시작해도 stale singleton 없이 독립 완료한다.
    private async Task TestSequentialReentry()
    {
        GD.Print("[G] sequential re-entry with isolation");
        var cap1 = new Capture();
        var cap2 = new Capture();
        BattleSession session2 = null;

        var session1 = new BattleSession();
        session1.Completed += r =>
        {
            cap1.Count++;
            cap1.Last = r;
            // 완료 handler 안에서 즉시 다음 세션 시작(같은 프레임 재진입). 정적 context가 이미 정리됐어야 성공한다.
            session2 = new BattleSession();
            session2.Completed += r2 => { cap2.Count++; cap2.Last = r2; };
            session2.Configure(MakeRequest(GD.Load<PackedScene>(BattlePath), 222L, PlayerPath));
            AddChild(session2);
            session2.Start();
        };
        session1.Configure(MakeRequest(GD.Load<PackedScene>(BattlePath), 111L, PlayerPath));
        AddChild(session1);
        session1.Start();

        Kill(BattleOf(session1), "Articles/Opponent/Character2");
        await Frames(4);

        CheckEqual("G.session1_completed", cap1.Count, 1);
        Check("G.session1_victory", cap1.Last?.Outcome == BattleOutcome.Victory, true);
        Check("G.session2_started_running", session2 != null && session2.State.ToString() == "Running", true);

        // session2가 stale singleton으로 거부되지 않고 실제로 시작됐다(Aborted가 아님).
        Kill(BattleOf(session2), "Articles/Opponent/Character2");
        await Frames(4);

        CheckEqual("G.session2_completed", cap2.Count, 1);
        Check("G.session2_victory", cap2.Last?.Outcome == BattleOutcome.Victory, true);
        CheckEqual("G.session2_seed_isolated", cap2.Last?.Seed ?? -1, 222L);
        Check("G.session2_player_alive", cap2.Last?.PlayerAlive == true, true);

        await FreeSession(session2);
        await FreeSession(session1);
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

    private static BattleFieldScene BattleOf(BattleSession session) =>
        session.GetChildren().OfType<BattleFieldScene>().FirstOrDefault();

    private static Health GetHealth(CharacterArticle article) =>
        article.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Health)) as Health;

    private static void Kill(BattleFieldScene battle, string path)
    {
        var article = battle.GetNode<CharacterArticle>(path);
        GetHealth(article).CurrentHealth = 0;
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
