#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BS-001 Step 1: TurnHelper 명시적 lifecycle seam + 커서 결함 회귀.
// [A] AutoStart=true 기존 동작(직접 씬 실행 자동 시작) 유지.
// [B] AutoStart=false에서 물리 프레임이 지나도 시작되지 않고, Configure->StartBattle 이후에만 진행하며,
//     Start/Stop 중복 호출이 안전하고 Stop이 진행을 멈춘다.
// [C] 현재 턴 유닛 제거 시 다음 순서가 목록 처음으로 리셋되지 않고 정규 후임이 선택된다.
public partial class Bs001Step1LifecycleTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    private static readonly FieldInfo CurrentTurnField =
        typeof(TurnHelper).GetField("_currentTurnArticle", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo RunStateField =
        typeof(TurnHelper).GetField("_runState", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo SeedField =
        typeof(TurnHelper).GetField("_combatSeed", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly PropertyInfo SpeedProp = typeof(TurnHelper).GetProperty(nameof(TurnHelper.Speed));
    private static readonly MethodInfo AdvanceMethod =
        typeof(TurnHelper).GetMethod("AdvanceToNextTurn", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly MethodInfo RemoveMethod =
        typeof(TurnHelper).GetMethod("RemoveFromTurnOrder", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo DeathSubsField =
        typeof(TurnHelper).GetField("_deathSubscriptions", BindingFlags.NonPublic | BindingFlags.Instance);

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BS-001 Step1] Running TurnHelper lifecycle + cursor tests...");
        double previousTimeScale = Engine.TimeScale;
        Engine.TimeScale = 16.0;
        try
        {
            await TestAutoStartTrueLegacyAutoStarts();
            await TestExplicitLifecycleAndIdempotency();
            await TestReconfigureDoesNotLeakSubscriptions();
            TestCursorSuccessorSelection();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BS-001 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            Engine.TimeScale = previousTimeScale;
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[BS-001 Step1] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[BS-001 Step1] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    // [A] AutoStart 기본값(true) 직접 실행 경로가 _Ready에서 자동 시작한다.
    private async Task TestAutoStartTrueLegacyAutoStarts()
    {
        GD.Print("[A] AutoStart=true legacy auto-start");
        var battle = GD.Load<PackedScene>(BattleScenePath).Instantiate<BattleFieldScene>();
        try
        {
            var turnHelper = battle.GetNode<TurnHelper>("TurnHelper");
            SeedField.SetValue(turnHelper, 101L);
            SpeedProp.SetValue(turnHelper, 4.0f);
            // AutoStart는 건드리지 않는다(기본 true).

            AddChild(battle);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            Check("A.auto_started_current_set", CurrentTurn(turnHelper) != null, true);
            CheckEqual("A.auto_started_running", RunStateName(turnHelper), "Running");
        }
        finally { await FreeBattle(battle); }
    }

    // [B] AutoStart=false: 자동 시작 없음 -> Configure/Start 이후에만 진행 -> Start/Stop 멱등 -> Stop이 정지.
    private async Task TestExplicitLifecycleAndIdempotency()
    {
        GD.Print("[B] AutoStart=false explicit lifecycle + idempotency");
        var battle = GD.Load<PackedScene>(BattleScenePath).Instantiate<BattleFieldScene>();
        try
        {
            var turnHelper = battle.GetNode<TurnHelper>("TurnHelper");
            turnHelper.AutoStart = false;
            SpeedProp.SetValue(turnHelper, 4.0f);

            AddChild(battle);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            // 자동 시작하지 않는다.
            Check("B.no_autostart_current_null", CurrentTurn(turnHelper) == null, true);
            CheckEqual("B.no_autostart_state_idle", RunStateName(turnHelper), "Idle");

            var participants = battle.Articles.Articles
                .SelectMany(kv => kv.Value)
                .OfType<ITurnAffectedArticle<ArticleBase>>()
                .ToList();
            Check("B.has_participants", participants.Count >= 2, true);

            turnHelper.Configure(participants, 202L);
            CheckEqual("B.configured_state", RunStateName(turnHelper), "Configured");
            Check("B.configured_seed_applied", turnHelper.CombatSeed == 202L, true);
            Check("B.configured_current_null", CurrentTurn(turnHelper) == null, true);

            // Configured지만 미시작: 물리 프레임이 지나도 진행하지 않는다.
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Check("B.configured_no_progress", CurrentTurn(turnHelper) == null, true);

            turnHelper.StartBattle();
            var firstCurrent = CurrentTurn(turnHelper);
            Check("B.started_current_set", firstCurrent != null, true);
            CheckEqual("B.started_state", RunStateName(turnHelper), "Running");

            // 중복 StartBattle은 안전하고 상태/현재를 바꾸지 않는다.
            turnHelper.StartBattle();
            CheckEqual("B.double_start_state", RunStateName(turnHelper), "Running");
            Check("B.double_start_current_stable", ReferenceEquals(CurrentTurn(turnHelper), firstCurrent), true);

            // 턴이 실제로 진행돼 현재 행동자가 바뀐다(turn transition = 진행 증거).
            bool progressed = await WaitForTurnChange(turnHelper, firstCurrent, 6000);
            Check("B.turn_progressed", progressed, true);

            // 중복 StopBattle은 안전하다.
            turnHelper.StopBattle();
            turnHelper.StopBattle();
            CheckEqual("B.double_stop_state", RunStateName(turnHelper), "Stopped");

            // 정지 후에는 더 이상 진행하지 않는다(현재 행동자 고정).
            var stoppedCurrent = CurrentTurn(turnHelper);
            bool changedAfterStop = await WaitForTurnChange(turnHelper, stoppedCurrent, 600);
            Check("B.no_progress_after_stop", changedAfterStop, false);
        }
        finally { await FreeBattle(battle); }
    }

    // [BR] 재구성/정지 시 OnDead 구독이 누적/잔존하지 않는다(리뷰 P2). 실제 ArticleBase 참가자 필요 -> 씬 사용.
    private async Task TestReconfigureDoesNotLeakSubscriptions()
    {
        GD.Print("[BR] reconfigure/stop does not leak OnDead subscriptions");
        var battle = GD.Load<PackedScene>(BattleScenePath).Instantiate<BattleFieldScene>();
        try
        {
            var turnHelper = battle.GetNode<TurnHelper>("TurnHelper");
            turnHelper.AutoStart = false;
            SpeedProp.SetValue(turnHelper, 0.0f);
            AddChild(battle);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            var participants = battle.Articles.Articles
                .SelectMany(kv => kv.Value)
                .OfType<ITurnAffectedArticle<ArticleBase>>()
                .ToList();
            int articleParticipants = participants.OfType<ArticleBase>().Count();
            Check("BR.has_article_participants", articleParticipants >= 2, true);

            turnHelper.Configure(participants, 1L);
            CheckEqual("BR.subs_after_first_configure", DeathSubCount(turnHelper), articleParticipants);

            // 같은 참가자 객체로 재구성해도 구독이 누적되지 않는다.
            turnHelper.Configure(participants, 1L);
            CheckEqual("BR.subs_after_reconfigure_no_leak", DeathSubCount(turnHelper), articleParticipants);

            // 정지 시 구독을 해제한다.
            turnHelper.StartBattle();
            turnHelper.StopBattle();
            CheckEqual("BR.subs_cleared_after_stop", DeathSubCount(turnHelper), 0);
        }
        finally { await FreeBattle(battle); }
    }

    // [C] 커서 결함 수정: 현재 턴 유닛이 제거돼도 다음 순서가 목록 처음으로 리셋되지 않는다.
    // Fake 참가자 + reflection으로 커서 산술을 결정론적으로 단언한다(실제 씬 사망은 상태효과 타이밍 의존).
    private void TestCursorSuccessorSelection()
    {
        GD.Print("[C] cursor successor selection on current-unit removal");

        // 후임 선택: [u0,u1,u2], u1(현재) 제거 -> 다음은 u2(후임), u0(처음)이 아님.
        var th = new TurnHelper { AutoStart = false };
        var u0 = new FakeTurnUnit("u0", 0);
        var u1 = new FakeTurnUnit("u1", 1);
        var u2 = new FakeTurnUnit("u2", 2);
        th.Configure(new List<ITurnAffectedArticle<ArticleBase>> { u0, u1, u2 }, 1);
        th.StartBattle();
        Check("C.start_first", ReferenceEquals(CurrentTurn(th), u0), true);
        Advance(th);
        Check("C.advance_to_u1", ReferenceEquals(CurrentTurn(th), u1), true);
        Remove(th, u1); // 현재 유닛 사망 시뮬레이션
        Advance(th);
        Check("C.successor_is_u2_not_reset", ReferenceEquals(CurrentTurn(th), u2), true);
        th.Free();

        // wrap: 마지막 유닛(현재) 제거 -> 처음으로 wrap.
        var wth = new TurnHelper { AutoStart = false };
        var v0 = new FakeTurnUnit("v0", 0);
        var v1 = new FakeTurnUnit("v1", 1);
        var v2 = new FakeTurnUnit("v2", 2);
        wth.Configure(new List<ITurnAffectedArticle<ArticleBase>> { v0, v1, v2 }, 1);
        wth.StartBattle();
        Advance(wth); // v1
        Advance(wth); // v2 (마지막)
        Check("C.wrap_at_v2", ReferenceEquals(CurrentTurn(wth), v2), true);
        Remove(wth, v2);
        Advance(wth);
        Check("C.wrap_to_v0", ReferenceEquals(CurrentTurn(wth), v0), true);
        wth.Free();

        // 선행 유닛(비현재) 제거는 커서 정렬을 유지한다: [w0,w1,w2] 현재 w1, w0 제거 -> 다음은 w2.
        var pth = new TurnHelper { AutoStart = false };
        var w0 = new FakeTurnUnit("w0", 0);
        var w1 = new FakeTurnUnit("w1", 1);
        var w2 = new FakeTurnUnit("w2", 2);
        pth.Configure(new List<ITurnAffectedArticle<ArticleBase>> { w0, w1, w2 }, 1);
        pth.StartBattle();
        Advance(pth); // w1
        Check("C.pred_current_w1", ReferenceEquals(CurrentTurn(pth), w1), true);
        Remove(pth, w0); // 현재보다 앞선 유닛 제거
        Advance(pth);
        Check("C.pred_removal_next_is_w2", ReferenceEquals(CurrentTurn(pth), w2), true);
        pth.Free();
    }

    // --- helpers ---

    private async Task<bool> WaitForTurnChange(TurnHelper turnHelper, object baseline, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (!ReferenceEquals(CurrentTurn(turnHelper), baseline)) return true;
        }
        return false;
    }

    private static int DeathSubCount(TurnHelper th) =>
        ((System.Collections.ICollection)DeathSubsField.GetValue(th)).Count;
    private static object CurrentTurn(TurnHelper th) => CurrentTurnField.GetValue(th);
    private static string RunStateName(TurnHelper th) => RunStateField.GetValue(th)?.ToString();
    private static void Advance(TurnHelper th) => AdvanceMethod.Invoke(th, null);
    private static void Remove(TurnHelper th, ITurnAffectedArticle<ArticleBase> unit) =>
        RemoveMethod.Invoke(th, new object[] { unit });

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

    private async Task FreeBattle(BattleFieldScene battle)
    {
        battle.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        SetBattleFieldScene(null);
    }

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }

    // 턴 순서 커서 검증용 경량 참가자. Node/씬이 아니므로 OnDead 배선 없이 reflection으로 제거를 구동한다.
    private sealed class FakeTurnUnit : ITurnAffectedArticle<ArticleBase>
    {
        private readonly string _id;
        public FakeTurnUnit(string id, int spawnIndex) { _id = id; SpawnIndex = spawnIndex; }

        public int Priority { get; set; }
        public int SpawnIndex { get; set; }
        public TurnActionBase CurrentTurnAction { get; set; }
        BehaviorTree ITurnAffectedArticle<ArticleBase>.BehaviorTree => null;
        public void ApplyTurnStartEffects() { }
        public BtStatus TurnPlay(double delta) => BtStatus.Success;
        public override string ToString() => _id;
    }
}
#endif
