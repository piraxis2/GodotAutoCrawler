#if TOOLS
using System;
using System.Collections.Generic;
using System.Reflection;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Cb001Step1TurnEffectTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0)
            {
                GD.Print("[CB-001 Step1] ALL PASS");
                GetTree().Quit(0);
            }
            else
            {
                GD.Print($"[CB-001 Step1] FAILED: {failures} assertion(s)");
                GetTree().Quit(1);
            }
        }
    }

    public int RunTests()
    {
        GD.Print("[CB-001 Step1] Running turn-start effect tests...");
        try
        {
            TestTurnStartAppliesExactlyOnce();
            TestRunningFramesDoNotReapplyEffects(1.0f);
            TestRunningFramesDoNotReapplyEffects(4.0f);
            TestTurnCompletionAppliesNextArticleOnce();
            TestGameOverDoesNotApplyTurnStartEffects();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[CB-001 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        return _failures;
    }

    private void Check(string name, bool actual, bool expected)
    {
        if (actual == expected)
        {
            GD.Print($"  PASS: {name}");
        }
        else
        {
            _failures++;
            GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}");
        }
    }

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected))
        {
            GD.Print($"  PASS: {name}");
        }
        else
        {
            _failures++;
            GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}");
        }
    }

    private void TestTurnStartAppliesExactlyOnce()
    {
        GD.Print("[A] New turn applies effects once");
        var first = new FakeTurnArticle("first", BtStatus.Running);
        var second = new FakeTurnArticle("second", BtStatus.Running);
        var helper = MakeHelper(1.0f, first, second);

        InvokeAdvanceToNextTurn(helper);

        CheckEqual("A.first_effect_count", first.TurnStartEffectCount, 1);
        CheckEqual("A.second_effect_count", second.TurnStartEffectCount, 0);
        CheckEqual("A.first_turn_play_count", first.TurnPlayCount, 0);

        helper.Free();
    }

    private void TestRunningFramesDoNotReapplyEffects(float speed)
    {
        GD.Print($"[B] Running frames do not reapply effects at Speed={speed}");
        var first = new FakeTurnArticle("first", BtStatus.Running);
        var second = new FakeTurnArticle("second", BtStatus.Running);
        var helper = MakeHelper(speed, first, second);

        InvokeAdvanceToNextTurn(helper);
        helper._PhysicsProcess(0.016);
        helper._PhysicsProcess(0.016);
        helper._PhysicsProcess(0.016);

        CheckEqual($"B.effect_count_speed_{speed}", first.TurnStartEffectCount, 1);
        CheckEqual($"B.turn_play_count_speed_{speed}", first.TurnPlayCount, 3);
        Check($"B.received_scaled_delta_{speed}", first.LastDelta > 0.0, true);

        helper.Free();
    }

    private void TestTurnCompletionAppliesNextArticleOnce()
    {
        GD.Print("[C] Completed turn applies next article once");
        var first = new FakeTurnArticle("first", BtStatus.Success);
        var second = new FakeTurnArticle("second", BtStatus.Running);
        var helper = MakeHelper(1.0f, first, second);

        InvokeAdvanceToNextTurn(helper);
        helper._PhysicsProcess(0.016);
        helper._PhysicsProcess(0.016);

        CheckEqual("C.first_effect_count", first.TurnStartEffectCount, 1);
        CheckEqual("C.second_effect_count", second.TurnStartEffectCount, 1);
        CheckEqual("C.first_turn_play_count", first.TurnPlayCount, 1);
        CheckEqual("C.second_turn_play_count", second.TurnPlayCount, 1);

        helper.Free();
    }

    private void TestGameOverDoesNotApplyTurnStartEffects()
    {
        GD.Print("[D] Game over state does not apply turn-start effects");
        var first = new FakeTurnArticle("first", BtStatus.Running);
        var helper = MakeHelper(1.0f, first);

        InvokeAdvanceToNextTurn(helper);

        CheckEqual("D.first_effect_count", first.TurnStartEffectCount, 0);
        CheckEqual("D.first_turn_play_count", first.TurnPlayCount, 0);

        helper.Free();
    }

    private static TurnHelper MakeHelper(float speed, params FakeTurnArticle[] articles)
    {
        var helper = new TurnHelper();
        var articlesContainer = MakeArticlesContainerWithTwoTeams();
        var fxPlayer = new FxPlayer();
        helper.AddChild(articlesContainer);
        helper.AddChild(fxPlayer);
        SetPrivateField(helper, "_articlesContainer", articlesContainer);
        SetPrivateField(helper, "_fxPlayer", fxPlayer);
        SetPrivateField(helper, "_currentTurnArticle", null);

        var list = (List<ITurnAffectedArticle<ArticleBase>>)GetPrivateField(helper, "_turnAffectedArticleList");
        list.Clear();
        foreach (var article in articles)
        {
            list.Add(article);
        }

        typeof(TurnHelper).GetProperty(nameof(TurnHelper.Speed))?.SetValue(helper, speed);
        return helper;
    }

    private static ArticlesContainer MakeArticlesContainerWithTwoTeams()
    {
        var container = new ArticlesContainer();
        container.Articles["Opponent"].Add(null);
        container.Articles["Ally"].Add(null);
        return container;
    }

    private static void InvokeAdvanceToNextTurn(TurnHelper helper)
    {
        typeof(TurnHelper)
            .GetMethod("AdvanceToNextTurn", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(helper, Array.Empty<object>());
    }

    private static object GetPrivateField(object target, string fieldName)
    {
        return target.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(target);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        target.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(target, value);
    }

    private sealed class FakeTurnArticle : ITurnAffectedArticle<ArticleBase>
    {
        private readonly BtStatus _status;

        public FakeTurnArticle(string name, BtStatus status)
        {
            Name = name;
            _status = status;
        }

        public string Name { get; }
        public int Priority { get; set; }
        public int SpawnIndex { get; set; }
        public TurnActionBase CurrentTurnAction { get; set; }
        public BehaviorTree BehaviorTree => null;
        public int TurnStartEffectCount { get; private set; }
        public int TurnPlayCount { get; private set; }
        public double LastDelta { get; private set; }

        public void ApplyTurnStartEffects()
        {
            TurnStartEffectCount++;
        }

        public BtStatus TurnPlay(double delta)
        {
            TurnPlayCount++;
            LastDelta = delta;
            return _status;
        }
    }
}
#endif