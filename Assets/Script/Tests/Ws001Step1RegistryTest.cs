#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// WS-001 Step 1 완료 조건 중 Window를 실제로 띄우지 않고 관찰 가능한 것들을 검증한다.
/// - registry 등록/중복/stale
/// - close 요청 = hide 정책, 재표시
/// - duplicate open 시 position 미리셋
/// - unknown id fail-closed
/// - 메인 창 최소화/복원 동기화(숨긴 창은 숨긴 채로)
/// </summary>
public partial class Ws001Step1RegistryTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[WS-001 Step1] Running workspace registry tests...");
        try
        {
            TestAutoRegistersChildWindows();
            TestEmptyIdIsRejected();
            TestDuplicateIdIsRejected();
            TestSameInstanceReregistrationMakesNoDuplicate();
            TestUnknownIdFailsClosed();
            TestShowHideToggle();
            TestDuplicateOpenDoesNotResetPosition();
            TestCloseRequestHidesAndKeepsRegistry();
            TestFreedWindowIsPruned();
            TestMinimizeRestoreKeepsHiddenWindowsHidden();
            TestWorkspaceSceneWiring();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[WS-001 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0)
        {
            GD.Print("[WS-001 Step1] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[WS-001 Step1] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    // fail-closed 경로는 GD.PushError/PushWarning으로 배선 실수를 보고한다(프로덕션에서 필요한 동작이다).
    // 그 로그가 테스트 실패로 오독되지 않도록, 기대되는 진단 로그 앞뒤에 마커를 남긴다.
    private static void ExpectDiagnosticLog(string reason, Action body)
    {
        GD.Print($"  EXPECTED-DIAGNOSTIC-BEGIN: {reason}");
        body();
        GD.Print($"  EXPECTED-DIAGNOSTIC-END: {reason}");
    }

    // [A] _windowRoot의 WorkspaceWindow 자식이 자동 등록된다.
    private void TestAutoRegistersChildWindows()
    {
        GD.Print("[A] Manager auto-registers WorkspaceWindow children");
        var fixture = MakeFixture("world", "situation", "log");

        CheckEqual("A.RegistryCount", fixture.Manager.Registry.Count, 3);
        CheckEqual("A.LookupWorld", fixture.Manager.TryGetWindow("world", out _), true);
        CheckEqual("A.LookupLog", fixture.Manager.TryGetWindow("log", out _), true);

        fixture.Dispose();
    }

    // [B] 빈 WindowId는 등록되지 않는다.
    private void TestEmptyIdIsRejected()
    {
        GD.Print("[B] Empty WindowId is rejected");
        var fixture = MakeFixture();
        var window = new WorkspaceWindow { Name = "NoId", WindowId = "" };

        ExpectDiagnosticLog("empty WindowId push_error",
            () => CheckEqual("B.RegisterReturnsFalse", fixture.Manager.RegisterWindow(window), false));
        CheckEqual("B.RegistryEmpty", fixture.Manager.Registry.Count, 0);

        window.Free();
        fixture.Dispose();
    }

    // [C] 같은 id의 다른 인스턴스는 거부되고 첫 등록이 유지된다.
    private void TestDuplicateIdIsRejected()
    {
        GD.Print("[C] Duplicate WindowId is rejected, first registration wins");
        var fixture = MakeFixture("world");
        WorkspaceWindow first = fixture.Windows["world"];
        var impostor = new WorkspaceWindow { Name = "WorldImpostor", WindowId = "world" };

        ExpectDiagnosticLog("duplicate WindowId push_error",
            () => CheckEqual("C.RegisterReturnsFalse", fixture.Manager.RegisterWindow(impostor), false));
        CheckEqual("C.RegistryCount", fixture.Manager.Registry.Count, 1);
        fixture.Manager.TryGetWindow("world", out WorkspaceWindow resolved);
        CheckEqual("C.FirstRegistrationKept", ReferenceEquals(resolved, first), true);

        impostor.Free();
        fixture.Dispose();
    }

    // [D] 같은 인스턴스를 다시 등록해도 entry가 늘지 않는다.
    private void TestSameInstanceReregistrationMakesNoDuplicate()
    {
        GD.Print("[D] Repeated registration of the same instance makes no duplicate entry");
        var fixture = MakeFixture("world");

        CheckEqual("D.ReregisterReturnsFalse", fixture.Manager.RegisterWindow(fixture.Windows["world"]), false);
        CheckEqual("D.RegistryCount", fixture.Manager.Registry.Count, 1);

        fixture.Dispose();
    }

    // [E] 미등록 id는 모든 API에서 fail-closed다.
    private void TestUnknownIdFailsClosed()
    {
        GD.Print("[E] Unknown window id fails closed");
        var fixture = MakeFixture("world");

        CheckEqual("E.TryGet", fixture.Manager.TryGetWindow("tactic_board", out _), false);
        CheckEqual("E.Show", fixture.Manager.ShowWindow("tactic_board"), false);
        CheckEqual("E.Hide", fixture.Manager.HideWindow("tactic_board"), false);
        CheckEqual("E.Toggle", fixture.Manager.ToggleWindow("tactic_board"), false);
        CheckEqual("E.IsVisible", fixture.Manager.IsWindowVisible("tactic_board"), false);
        CheckEqual("E.NullId", fixture.Manager.ShowWindow(null), false);

        fixture.Dispose();
    }

    // [F] show/hide/toggle이 visible 상태를 뒤집는다.
    private void TestShowHideToggle()
    {
        GD.Print("[F] show/hide/toggle drive visibility");
        var fixture = MakeFixture("situation");

        CheckEqual("F.StartsHidden", fixture.Manager.IsWindowVisible("situation"), false);
        CheckEqual("F.ShowReturnsTrue", fixture.Manager.ShowWindow("situation"), true);
        CheckEqual("F.VisibleAfterShow", fixture.Manager.IsWindowVisible("situation"), true);
        CheckEqual("F.HideReturnsTrue", fixture.Manager.HideWindow("situation"), true);
        CheckEqual("F.HiddenAfterHide", fixture.Manager.IsWindowVisible("situation"), false);
        fixture.Manager.ToggleWindow("situation");
        CheckEqual("F.VisibleAfterToggle", fixture.Manager.IsWindowVisible("situation"), true);
        fixture.Manager.ToggleWindow("situation");
        CheckEqual("F.HiddenAfterSecondToggle", fixture.Manager.IsWindowVisible("situation"), false);

        fixture.Dispose();
    }

    // [G] 이미 보이는 창에 ShowWindow를 다시 불러도 position/size가 되돌아가지 않는다(duplicate open).
    private void TestDuplicateOpenDoesNotResetPosition()
    {
        GD.Print("[G] Duplicate open keeps user position/size");
        var fixture = MakeFixture("world");
        WorkspaceWindow window = fixture.Windows["world"];

        fixture.Manager.ShowWindow("world");
        window.Position = new Vector2I(321, 123);
        window.Size = new Vector2I(500, 400);

        CheckEqual("G.SecondShowReturnsTrue", fixture.Manager.ShowWindow("world"), true);
        CheckEqual("G.PositionPreserved", window.Position, new Vector2I(321, 123));
        CheckEqual("G.SizePreserved", window.Size, new Vector2I(500, 400));
        CheckEqual("G.StillOneEntry", fixture.Manager.Registry.Count, 1);

        fixture.Dispose();
    }

    // [H] OS close 요청은 free가 아니라 hide다. registry는 유지되고 다시 표시할 수 있다.
    private void TestCloseRequestHidesAndKeepsRegistry()
    {
        GD.Print("[H] close_requested hides instead of freeing");
        var fixture = MakeFixture("log");
        WorkspaceWindow window = fixture.Windows["log"];
        fixture.Manager.ShowWindow("log");

        window.EmitSignal(Godot.Window.SignalName.CloseRequested);

        CheckEqual("H.HiddenAfterClose", window.Visible, false);
        CheckEqual("H.NotFreed", GodotObject.IsInstanceValid(window), true);
        CheckEqual("H.RegistryIntact", fixture.Manager.Registry.Count, 1);
        CheckEqual("H.ReopenFromMenu", fixture.Manager.ShowWindow("log"), true);
        CheckEqual("H.VisibleAgain", window.Visible, true);

        fixture.Dispose();
    }

    // [I] freed window entry는 lookup 시 제거되고 fail-closed된다.
    private void TestFreedWindowIsPruned()
    {
        GD.Print("[I] Freed window is pruned from the registry");
        var fixture = MakeFixture("world", "calendar");
        WorkspaceWindow calendar = fixture.Windows["calendar"];

        fixture.WindowRoot.RemoveChild(calendar);
        calendar.Free();
        fixture.Windows.Remove("calendar");

        ExpectDiagnosticLog("stale entry push_warning", () =>
        {
            CheckEqual("I.LookupFailsClosed", fixture.Manager.TryGetWindow("calendar", out _), false);
            CheckEqual("I.ShowFailsClosed", fixture.Manager.ShowWindow("calendar"), false);
        });
        CheckEqual("I.StaleEntryDropped", fixture.Manager.Registry.Count, 1);
        CheckEqual("I.SurvivorIntact", fixture.Manager.TryGetWindow("world", out _), true);

        // 같은 id를 새 인스턴스로 다시 등록할 수 있다.
        var recreated = new WorkspaceWindow { Name = "CalendarWindow2", WindowId = "calendar" };
        fixture.WindowRoot.AddChild(recreated);
        fixture.Windows["calendar"] = recreated;
        CheckEqual("I.ReregisterAfterPrune", fixture.Manager.RegisterWindow(recreated), true);

        fixture.Dispose();
    }

    // [J] 메인 창 최소화/복원 동기화. 숨겨둔 창은 복원에서 다시 뜨지 않는다.
    private void TestMinimizeRestoreKeepsHiddenWindowsHidden()
    {
        GD.Print("[J] Minimize/restore sync preserves hidden windows");
        var fixture = MakeFixture("world", "situation", "log");
        fixture.Manager.ShowWindow("world");
        fixture.Manager.ShowWindow("situation");
        // log는 사용자가 닫아둔 창이다.

        fixture.Manager.MinimizeManagedWindows();
        CheckEqual("J.WorldHiddenOnMinimize", fixture.Manager.IsWindowVisible("world"), false);
        CheckEqual("J.SituationHiddenOnMinimize", fixture.Manager.IsWindowVisible("situation"), false);
        CheckEqual("J.LogStillHidden", fixture.Manager.IsWindowVisible("log"), false);

        fixture.Manager.RestoreManagedWindows();
        CheckEqual("J.WorldRestored", fixture.Manager.IsWindowVisible("world"), true);
        CheckEqual("J.SituationRestored", fixture.Manager.IsWindowVisible("situation"), true);
        CheckEqual("J.LogStaysHidden", fixture.Manager.IsWindowVisible("log"), false);

        // 복원을 두 번 불러도 닫아둔 창이 되살아나지 않는다(재진입).
        fixture.Manager.RestoreManagedWindows();
        CheckEqual("J.LogStaysHiddenOnSecondRestore", fixture.Manager.IsWindowVisible("log"), false);

        fixture.Dispose();
    }

    // [K] 실제 셸 entry scene(workspace.tscn) 배선: 자동 등록 -> 메뉴 버튼 -> OS close -> 메뉴에서 재표시.
    private void TestWorkspaceSceneWiring()
    {
        GD.Print("[K] workspace.tscn wiring");
        var scene = GD.Load<PackedScene>("res://Assets/Scenes/workspace.tscn");
        Node workspace = scene.Instantiate();
        // 자동 배치 복원은 raw registry 관찰을 방해하므로 트리 진입 전에 끈다.
        workspace.Set("_restoreLayoutOnReady", false);
        AddChild(workspace);

        var manager = workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");
        var menu = workspace.GetNode<MenuButton>("CanvasLayer/TopMenuBar/HBox/WindowMenu");
        PopupMenu popup = menu.GetPopup();

        // WS-002 창 다이어트: floating managed 창은 World/TacticBoard/Report 3종. Log는 하단 도크,
        // Situation/WeeklyAction/Calendar는 World 내부 패널로 이관돼 registry에 없다.
        CheckEqual("K.ManagedWindowCount", manager.Registry.Count, 3);
        foreach (string gone in new[] { "log", "situation", "weekly_action", "calendar" })
            CheckEqual($"K.NotInRegistry_{gone}", manager.TryGetWindow(gone, out _), false);
        foreach (string id in new[] { "world", "tacticboard", "report" })
        {
            CheckEqual($"K.Registered_{id}", manager.TryGetWindow(id, out _), true);
            manager.TryGetWindow(id, out WorkspaceWindow window);
            CheckEqual($"K.NonExclusive_{id}", window.Exclusive, false);
            CheckEqual($"K.StartsHidden_{id}", manager.IsWindowVisible(id), false);
        }

        CheckEqual("K.MenuButtonCount", popup.GetItemCount(), 3);
        int worldItemId = 1;
        int worldIndex = popup.GetItemIndex(worldItemId);
        CheckEqual("K.WorldMenuItemExists", worldIndex >= 0, true);

        // 메뉴 버튼으로 연다.
        popup.EmitSignal(PopupMenu.SignalName.IdPressed, worldItemId);
        CheckEqual("K.WorldVisibleAfterMenuToggle", manager.IsWindowVisible("world"), true);

        // OS close = hide. 버튼도 함께 풀려야 한 번 눌러 다시 열 수 있다.
        manager.TryGetWindow("world", out WorkspaceWindow world);
        world.EmitSignal(Godot.Window.SignalName.CloseRequested);
        CheckEqual("K.WorldHiddenAfterClose", manager.IsWindowVisible("world"), false);
        CheckEqual("K.MenuUncheckedAfterClose", popup.IsItemChecked(worldIndex), false);
        CheckEqual("K.WindowNotFreed", GodotObject.IsInstanceValid(world), true);

        // 메뉴에서 다시 표시된다.
        popup.EmitSignal(PopupMenu.SignalName.IdPressed, worldItemId);
        CheckEqual("K.WorldVisibleAgain", manager.IsWindowVisible("world"), true);
        CheckEqual("K.RegistryStillThree", manager.Registry.Count, 3);

        RemoveChild(workspace);
        workspace.QueueFree();
    }

    private Fixture MakeFixture(params string[] windowIds)
    {
        var windowRoot = new Node { Name = "Windows" };
        var windows = new Dictionary<string, WorkspaceWindow>();
        foreach (string id in windowIds)
        {
            var window = new WorkspaceWindow { Name = $"{id}_window", WindowId = id, Visible = false };
            windowRoot.AddChild(window);
            windows[id] = window;
        }

        var manager = new WorkspaceWindowManager { Name = "WorkspaceWindowManager" };
        manager.Set("_windowRoot", windowRoot);

        var host = new Node { Name = "FixtureHost" };
        host.AddChild(windowRoot);
        host.AddChild(manager);
        AddChild(host);

        return new Fixture(host, windowRoot, manager, windows);
    }

    private sealed class Fixture
    {
        public Fixture(Node host, Node windowRoot, WorkspaceWindowManager manager,
            Dictionary<string, WorkspaceWindow> windows)
        {
            Host = host;
            WindowRoot = windowRoot;
            Manager = manager;
            Windows = windows;
        }

        public Node Host { get; }
        public Node WindowRoot { get; }
        public WorkspaceWindowManager Manager { get; }
        public Dictionary<string, WorkspaceWindow> Windows { get; }

        public void Dispose()
        {
            Host.GetParent()?.RemoveChild(Host);
            Host.QueueFree();
        }
    }
}
#endif






