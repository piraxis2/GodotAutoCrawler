#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Cb001Step3CanonicalOrderTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0)
            {
                GD.Print("[CB-001 Step3] ALL PASS");
                GetTree().Quit(0);
            }
            else
            {
                GD.Print($"[CB-001 Step3] FAILED: {failures} assertion(s)");
                GetTree().Quit(1);
            }
        }
    }

    public int RunTests()
    {
        GD.Print("[CB-001 Step3] Running canonical order tests...");
        try
        {
            TestAttackRangePositionsCanonicalOrder();
            TestCanonicalTargetTieBreak();
            TestChainStyleLeadingKeyThenCanonical();
            TestTurnOrderStableSort();
            TestSelectCanonicalPath();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[CB-001 Step3] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        return _failures;
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

    private static string Join(IEnumerable<Vector2I> positions)
    {
        return string.Join("|", positions.Select(p => $"({p.X},{p.Y})"));
    }

    private void TestAttackRangePositionsCanonicalOrder()
    {
        GD.Print("[A] GetAttackRangePositions canonical order");

        var range1 = SkillUtil.GetAttackRangePositions(1);
        CheckEqual("A.range1_sequence", Join(range1), "(0,0)|(0,-1)|(-1,0)|(1,0)|(0,1)");

        var range2First = SkillUtil.GetAttackRangePositions(2);
        var range2Second = SkillUtil.GetAttackRangePositions(2);
        CheckEqual("A.range2_sequence", Join(range2First),
            "(0,0)|(0,-1)|(-1,0)|(1,0)|(0,1)|(0,-2)|(-1,-1)|(1,-1)|(-2,0)|(2,0)|(-1,1)|(1,1)|(0,2)");
        CheckEqual("A.range2_repeatable", Join(range2First), Join(range2Second));
    }

    private void TestCanonicalTargetTieBreak()
    {
        GD.Print("[B] Canonical target tie-break (distance -> Y -> X)");
        var origin = new Vector2I(5, 5);
        var candidates = new List<Vector2I>
        {
            new(6, 5), new(5, 6), new(4, 5), new(5, 4), new(7, 5), new(3, 5)
        };

        var ordered = candidates.OrderByCanonical(p => p, origin).ToList();
        CheckEqual("B.ordered_sequence", Join(ordered), "(5,4)|(4,5)|(6,5)|(5,6)|(3,5)|(7,5)");
        CheckEqual("B.winner", ordered.First(), new Vector2I(5, 4));
    }

    private void TestChainStyleLeadingKeyThenCanonical()
    {
        GD.Print("[C] Leading key (unhit-first) then canonical tie-break");
        var origin = new Vector2I(5, 5);
        var candidates = new List<(Vector2I Position, bool AlreadyHit)>
        {
            (new Vector2I(5, 4), true),  // 가장 가깝지만 이미 맞은 대상
            (new Vector2I(5, 6), false),
            (new Vector2I(6, 5), false)
        };

        var ordered = candidates
            .OrderBy(c => c.AlreadyHit)
            .ThenByCanonical(c => c.Position, origin)
            .Select(c => c.Position)
            .ToList();

        CheckEqual("C.ordered_sequence", Join(ordered), "(6,5)|(5,6)|(5,4)");
    }

    private void TestTurnOrderStableSort()
    {
        GD.Print("[D] Turn order: Priority -> SpawnIndex");
        var helper = new TurnHelper();
        var a = new FakeTurnArticle("a") { Priority = 1, SpawnIndex = 0 };
        var b = new FakeTurnArticle("b") { Priority = 0, SpawnIndex = 3 };
        var c = new FakeTurnArticle("c") { Priority = 0, SpawnIndex = 1 };
        var d = new FakeTurnArticle("d") { Priority = 1, SpawnIndex = 2 };

        var list = (List<ITurnAffectedArticle<ArticleBase>>)typeof(TurnHelper)
            .GetField("_turnAffectedArticleList", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(helper);
        list.AddRange(new[] { a, b, c, d });

        typeof(TurnHelper)
            .GetMethod("SortTurnAffectedArticles", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(helper, Array.Empty<object>());

        string orderedNames = string.Join("|", list.Cast<FakeTurnArticle>().Select(article => article.Name));
        CheckEqual("D.turn_order", orderedNames, "c|b|a|d");

        helper.Free();
    }

    private void TestSelectCanonicalPath()
    {
        GD.Print("[E] SelectCanonicalPath tie-break (count -> length^2 -> Y -> X)");
        var origin = new Vector2I(0, 0);

        var shortPath = MakePath((0, 0), (0, 5));
        var longPath = MakePath((0, 0), (1, 0), (1, 1));
        var selectedByCount = SkillUtil.SelectCanonicalPath(new[] { longPath, shortPath }, origin);
        CheckEqual("E.shorter_path_wins", selectedByCount[selectedByCount.Count - 1], new Vector2I(0, 5));

        var eastPath = MakePath((0, 0), (1, 0), (2, 0));   // 목적지 (2,0): len^2=4, Y=0
        var southPath = MakePath((0, 0), (0, 1), (0, 2));  // 목적지 (0,2): len^2=4, Y=2
        var selectedByY = SkillUtil.SelectCanonicalPath(new[] { southPath, eastPath }, origin);
        CheckEqual("E.y_tiebreak", selectedByY[selectedByY.Count - 1], new Vector2I(2, 0));

        var westPath = MakePath((0, 0), (-1, 0), (-2, 0)); // 목적지 (-2,0): len^2=4, Y=0, X=-2
        var selectedByX = SkillUtil.SelectCanonicalPath(new[] { eastPath, westPath }, origin);
        CheckEqual("E.x_tiebreak", selectedByX[selectedByX.Count - 1], new Vector2I(-2, 0));

        var tooShort = MakePath((0, 0));
        var selectedFiltered = SkillUtil.SelectCanonicalPath(new[] { tooShort, eastPath }, origin);
        CheckEqual("E.filters_short_candidates", selectedFiltered[selectedFiltered.Count - 1], new Vector2I(2, 0));

        var selectedEmpty = SkillUtil.SelectCanonicalPath(new[] { tooShort }, origin);
        CheckEqual("E.no_candidate_returns_null", selectedEmpty == null, true);
    }

    private static Godot.Collections.Array<Vector2I> MakePath(params (int X, int Y)[] points)
    {
        var path = new Godot.Collections.Array<Vector2I>();
        foreach (var (x, y) in points)
        {
            path.Add(new Vector2I(x, y));
        }
        return path;
    }

    private sealed class FakeTurnArticle : ITurnAffectedArticle<ArticleBase>
    {
        public FakeTurnArticle(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public int Priority { get; set; }
        public int SpawnIndex { get; set; }
        public TurnActionBase CurrentTurnAction { get; set; }
        public BehaviorTree BehaviorTree => null;

        public void ApplyTurnStartEffects() { }

        public BtStatus TurnPlay(double delta)
        {
            return BtStatus.Success;
        }
    }
}
#endif
