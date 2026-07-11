#if TOOLS
using System.Collections.Generic;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// WS-002 Step 3. 레이아웃 슬롯 스냅을 headless로 검증한다(순수 함수 + manager driver).
///
/// 기본 계약(Step 0 리뷰 Finding 3): 자석 거리 = content 짧은 변 6%, tie-break = 정규 순서(Y→X→id),
/// hidden slot 후보 제외, disable flag 1개. applied rect는 content area를 벗어나지 않는다.
/// </summary>
public partial class Ws002Step3SlotSnapTest : Node
{
    private int _pass;
    private int _fail;

    private static readonly Rect2I Content = new(200, 32, 1240, 736);

    public override void _Ready()
    {
        GD.Print("[WS-002 Step3] Running slot snap tests...");

        TestResolveSlotsVisibilityAndContentRelative();
        TestSnapWithinAndOutsideDistance();
        TestSnapPicksNearest();
        TestTieBreakCanonicalOrder();
        TestDisabledSnapYieldsNoCandidate();
        TestAppliedRectClampedToContent();
        TestManagerPreviewAndCommit();
        TestPresetSwitchChangesSlots();

        GD.Print($"[WS-002 Step3] {_pass} passed, {_fail} failed.");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // ResolveSlots는 visible 창만 슬롯으로 내고(hidden 제외), rect는 content 원점 기준이며 content 안이다.
    private void TestResolveSlotsVisibilityAndContentRelative()
    {
        GD.Print("[A] ResolveSlots: visible-only, content-relative, inside content");
        // outgame은 World만 visible → 슬롯 1개, World가 content 전체를 채운다.
        Dictionary<string, Rect2I> outgame = WorkspaceLayoutPreset.ResolveSlots(WorkspaceLayoutPreset.Outgame, Content);
        CheckEqual("A.OutgameSlotCount", outgame.Count, 1);
        CheckTrue("A.OutgameHasWorld", outgame.ContainsKey("world"));
        CheckEqual("A.WorldSlotFillsContent", outgame["world"], Content);

        // battle은 world+tacticboard visible → 슬롯 2개, report 제외(hidden slot).
        Dictionary<string, Rect2I> battle = WorkspaceLayoutPreset.ResolveSlots(WorkspaceLayoutPreset.Battle, Content);
        CheckEqual("A.BattleSlotCount", battle.Count, 2);
        CheckTrue("A.BattleHasTacticBoard", battle.ContainsKey("tacticboard"));
        CheckFalse("A.BattleExcludesReport", battle.ContainsKey("report"));
        foreach (KeyValuePair<string, Rect2I> s in battle)
            CheckTrue($"A.{s.Key}_inside", WorkspaceGeometry.IsInside(s.Value, Content));

        // analysis는 3창 모두 visible → 슬롯 3개.
        CheckEqual("A.AnalysisSlotCount",
            WorkspaceLayoutPreset.ResolveSlots(WorkspaceLayoutPreset.Analysis, Content).Count, 3);

        // unknown preset은 빈 dict(fail-closed).
        CheckEqual("A.UnknownEmpty", WorkspaceLayoutPreset.ResolveSlots("nope", Content).Count, 0);
    }

    // 자석 거리 안이면 후보, 밖이면 후보 없음. 경계(정확히 거리)는 포함.
    private void TestSnapWithinAndOutsideDistance()
    {
        GD.Print("[B] Candidate within distance, none outside");
        var slots = new Dictionary<string, Rect2I> { ["a"] = new(300, 100, 200, 150) }; // center (400,175)
        int dist = 50;

        // center (410,180): |Δ|≈11 < 50 → 후보.
        var near = new Rect2I(310, 105, 200, 150);
        WorkspaceGeometry.SlotSnapResult r1 = WorkspaceGeometry.FindSlotSnap(near, slots, dist, Content, false);
        CheckTrue("B.NearHasCandidate", r1.HasCandidate);
        CheckEqual("B.NearSlotId", r1.SlotId, "a");

        // center (600,400): 멀다 → 후보 없음.
        var far = new Rect2I(500, 325, 200, 150);
        WorkspaceGeometry.SlotSnapResult r2 = WorkspaceGeometry.FindSlotSnap(far, slots, dist, Content, false);
        CheckFalse("B.FarNoCandidate", r2.HasCandidate);

        // 정확히 거리(dx=50, dy=0)면 포함(<=).
        var edge = new Rect2I(350, 100, 200, 150); // center (450,175), Δx=50
        CheckTrue("B.EdgeInclusive", WorkspaceGeometry.FindSlotSnap(edge, slots, dist, Content, false).HasCandidate);
    }

    // 여러 후보 중 가장 가까운 슬롯을 고른다.
    private void TestSnapPicksNearest()
    {
        GD.Print("[C] Picks the nearest slot");
        var slots = new Dictionary<string, Rect2I>
        {
            ["a"] = new(0, 0, 100, 100),     // center (50,50)
            ["b"] = new(100, 0, 100, 100),   // center (150,50)
        };
        // center (60,50): a까지 10, b까지 90 → a.
        var dragged = new Rect2I(10, 0, 100, 100);
        WorkspaceGeometry.SlotSnapResult r = WorkspaceGeometry.FindSlotSnap(dragged, slots, 200, Content, false);
        CheckEqual("C.NearestIsA", r.SlotId, "a");
    }

    // 같은 거리면 정규 순서(Y→X→id)로 stable하게 고른다.
    private void TestTieBreakCanonicalOrder()
    {
        GD.Print("[D] Equal distance -> canonical tie-break");
        // 중심 Y가 다른 두 슬롯이 dragged에서 등거리 → 작은 Y가 이긴다.
        var slotsY = new Dictionary<string, Rect2I>
        {
            ["low"] = new(60, 60, 80, 80),   // center (100,100)
            ["high"] = new(60, 140, 80, 80),  // center (100,180)
        };
        var draggedMid = new Rect2I(60, 100, 80, 80); // center (100,140): 위/아래 각각 40
        WorkspaceGeometry.SlotSnapResult ry = WorkspaceGeometry.FindSlotSnap(draggedMid, slotsY, 100, Content, false);
        CheckEqual("D.SmallerYWins", ry.SlotId, "low");

        // 중심이 완전히 같은 두 슬롯(거리 0 동률) → id ordinal이 작은 것.
        var slotsId = new Dictionary<string, Rect2I>
        {
            ["zeta"] = new(100, 100, 40, 40),
            ["alpha"] = new(100, 100, 40, 40),
        };
        var draggedSame = new Rect2I(100, 100, 40, 40); // center (120,120)
        WorkspaceGeometry.SlotSnapResult ri = WorkspaceGeometry.FindSlotSnap(draggedSame, slotsId, 100, Content, false);
        CheckEqual("D.SmallerIdWins", ri.SlotId, "alpha");
    }

    // disable flag가 켜지면 거리 안이라도 후보 없음.
    private void TestDisabledSnapYieldsNoCandidate()
    {
        GD.Print("[E] Disabled snap -> no candidate even in range");
        var slots = new Dictionary<string, Rect2I> { ["a"] = new(300, 100, 200, 150) };
        var near = new Rect2I(310, 105, 200, 150);
        WorkspaceGeometry.SlotSnapResult r = WorkspaceGeometry.FindSlotSnap(near, slots, 50, Content, true);
        CheckFalse("E.DisabledNoCandidate", r.HasCandidate);
    }

    // applied rect는 슬롯이 content를 삐져나가도 content 안으로 clamp된다.
    private void TestAppliedRectClampedToContent()
    {
        GD.Print("[F] Applied rect clamped inside content");
        var small = new Rect2I(0, 0, 100, 100);
        var slots = new Dictionary<string, Rect2I> { ["s"] = new(60, 60, 80, 80) }; // (60,60)~(140,140) content 밖
        var dragged = new Rect2I(90, 90, 20, 20); // center (100,100) = slot center
        WorkspaceGeometry.SlotSnapResult r = WorkspaceGeometry.FindSlotSnap(dragged, slots, 50, small, false);
        CheckTrue("F.HasCandidate", r.HasCandidate);
        CheckTrue("F.AppliedInsideContent", WorkspaceGeometry.IsInside(r.AppliedRect, small));
    }

    // 실제 scene: preview는 후보/하이라이트를 주고, commit은 창을 슬롯 rect로 정렬한다. disable는 no-op.
    private void TestManagerPreviewAndCommit()
    {
        GD.Print("[G] Manager preview + commit against a real scene");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);
        manager.ApplyPreset(WorkspaceLayoutPreset.Analysis); // report/tacticboard/world 3슬롯

        Rect2I tbSlot = manager.GetCurrentSlots()["tacticboard"];

        // report 창을 tacticboard 슬롯 근처(대략 슬롯의 client 위치)로 옮긴다.
        manager.TryGetWindow("report", out WorkspaceWindow report);
        report.Position = tbSlot.Position + new Vector2I(8, 32);

        WorkspaceGeometry.SlotSnapResult preview = manager.PreviewSlotSnap("report", false);
        CheckTrue("G.PreviewHasCandidate", preview.HasCandidate);
        CheckEqual("G.PreviewSlotIsTacticBoard", preview.SlotId, "tacticboard");

        // disable면 후보 없음(스냅 무시).
        CheckFalse("G.DisabledNoCandidate", manager.PreviewSlotSnap("report", true).HasCandidate);

        // commit → report의 보이는 영역이 tacticboard 슬롯 rect로 정렬.
        CheckTrue("G.CommitSnapped", manager.CommitSlotSnap("report", false));
        CheckEqual("G.ReportSnappedDecoRect", WorkspaceWindowManager.GetDecorationRect(report), tbSlot);

        // disable로는 commit이 no-op.
        report.Position = tbSlot.Position + new Vector2I(8, 32);
        CheckFalse("G.DisabledCommitNoOp", manager.CommitSlotSnap("report", true));

        Free(workspace);
    }

    // preset 전환 시 슬롯 후보 집합이 새 국면 기준으로 바뀐다.
    private void TestPresetSwitchChangesSlots()
    {
        GD.Print("[H] Preset switch changes slot candidates");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);

        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        CheckEqual("H.OutgameCurrent", manager.CurrentPresetId, WorkspaceLayoutPreset.Outgame);
        CheckEqual("H.OutgameSlots", manager.GetCurrentSlots().Count, 1);

        manager.ApplyPreset(WorkspaceLayoutPreset.Battle);
        CheckEqual("H.BattleCurrent", manager.CurrentPresetId, WorkspaceLayoutPreset.Battle);
        CheckEqual("H.BattleSlots", manager.GetCurrentSlots().Count, 2);

        manager.ApplyPreset(WorkspaceLayoutPreset.Analysis);
        CheckEqual("H.AnalysisSlots", manager.GetCurrentSlots().Count, 3);

        Free(workspace);
    }

    private WorkspaceWindowManager MakeManager(out Node workspace)
    {
        var scene = GD.Load<PackedScene>("res://Assets/Scenes/workspace.tscn");
        workspace = scene.Instantiate();
        workspace.Set("_restoreLayoutOnReady", false);
        AddChild(workspace);
        return workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");
    }

    private void Free(Node workspace)
    {
        RemoveChild(workspace);
        workspace.QueueFree();
    }

    private void CheckTrue(string name, bool condition)
    {
        if (condition) { _pass++; GD.Print($"  PASS: {name}"); }
        else { _fail++; GD.PrintErr($"  FAIL: {name}"); }
    }

    private void CheckFalse(string name, bool condition) => CheckTrue(name, !condition);

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        bool equal = actual?.Equals(expected) ?? expected == null;
        if (equal) { _pass++; GD.Print($"  PASS: {name} = {actual}"); }
        else { _fail++; GD.PrintErr($"  FAIL: {name}: expected {expected}, got {actual}"); }
    }
}
#endif
