#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// WS-001 Step 2. preset apply와 gather_windows를 검증한다.
/// geometry는 D7 순수 로직이므로 Window를 띄우지 않고 실패/경계 조건을 전부 단언한다.
/// </summary>
public partial class Ws001Step2GeometryTest : Node
{
    private int _failures;

    // 실측 멀티모니터 배치. screen 원점이 (0, 0)이 아니다.
    private static readonly Rect2I Screen0 = new(0, 542, 1920, 1032);
    private static readonly Rect2I Screen1 = new(1920, 774, 1920, 1032);
    private static readonly Rect2I Screen2 = new(3840, 0, 1440, 2512);
    private static readonly List<Rect2I> Screens = new() { Screen0, Screen1, Screen2 };

    public override void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[WS-001 Step2] Running preset/gather geometry tests...");
        try
        {
            TestResolveTargetScreenByCenter();
            TestResolveTargetScreenFallsBackToPrimary();
            TestIsReachableRejectsOffScreenTitleBar();
            TestClampPullsWindowBack();
            TestClampShrinksWindowLargerThanWorkArea();
            TestGatherIsIdempotentAndPreservesReachableWindows();
            TestRegressionStep1TitleBarBug();
            TestApplyPresetShowsAndHides();
            TestApplyPresetDoesNotRecreateWorldWindow();
            TestApplyUnknownPresetFailsClosed();
            TestPresetOffsetsAreRelativeToContentArea();
            TestGatherRecoversForcedOffScreenWindow();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[WS-001 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0)
        {
            GD.Print("[WS-001 Step2] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[WS-001 Step2] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    private void CheckTrue(string name, bool actual) => CheckEqual(name, actual, true);
    private void CheckFalse(string name, bool actual) => CheckEqual(name, actual, false);

    private static void ExpectDiagnosticLog(string reason, Action body)
    {
        GD.Print($"  EXPECTED-DIAGNOSTIC-BEGIN: {reason}");
        body();
        GD.Print($"  EXPECTED-DIAGNOSTIC-END: {reason}");
    }

    // [A] 대상 screen은 decoration 포함 rect의 중심점이 포함된 screen이다.
    private void TestResolveTargetScreenByCenter()
    {
        GD.Print("[A] Target screen resolves by decoration rect center");
        CheckEqual("A.OnScreen0", WorkspaceGeometry.ResolveTargetScreen(new Rect2I(100, 600, 400, 300), Screens, 1), 0);
        CheckEqual("A.OnScreen1", WorkspaceGeometry.ResolveTargetScreen(new Rect2I(2500, 1000, 400, 300), Screens, 0), 1);
        CheckEqual("A.OnScreen2", WorkspaceGeometry.ResolveTargetScreen(new Rect2I(4000, 100, 400, 300), Screens, 0), 2);
    }

    // [B] 어떤 screen에도 중심점이 없으면 primary로 fallback한다.
    private void TestResolveTargetScreenFallsBackToPrimary()
    {
        GD.Print("[B] No screen contains center -> primary fallback");
        // (40, 80)은 screen0 위쪽 빈 가상 좌표다. Step 1 버그가 났던 자리다.
        var deadSpace = new Rect2I(32, 49, 656, 519);
        CheckEqual("B.FallbackToPrimary1", WorkspaceGeometry.ResolveTargetScreen(deadSpace, Screens, 1), 1);
        CheckEqual("B.FallbackToPrimary0", WorkspaceGeometry.ResolveTargetScreen(deadSpace, Screens, 0), 0);
        CheckEqual("B.OutOfRangePrimaryClampsToZero",
            WorkspaceGeometry.ResolveTargetScreen(deadSpace, Screens, 99), 0);
        CheckEqual("B.EmptyScreenList", WorkspaceGeometry.ResolveTargetScreen(deadSpace, new List<Rect2I>(), 0), 0);
    }

    // [C] 타이틀바가 잘리면 잡을 수 없다.
    private void TestIsReachableRejectsOffScreenTitleBar()
    {
        GD.Print("[C] IsReachable requires a grabbable title bar");
        CheckTrue("C.FullyInside", WorkspaceGeometry.IsReachable(new Rect2I(100, 600, 400, 300), Screen0));

        // 타이틀바가 work area 위로 넘어감.
        CheckFalse("C.TitleBarAbove", WorkspaceGeometry.IsReachable(new Rect2I(100, 520, 400, 300), Screen0));

        // 창 아래쪽만 살짝 걸침 = 타이틀바가 화면 위에 없음.
        CheckFalse("C.OnlyBottomSliverVisible", WorkspaceGeometry.IsReachable(new Rect2I(100, 300, 400, 300), Screen0));

        // 타이틀바가 work area 아래로 밀림.
        CheckFalse("C.TitleBarBelow", WorkspaceGeometry.IsReachable(new Rect2I(100, 1570, 400, 300), Screen0));

        // 가로로 64px 미만만 보임.
        CheckFalse("C.TooNarrowToGrabRight",
            WorkspaceGeometry.IsReachable(new Rect2I(1900, 600, 400, 300), Screen0));
        CheckFalse("C.TooNarrowToGrabLeft",
            WorkspaceGeometry.IsReachable(new Rect2I(-360, 600, 400, 300), Screen0));
        CheckTrue("C.ExactlyMinGrabWidth",
            WorkspaceGeometry.IsReachable(new Rect2I(1920 - 64, 600, 400, 300), Screen0));

        // 다른 screen 안에 있으면 그 screen 기준으로는 잡을 수 있다.
        CheckTrue("C.ReachableOnScreen1", WorkspaceGeometry.IsReachable(new Rect2I(2000, 800, 400, 300), Screen1));
        CheckFalse("C.SameRectNotReachableOnScreen0",
            WorkspaceGeometry.IsReachable(new Rect2I(2000, 800, 400, 300), Screen0));
    }

    // [D] 회수는 work area 안으로 끌어온다.
    private void TestClampPullsWindowBack()
    {
        GD.Print("[D] Clamp pulls the window into the work area");
        Rect2I clamped = WorkspaceGeometry.ClampIntoWorkArea(new Rect2I(-500, -500, 400, 300), Screen0);
        CheckEqual("D.ClampedPosition", clamped.Position, new Vector2I(0, 542));
        CheckEqual("D.SizePreserved", clamped.Size, new Vector2I(400, 300));
        CheckTrue("D.NowReachable", WorkspaceGeometry.IsReachable(clamped, Screen0));

        Rect2I bottomRight = WorkspaceGeometry.ClampIntoWorkArea(new Rect2I(5000, 5000, 400, 300), Screen0);
        CheckEqual("D.ClampedToBottomRight", bottomRight.Position, new Vector2I(1520, 1274));
        CheckTrue("D.BottomRightReachable", WorkspaceGeometry.IsReachable(bottomRight, Screen0));
    }

    // [E] work area보다 큰 창은 먼저 줄인다(tiny screen).
    private void TestClampShrinksWindowLargerThanWorkArea()
    {
        GD.Print("[E] Windows larger than the work area shrink first");
        var tinyScreen = new Rect2I(0, 0, 320, 240);
        Rect2I clamped = WorkspaceGeometry.ClampIntoWorkArea(new Rect2I(100, 100, 1920, 1080), tinyScreen);
        CheckEqual("E.ShrunkToWorkArea", clamped.Size, new Vector2I(320, 240));
        CheckEqual("E.PositionedAtOrigin", clamped.Position, new Vector2I(0, 0));
        CheckTrue("E.TinyScreenReachable", WorkspaceGeometry.IsReachable(clamped, tinyScreen));

        // 타이틀바보다 낮은 창도 잡을 수 있다고 판정해야 한다(높이 clamp).
        var shortWindow = new Rect2I(0, 0, 320, 20);
        CheckTrue("E.ShortWindowReachable", WorkspaceGeometry.IsReachable(shortWindow, tinyScreen));
    }

    // [F] gather는 이미 잡을 수 있는 창을 건드리지 않고, 두 번 불러도 결과가 같다.
    private void TestGatherIsIdempotentAndPreservesReachableWindows()
    {
        GD.Print("[F] Gather is idempotent and leaves reachable windows alone");
        var reachable = new Rect2I(100, 600, 400, 300);
        CheckEqual("F.ReachableUntouched", WorkspaceGeometry.Gather(reachable, Screen0), reachable);

        Rect2I once = WorkspaceGeometry.Gather(new Rect2I(-500, -500, 400, 300), Screen0);
        Rect2I twice = WorkspaceGeometry.Gather(once, Screen0);
        CheckEqual("F.Idempotent", twice, once);
    }

    // [G] Step 1 실측 버그 회귀. client rect만 클램프하면 타이틀바가 화면 밖에 남는다.
    private void TestRegressionStep1TitleBarBug()
    {
        GD.Print("[G] Regression: Step 1 title bar off-screen bug");
        // Godot이 client rect (40, 80) -> (40, 542)로 올린 뒤 decoration은 (32, 511)에 남았다.
        var buriedTitleBar = new Rect2I(32, 511, 656, 519);
        CheckFalse("G.BuriedTitleBarNotReachable", WorkspaceGeometry.IsReachable(buriedTitleBar, Screen0));

        Rect2I recovered = WorkspaceGeometry.Gather(buriedTitleBar, Screen0);
        CheckTrue("G.RecoveredReachable", WorkspaceGeometry.IsReachable(recovered, Screen0));
        CheckTrue("G.TitleBarInsideWorkArea", recovered.Position.Y >= Screen0.Position.Y);
        CheckEqual("G.RecoveredTop", recovered.Position.Y, 542);
    }

    // [H] preset이 필요한 창을 보이고 불필요한 창을 숨긴다.
    private void TestApplyPresetShowsAndHides()
    {
        GD.Print("[H] Preset shows required windows and hides the rest");
        Fixture fixture = MakeFixture();

        // WS-002 창 다이어트: outgame=World만, battle=World+TacticBoard, analysis=Report+TacticBoard+World.
        CheckTrue("H.OutgameApplied", fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Outgame));
        CheckTrue("H.Outgame_world_visible", fixture.Manager.IsWindowVisible("world"));
        CheckFalse("H.Outgame_tacticboard_hidden", fixture.Manager.IsWindowVisible("tacticboard"));
        CheckFalse("H.Outgame_report_hidden", fixture.Manager.IsWindowVisible("report"));

        CheckTrue("H.BattleApplied", fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Battle));
        CheckTrue("H.Battle_world_visible", fixture.Manager.IsWindowVisible("world"));
        CheckTrue("H.Battle_tacticboard_visible", fixture.Manager.IsWindowVisible("tacticboard"));
        CheckFalse("H.Battle_report_hidden", fixture.Manager.IsWindowVisible("report"));

        CheckTrue("H.AnalysisApplied", fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Analysis));
        CheckTrue("H.Analysis_report_visible", fixture.Manager.IsWindowVisible("report"));
        CheckTrue("H.Analysis_tacticboard_visible", fixture.Manager.IsWindowVisible("tacticboard"));
        CheckTrue("H.Analysis_world_visible", fixture.Manager.IsWindowVisible("world"));

        fixture.Dispose();
    }

    // [I] D4. World 창은 새로 만들지 않고 content_mode/position/size만 바뀐다.
    private void TestApplyPresetDoesNotRecreateWorldWindow()
    {
        GD.Print("[I] World window is never recreated across presets");
        Fixture fixture = MakeFixture();
        fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        fixture.Manager.TryGetWindow("world", out WorkspaceWindow world);
        ulong instanceId = world.GetInstanceId();
        CheckEqual("I.HubContentMode", world.ContentMode, WorldContentMode.Hub);
        Vector2I hubSize = world.Size;

        fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Battle);
        fixture.Manager.TryGetWindow("world", out WorkspaceWindow worldAfterBattle);
        CheckEqual("I.SameInstanceAfterBattle", worldAfterBattle.GetInstanceId(), instanceId);
        CheckEqual("I.BattleContentMode", worldAfterBattle.ContentMode, WorldContentMode.Battle);
        CheckFalse("I.SizeChanged", worldAfterBattle.Size == hubSize);

        fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Analysis);
        fixture.Manager.TryGetWindow("world", out WorkspaceWindow worldAfterAnalysis);
        CheckEqual("I.SameInstanceAfterAnalysis", worldAfterAnalysis.GetInstanceId(), instanceId);
        CheckEqual("I.ReplayContentMode", worldAfterAnalysis.ContentMode, WorldContentMode.Replay);
        CheckEqual("I.RegistryStillThree", fixture.Manager.Registry.Count, 3);

        fixture.Dispose();
    }

    // [J] unknown preset은 아무것도 바꾸지 않는다.
    private void TestApplyUnknownPresetFailsClosed()
    {
        GD.Print("[J] Unknown preset fails closed");
        Fixture fixture = MakeFixture();
        fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Battle);
        bool reportVisibleBefore = fixture.Manager.IsWindowVisible("report"); // battle에서 hidden
        fixture.Manager.TryGetWindow("world", out WorkspaceWindow world);
        Vector2I positionBefore = world.Position;
        string contentModeBefore = world.ContentMode;

        ExpectDiagnosticLog("unknown preset push_error", () =>
        {
            CheckFalse("J.UnknownPresetReturnsFalse", fixture.Manager.ApplyPreset("does_not_exist"));
            CheckFalse("J.NullPresetReturnsFalse", fixture.Manager.ApplyPreset(null));
        });

        CheckEqual("J.VisibilityUnchanged", fixture.Manager.IsWindowVisible("report"), reportVisibleBefore);
        CheckEqual("J.PositionUnchanged", world.Position, positionBefore);
        CheckEqual("J.ContentModeUnchanged", world.ContentMode, contentModeBefore);

        // preset이 registry에 없는 창을 참조해도 나머지 적용을 막지 않는다.
        // (preset에 있는 `tacticboard`를 등록 해제해 missing-window 경로를 검증한다.)
        fixture.Manager.UnregisterWindow("tacticboard");
        ExpectDiagnosticLog("unknown window in preset push_warning",
            () => CheckTrue("J.AppliesDespiteMissingWindow", fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Outgame)));
        CheckTrue("J.SurvivingWindowApplied", fixture.Manager.IsWindowVisible("world"));

        fixture.Dispose();
    }

    // [K] WS-002: preset 좌표는 screen work area가 아니라 마스터 content area 원점 기준 client 오프셋이다.
    private void TestPresetOffsetsAreRelativeToContentArea()
    {
        GD.Print("[K] Preset offsets are relative to the master content area origin");
        Fixture fixture = MakeFixture();
        Rect2I content = fixture.Manager.GetContentRect();

        fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        fixture.Manager.TryGetWindow("world", out WorkspaceWindow world);

        WorkspaceLayoutPreset.TryGetPreset(WorkspaceLayoutPreset.Outgame,
            out IReadOnlyDictionary<string, WindowLayout> layouts);
        Rect2I expected = WorkspaceLayoutPreset.ResolveRect(layouts["world"].NormalizedRect, content);

        // 보이는 영역(타이틀바 포함 decoration rect) = 정규화 비율(E-7)을 content 기준으로 resolve한 rect.
        Rect2I decoRect = WorkspaceWindowManager.GetDecorationRect(world);
        CheckEqual("K.DecoRectIsContentResolved", decoRect, expected);
        CheckTrue("K.InsideContentArea", WorkspaceGeometry.IsInside(decoRect, content));

        fixture.Dispose();
    }

    // [L] 화면 밖으로 강제로 밀어낸 창을 gather가 회수한다.
    private void TestGatherRecoversForcedOffScreenWindow()
    {
        GD.Print("[L] Gather recovers a forcibly off-screen window");
        Fixture fixture = MakeFixture();
        fixture.Manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        fixture.Manager.TryGetWindow("world", out WorkspaceWindow world);

        world.Position = new Vector2I(-5000, -5000);
        int moved = fixture.Manager.GatherWindows();
        CheckTrue("L.AtLeastOneWindowMoved", moved >= 1);

        Rect2I workArea = fixture.Manager.GetWorkArea();
        Rect2I decoRect = WorkspaceWindowManager.GetDecorationRect(world);
        CheckTrue("L.ReachableAfterGather", WorkspaceGeometry.IsReachable(decoRect, workArea));

        // 이미 회수된 창은 다시 옮기지 않는다.
        CheckEqual("L.SecondGatherMovesNothing", fixture.Manager.GatherWindows(), 0);

        fixture.Dispose();
    }

    private Fixture MakeFixture()
    {
        var scene = GD.Load<PackedScene>("res://Assets/Scenes/workspace.tscn");
        Node workspace = scene.Instantiate();
        // 자동 배치 복원을 꺼서 preset/gather를 명시적으로만 구동한다.
        workspace.Set("_restoreLayoutOnReady", false);
        AddChild(workspace);
        return new Fixture(this, workspace);
    }

    private sealed class Fixture
    {
        private readonly Node _parent;
        private readonly Node _workspace;

        public Fixture(Node parent, Node workspace)
        {
            _parent = parent;
            _workspace = workspace;
            Manager = workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");
        }

        public WorkspaceWindowManager Manager { get; }

        public void Dispose()
        {
            _parent.RemoveChild(_workspace);
            _workspace.QueueFree();
        }
    }
}
#endif
