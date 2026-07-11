#if TOOLS
using System.Collections.Generic;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// WS-002 Step 2. 마스터 윈도우 승격 + 하단 로그 도크 + content area clamp를 headless로 검증한다.
///
/// - content rect = 마스터 − 좌측 메뉴바 − 하단 로그 도크 − 상단 inset (순수 geometry).
/// - 로그 도크 rect = 하단 전폭, content와 겹치지 않음.
/// - `log` floating window 제거(registry 4종), 로그는 `CanvasLayer/LogDock/LogView`(LogWindowController).
/// - content 밖으로 민 창을 GatherIntoContentArea가 메뉴/도크 비침범 위치로 회수(멱등).
/// - preset 적용 시 visible 창이 content 안에 남는다.
/// - 마스터 크기 변경 시 content rect 재계산(순수 함수 두 크기 비교).
/// </summary>
public partial class Ws002Step2MasterDockTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        GD.Print("[WS-002 Step2] Running master window + log dock tests...");

        TestContentRectExcludesMenuAndDock();
        TestLogDockIsBottomFullWidth();
        TestGatherIntoContentPure();
        TestRegistryDropsLogWindow();
        TestLogDockWiresController();
        TestGatherRecoversWindowIntoContent();
        TestPresetKeepsVisibleWindowsInsideContent();
        TestContentRectRecomputesOnResize();
        TestMasterResizeHookReGathers();
        TestPresetWindowsDoNotOverlap();

        GD.Print($"[WS-002 Step2] {_pass} passed, {_fail} failed.");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // content rect는 좌측 메뉴바 폭, 하단 도크 높이, 상단 inset을 제외한다.
    private void TestContentRectExcludesMenuAndDock()
    {
        GD.Print("[A] Content rect excludes menu, dock, top inset");
        var master = new Vector2I(1440, 960);
        int dock = Mathf.RoundToInt(master.Y * WorkspaceWindowManager.LogDockHeightRatio); // 192
        Rect2I content = WorkspaceGeometry.ComputeContentRect(
            master, WorkspaceWindowManager.MenuBarWidth, dock, WorkspaceWindowManager.ContentTopInset);

        CheckEqual("A.ContentRect", content, new Rect2I(200, 32, 1240, 736));

        // 마스터가 메뉴/도크보다 작아도(메뉴 200 > 마스터 100) content rect는 마스터 경계 안에서
        // 시작하고 끝나야 한다. 크기 ≥ 1, position ≥ 0, 오른쪽/아래 끝이 마스터를 넘지 않는다.
        var tinyMaster = new Vector2I(100, 100);
        Rect2I tiny = WorkspaceGeometry.ComputeContentRect(tinyMaster, 200, 20, 32);
        CheckTrue("A.TinyWidthClamped", tiny.Size.X >= 1);
        CheckTrue("A.TinyHeightClamped", tiny.Size.Y >= 1);
        CheckTrue("A.TinyPositionNonNegative", tiny.Position.X >= 0 && tiny.Position.Y >= 0);
        CheckTrue("A.TinyInsideMasterX", tiny.Position.X + tiny.Size.X <= tinyMaster.X);
        CheckTrue("A.TinyInsideMasterY", tiny.Position.Y + tiny.Size.Y <= tinyMaster.Y);
    }

    // 로그 도크는 하단 전폭이고 content area와 세로로 겹치지 않는다.
    private void TestLogDockIsBottomFullWidth()
    {
        GD.Print("[B] Log dock is bottom full-width, disjoint from content");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);

        Rect2I dock = manager.GetLogDockRect();
        Rect2I content = manager.GetContentRect();
        Vector2I master = manager.GetMasterSize();

        CheckEqual("B.DockFullWidth", dock.Size.X, master.X);
        CheckEqual("B.DockAtBottom", dock.Position.Y + dock.Size.Y, master.Y);
        CheckTrue("B.DockNonEmpty", dock.Size.Y > 0);
        // content 하단이 도크 상단을 넘지 않는다(침범 없음).
        CheckTrue("B.ContentAboveDock", content.Position.Y + content.Size.Y <= dock.Position.Y);

        Free(workspace);
    }

    // 순수 content gather: 안이면 불변, 밖이면 완전 포함으로 회수, 큰 창은 축소.
    private void TestGatherIntoContentPure()
    {
        GD.Print("[C] GatherIntoContent pure containment");
        var content = new Rect2I(200, 32, 1240, 736);

        var inside = new Rect2I(300, 100, 400, 300);
        CheckEqual("C.InsideUnchanged", WorkspaceGeometry.GatherIntoContent(inside, content), inside);

        var offTopLeft = new Rect2I(-500, -500, 400, 300);
        Rect2I recovered = WorkspaceGeometry.GatherIntoContent(offTopLeft, content);
        CheckTrue("C.RecoveredInside", WorkspaceGeometry.IsInside(recovered, content));
        CheckEqual("C.RecoveredPosition", recovered.Position, content.Position);

        var huge = new Rect2I(0, 0, 5000, 5000);
        Rect2I shrunk = WorkspaceGeometry.GatherIntoContent(huge, content);
        CheckEqual("C.ShrunkToContent", shrunk.Size, content.Size);
        CheckTrue("C.ShrunkInside", WorkspaceGeometry.IsInside(shrunk, content));
    }

    // WS-002: `log`는 floating registry에서 빠지고 managed 창은 4종이다.
    private void TestRegistryDropsLogWindow()
    {
        GD.Print("[D] Log dropped from floating registry (4 managed windows)");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);

        CheckEqual("D.RegistryCount", manager.Registry.Count, 3);
        foreach (string gone in new[] { "log", "situation", "weekly_action", "calendar" })
            CheckFalse($"D.NotRegistered_{gone}", manager.TryGetWindow(gone, out _));
        foreach (string id in new[] { "world", "tacticboard", "report" })
            CheckTrue($"D.Registered_{id}", manager.TryGetWindow(id, out _));

        Free(workspace);
    }

    // 로그는 마스터 하단 도크의 LogWindowController로 표시된다.
    private void TestLogDockWiresController()
    {
        GD.Print("[E] Log dock hosts the LogWindowController");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);

        var logView = workspace.GetNodeOrNull<LogWindowController>("CanvasLayer/LogDock/LogView");
        CheckTrue("E.LogViewIsController", logView != null);
        // floating Log 창은 더 이상 없다.
        CheckTrue("E.NoFloatingLogWindow", workspace.GetNodeOrNull("Windows/LogWindow") == null);

        Free(workspace);
    }

    // content 밖으로 민 창을 gather가 회수하고, 재호출은 멱등이다.
    private void TestGatherRecoversWindowIntoContent()
    {
        GD.Print("[F] Gather recovers an out-of-content window without invading menu/dock");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);

        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        manager.TryGetWindow("world", out WorkspaceWindow world);
        world.Position = new Vector2I(-5000, -5000);

        int moved = manager.GatherIntoContentArea();
        CheckTrue("F.AtLeastOneMoved", moved >= 1);

        Rect2I content = manager.GetContentRect();
        Rect2I decoRect = WorkspaceWindowManager.GetDecorationRect(world);
        CheckTrue("F.InsideContent", WorkspaceGeometry.IsInside(decoRect, content));
        // 보이는 영역(타이틀바 포함)이 메뉴바(좌측)·로그 도크(하단)를 침범하지 않는다.
        CheckTrue("F.NotIntoMenu", decoRect.Position.X >= content.Position.X);
        CheckTrue("F.NotIntoDock", decoRect.Position.Y + decoRect.Size.Y <= content.Position.Y + content.Size.Y);

        CheckEqual("F.SecondGatherIdempotent", manager.GatherIntoContentArea(), 0);

        Free(workspace);
    }

    // preset 적용 시 visible 창들이 content area 안에 배치된다.
    private void TestPresetKeepsVisibleWindowsInsideContent()
    {
        GD.Print("[G] Preset places visible windows inside content area");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);

        manager.ApplyPreset(WorkspaceLayoutPreset.Analysis); // 3창 모두 visible인 국면
        Rect2I content = manager.GetContentRect();

        foreach (string id in new[] { "world", "tacticboard", "report" })
        {
            if (!manager.TryGetWindow(id, out WorkspaceWindow w) || !w.Visible) continue;
            Rect2I rect = WorkspaceWindowManager.GetDecorationRect(w);
            CheckTrue($"G.{id}_inside", WorkspaceGeometry.IsInside(rect, content));
        }

        Free(workspace);
    }

    // 마스터 크기가 바뀌면 content rect가 재계산된다(순수 함수 두 크기 비교).
    private void TestContentRectRecomputesOnResize()
    {
        GD.Print("[H] Content rect recomputes when master resizes");
        int menu = WorkspaceWindowManager.MenuBarWidth;
        int inset = WorkspaceWindowManager.ContentTopInset;

        var small = new Vector2I(1440, 960);
        var large = new Vector2I(1920, 1080);
        Rect2I cSmall = WorkspaceGeometry.ComputeContentRect(
            small, menu, Mathf.RoundToInt(small.Y * WorkspaceWindowManager.LogDockHeightRatio), inset);
        Rect2I cLarge = WorkspaceGeometry.ComputeContentRect(
            large, menu, Mathf.RoundToInt(large.Y * WorkspaceWindowManager.LogDockHeightRatio), inset);

        CheckTrue("H.WidthGrew", cLarge.Size.X > cSmall.Size.X);
        CheckTrue("H.HeightGrew", cLarge.Size.Y > cSmall.Size.Y);
        // content 오른쪽 끝이 마스터 폭을 넘지 않는다.
        CheckEqual("H.RightEdgeTracksMaster", cLarge.Position.X + cLarge.Size.X, large.X);
    }

    // 마스터 리사이즈 signal이 발화하면 content 밖 창이 자동 회수된다(수동 gather 버튼 없이).
    private void TestMasterResizeHookReGathers()
    {
        GD.Print("[I] Master SizeChanged hook re-gathers windows into content");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);

        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        manager.TryGetWindow("world", out WorkspaceWindow world);
        world.Position = new Vector2I(-5000, -5000); // content 밖으로 밀어둔다.

        // 실제 리사이즈를 흉내내 root.SizeChanged를 발화한다. hook이 연결돼 있으면 gather가 돈다.
        GetTree().Root.EmitSignal(Godot.Window.SignalName.SizeChanged);

        Rect2I content = manager.GetContentRect();
        var rect = new Rect2I(world.Position, world.Size);
        CheckTrue("I.ReGatheredOnResize", WorkspaceGeometry.IsInside(rect, content));

        Free(workspace);
    }

    // 회귀: preset이 배치한 visible 창들의 '보이는 영역(타이틀바 포함)'이 서로 겹치지 않는다.
    // (GUI 버그: 임베디드 타이틀바가 이웃 창 content를 덮던 문제 — decoration 미반영 배치.)
    private void TestPresetWindowsDoNotOverlap()
    {
        GD.Print("[J] Visible preset windows' decoration rects do not overlap");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);
        manager.ApplyPreset(WorkspaceLayoutPreset.Analysis); // Report+TacticBoard+World 3창 병치

        var visible = new List<(string Id, Rect2I Deco)>();
        foreach (string id in new[] { "world", "tacticboard", "report" })
        {
            if (manager.TryGetWindow(id, out WorkspaceWindow w) && w.Visible)
                visible.Add((id, WorkspaceWindowManager.GetDecorationRect(w)));
        }

        CheckEqual("J.VisibleCount", visible.Count, 3);
        for (int i = 0; i < visible.Count; i++)
        for (int j = i + 1; j < visible.Count; j++)
        {
            Rect2I overlap = visible[i].Deco.Intersection(visible[j].Deco);
            bool disjoint = overlap.Size.X <= 0 || overlap.Size.Y <= 0;
            CheckTrue($"J.{visible[i].Id}_x_{visible[j].Id}_disjoint", disjoint);
        }

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
