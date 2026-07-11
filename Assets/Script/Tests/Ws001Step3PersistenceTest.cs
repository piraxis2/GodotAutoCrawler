#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// WS-001 Step 3. 배치 저장/복원 round-trip과 fallback을 검증한다.
/// 실제 파일을 오염시키지 않도록 각 테스트는 격리된 user:// 경로를 쓰고 끝나면 지운다.
/// </summary>
public partial class Ws001Step3PersistenceTest : Node
{
    private int _failures;
    private int _tmpCounter;

    public override void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[WS-001 Step3] Running layout persistence tests...");
        try
        {
            TestStoreRoundTrip();
            TestFirstRunReturnsFirstRunStatus();
            TestCorruptConfigFallsBack();
            TestUnknownVersionFallsBackWithoutDestroyingFile();
            TestMalformedWindowEntryIsSkipped();
            TestManagerSaveLoadRestoresPositions();
            TestManagerFirstRunAppliesDefaultPreset();
            TestStaleCoordinatesAreGatheredOnLoad();
            TestNegativeSizeRejected();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[WS-001 Step3] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0)
        {
            GD.Print("[WS-001 Step3] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[WS-001 Step3] FAILED: {_failures} assertion(s)");
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

    private string NextTmpPath() => $"user://ws001_test_layout_{_tmpCounter++}.cfg";

    private static void DeleteIfExists(string path)
    {
        if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(path);
    }

    // [A] store save -> load 왕복에서 값이 보존된다.
    private void TestStoreRoundTrip()
    {
        GD.Print("[A] Store round-trip preserves values");
        string path = NextTmpPath();
        DeleteIfExists(path);
        var store = new WorkspaceLayoutStore(path);

        var snapshots = new Dictionary<string, WindowSnapshot>
        {
            ["world"] = new(true, new Vector2I(100, 200), new Vector2I(640, 480), WorldContentMode.Battle),
            ["log"] = new(false, new Vector2I(1000, 50), new Vector2I(360, 560), "")
        };
        CheckEqual("A.SaveOk", store.Save(snapshots), Error.Ok);

        LayoutLoadResult result = store.Load();
        CheckEqual("A.Status", result.Status, LayoutLoadStatus.Loaded);
        CheckTrue("A.Usable", result.HasUsableSnapshots);
        CheckEqual("A.Count", result.Snapshots.Count, 2);
        CheckEqual("A.WorldVisible", result.Snapshots["world"].Visible, true);
        CheckEqual("A.WorldPosition", result.Snapshots["world"].Position, new Vector2I(100, 200));
        CheckEqual("A.WorldContentMode", result.Snapshots["world"].ContentMode, WorldContentMode.Battle);
        CheckEqual("A.LogVisible", result.Snapshots["log"].Visible, false);
        CheckEqual("A.LogSize", result.Snapshots["log"].Size, new Vector2I(360, 560));

        DeleteIfExists(path);
    }

    // [B] 파일이 없으면 FirstRun이다.
    private void TestFirstRunReturnsFirstRunStatus()
    {
        GD.Print("[B] Missing file -> FirstRun");
        string path = NextTmpPath();
        DeleteIfExists(path);
        var store = new WorkspaceLayoutStore(path);

        LayoutLoadResult result = store.Load();
        CheckEqual("B.Status", result.Status, LayoutLoadStatus.FirstRun);
        CheckFalse("B.NotUsable", result.HasUsableSnapshots);
    }

    // [C] 파싱 불가 내용은 안전 fallback한다(크래시 없음, 사용 가능한 스냅샷 없음).
    // 주: C# ConfigFile.Load는 GDScript와 달리 잘못된 토큰에도 Error.Ok를 반환한다(더 관대함).
    // 그래서 이런 파일은 version 누락 경로로 흘러 UnsupportedVersion이 된다. Corrupt enum은
    // Load가 실제 Error를 낼 때(예: 읽기 불가)를 위한 방어 경로로 코드에 남는다. 완료 조건이 요구하는 것은
    // "잘못된 값이 있어도 실행이 깨지지 않고 기본 배치로 복구"이므로 여기서는 그것을 단언한다.
    private void TestCorruptConfigFallsBack()
    {
        GD.Print("[C] Unparseable config falls back safely");
        string path = NextTmpPath();
        using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            file.StoreString("[meta]\nversion = @@@\n[window.world]\nposition = Vector2i(10, 10\n");
        }

        var store = new WorkspaceLayoutStore(path);
        LayoutLoadResult result = default;
        ExpectDiagnosticLog("unparseable config push_warning", () => result = store.Load());
        CheckFalse("C.NotUsable", result.HasUsableSnapshots);
        CheckTrue("C.SafeFallbackStatus",
            result.Status == LayoutLoadStatus.Corrupt || result.Status == LayoutLoadStatus.UnsupportedVersion);
        CheckTrue("C.FileNotDestroyed", FileAccess.FileExists(path));

        DeleteIfExists(path);
    }

    // [D] unknown/미지 version은 fallback하고 파일을 파괴하지 않는다.
    private void TestUnknownVersionFallsBackWithoutDestroyingFile()
    {
        GD.Print("[D] Unknown version -> fallback, file preserved");
        string path = NextTmpPath();
        var config = new ConfigFile();
        config.SetValue("meta", "version", 999);
        config.SetValue("window.world", "visible", true);
        config.SetValue("window.world", "position", new Vector2I(10, 10));
        config.SetValue("window.world", "size", new Vector2I(640, 480));
        config.SetValue("window.world", "content_mode", "hub");
        config.Save(path);

        var store = new WorkspaceLayoutStore(path);
        LayoutLoadResult result = default;
        ExpectDiagnosticLog("unknown version push_warning", () => result = store.Load());
        CheckEqual("D.Status", result.Status, LayoutLoadStatus.UnsupportedVersion);
        CheckFalse("D.NotUsable", result.HasUsableSnapshots);
        CheckTrue("D.FileNotDestroyed", FileAccess.FileExists(path));

        // version 누락도 UnsupportedVersion이다.
        var noVersion = new ConfigFile();
        noVersion.SetValue("window.world", "visible", true);
        noVersion.SetValue("window.world", "position", new Vector2I(10, 10));
        noVersion.SetValue("window.world", "size", new Vector2I(640, 480));
        noVersion.SetValue("window.world", "content_mode", "hub");
        string path2 = NextTmpPath();
        noVersion.Save(path2);
        LayoutLoadResult result2 = default;
        ExpectDiagnosticLog("missing version push_warning", () => result2 = new WorkspaceLayoutStore(path2).Load());
        CheckEqual("D.MissingVersionStatus", result2.Status, LayoutLoadStatus.UnsupportedVersion);

        DeleteIfExists(path);
        DeleteIfExists(path2);
    }

    // [E] 타입이 깨진 개별 창은 건너뛰고 나머지는 살린다.
    private void TestMalformedWindowEntryIsSkipped()
    {
        GD.Print("[E] Malformed window entry is skipped, others survive");
        string path = NextTmpPath();
        var config = new ConfigFile();
        config.SetValue("meta", "version", WorkspaceLayoutStore.CurrentVersion);
        // 정상 창.
        config.SetValue("window.world", "visible", true);
        config.SetValue("window.world", "position", new Vector2I(10, 10));
        config.SetValue("window.world", "size", new Vector2I(640, 480));
        config.SetValue("window.world", "content_mode", "hub");
        // position이 문자열(타입 오류).
        config.SetValue("window.log", "visible", true);
        config.SetValue("window.log", "position", "not a vector");
        config.SetValue("window.log", "size", new Vector2I(360, 560));
        config.SetValue("window.log", "content_mode", "");
        // size 키 누락.
        config.SetValue("window.calendar", "visible", true);
        config.SetValue("window.calendar", "position", new Vector2I(0, 0));
        config.SetValue("window.calendar", "content_mode", "");
        config.Save(path);

        var store = new WorkspaceLayoutStore(path);
        LayoutLoadResult result = default;
        ExpectDiagnosticLog("malformed entries push_warning", () => result = store.Load());
        CheckEqual("E.Status", result.Status, LayoutLoadStatus.Loaded);
        CheckEqual("E.OnlyValidWindowKept", result.Snapshots.Count, 1);
        CheckTrue("E.WorldKept", result.Snapshots.ContainsKey("world"));
        CheckFalse("E.LogSkipped", result.Snapshots.ContainsKey("log"));
        CheckFalse("E.CalendarSkipped", result.Snapshots.ContainsKey("calendar"));

        DeleteIfExists(path);
    }

    // [F] manager save -> 새 manager load에서 position/size/visible/content_mode가 복원된다(재시작 시뮬레이션).
    private void TestManagerSaveLoadRestoresPositions()
    {
        GD.Print("[F] Manager save -> fresh manager restores layout");
        string path = NextTmpPath();
        DeleteIfExists(path);

        Fixture first = MakeFixture(path);
        first.Manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        first.Manager.TryGetWindow("world", out WorkspaceWindow world);
        world.Position = new Vector2I(222, 333);
        world.Size = new Vector2I(500, 400);
        Vector2I savedWorldPos = world.Position;
        Vector2I savedWorldSize = world.Size;
        CheckEqual("F.SaveOk", first.Manager.SaveLayout(), Error.Ok);
        first.Dispose();

        // 새 인스턴스 = 재시작.
        Fixture second = MakeFixture(path);
        LayoutLoadStatus status = second.Manager.LoadLayout();
        CheckEqual("F.LoadedStatus", status, LayoutLoadStatus.Loaded);
        second.Manager.TryGetWindow("world", out WorkspaceWindow restoredWorld);
        CheckEqual("F.WorldPositionRestored", restoredWorld.Position, savedWorldPos);
        CheckEqual("F.WorldSizeRestored", restoredWorld.Size, savedWorldSize);
        CheckFalse("F.LogNotInRegistry", second.Manager.TryGetWindow("log", out _));
        CheckTrue("F.WorldVisible", second.Manager.IsWindowVisible("world"));
        second.Dispose();

        DeleteIfExists(path);
    }

    // [G] 첫 실행은 기본 preset으로 시작한다.
    private void TestManagerFirstRunAppliesDefaultPreset()
    {
        GD.Print("[G] First run applies the default preset");
        string path = NextTmpPath();
        DeleteIfExists(path);

        Fixture fixture = MakeFixture(path);
        LayoutLoadStatus status = fixture.Manager.LoadLayout();
        CheckEqual("G.FirstRunStatus", status, LayoutLoadStatus.FirstRun);
        // 기본 preset(outgame)은 World(hub)만 보인다. TacticBoard/Report 숨김, Log는 상시 도크.
        CheckTrue("G.world_visible", fixture.Manager.IsWindowVisible("world"));
        CheckFalse("G.tacticboard_hidden", fixture.Manager.IsWindowVisible("tacticboard"));
        CheckFalse("G.report_hidden", fixture.Manager.IsWindowVisible("report"));

        fixture.Dispose();
        DeleteIfExists(path);
    }

    // [H] WS-002: 저장된 좌표가 현재 마스터 content 밖이면 load 후 content gather로 회수된다.
    private void TestStaleCoordinatesAreGatheredOnLoad()
    {
        GD.Print("[H] Stale out-of-content coordinates are gathered on load");
        string path = NextTmpPath();
        var config = new ConfigFile();
        config.SetValue("meta", "version", WorkspaceLayoutStore.CurrentVersion);
        // 예전 모니터에서 저장한 듯한 화면 밖 좌표.
        config.SetValue("window.world", "visible", true);
        config.SetValue("window.world", "position", new Vector2I(-9000, -9000));
        config.SetValue("window.world", "size", new Vector2I(640, 480));
        config.SetValue("window.world", "content_mode", "hub");
        config.Save(path);

        Fixture fixture = MakeFixture(path);
        LayoutLoadStatus status = fixture.Manager.LoadLayout();
        CheckEqual("H.LoadedStatus", status, LayoutLoadStatus.Loaded);

        fixture.Manager.TryGetWindow("world", out WorkspaceWindow world);
        Rect2I content = fixture.Manager.GetContentRect();
        Rect2I decoRect = WorkspaceWindowManager.GetDecorationRect(world);
        CheckTrue("H.InsideContentAfterLoad", WorkspaceGeometry.IsInside(decoRect, content));
        CheckTrue("H.PulledBackFromNegative", world.Position.X > -9000);

        fixture.Dispose();
        DeleteIfExists(path);
    }

    // [I] size <= 0은 손상으로 간주해 건너뛴다.
    private void TestNegativeSizeRejected()
    {
        GD.Print("[I] Non-positive size is treated as malformed");
        string path = NextTmpPath();
        var config = new ConfigFile();
        config.SetValue("meta", "version", WorkspaceLayoutStore.CurrentVersion);
        config.SetValue("window.world", "visible", true);
        config.SetValue("window.world", "position", new Vector2I(10, 10));
        config.SetValue("window.world", "size", new Vector2I(0, 480));
        config.SetValue("window.world", "content_mode", "hub");
        config.Save(path);

        LayoutLoadResult result = default;
        ExpectDiagnosticLog("non-positive size push_warning", () => result = new WorkspaceLayoutStore(path).Load());
        CheckEqual("I.Status", result.Status, LayoutLoadStatus.Loaded);
        CheckEqual("I.ZeroSizeSkipped", result.Snapshots.Count, 0);

        DeleteIfExists(path);
    }

    private Fixture MakeFixture(string storePath)
    {
        var scene = GD.Load<PackedScene>("res://Assets/Scenes/workspace.tscn");
        Node workspace = scene.Instantiate();
        workspace.Set("_restoreLayoutOnReady", false);
        AddChild(workspace);
        var manager = workspace.GetNode<WorkspaceWindowManager>("WorkspaceWindowManager");
        manager.UseLayoutStorePath(storePath);
        return new Fixture(this, workspace, manager);
    }

    private sealed class Fixture
    {
        private readonly Node _parent;
        private readonly Node _workspace;

        public Fixture(Node parent, Node workspace, WorkspaceWindowManager manager)
        {
            _parent = parent;
            _workspace = workspace;
            Manager = manager;
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
