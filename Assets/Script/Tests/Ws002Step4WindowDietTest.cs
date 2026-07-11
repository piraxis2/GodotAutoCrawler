#if TOOLS
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// WS-002 Step 4. 창 다이어트 + E-7 프리셋 v1을 headless로 검증한다.
///
/// - floating managed 창 = World/TacticBoard/Report 3종. Log=도크, Situation/WeeklyAction/Calendar=World 내부 패널.
/// - preset 국면별 visibility + content_mode(World hub/battle/replay, TacticBoard read/edit).
/// - E-7 정규화 비율이 content 기준 px로 resolve된다.
/// - World는 preset 전환 중 재생성되지 않는다.
/// - old(v1) workspace_layout.cfg는 UnsupportedVersion → 기본 preset fallback, 파일 파괴 없음.
/// </summary>
public partial class Ws002Step4WindowDietTest : Node
{
    private int _pass;
    private int _fail;

    public override void _Ready()
    {
        GD.Print("[WS-002 Step4] Running window diet + preset v1 tests...");

        TestRegistryIsThreeWindowDiet();
        TestWorldHasHubPanels();
        TestPresetVisibilityAndContentMode();
        TestWorldNotRecreatedAcrossPresets();
        TestPresetRatiosResolveToContent();
        TestHubPanelsGatedByContentMode();
        TestOldConfigVersionFallsBack();

        GD.Print($"[WS-002 Step4] {_pass} passed, {_fail} failed.");
        GetTree().Quit(_fail == 0 ? 0 : 1);
    }

    // floating managed 창은 world/tacticboard/report 3종. 제거된 창은 registry에 없다.
    private void TestRegistryIsThreeWindowDiet()
    {
        GD.Print("[A] Registry = World/TacticBoard/Report (diet)");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);

        CheckEqual("A.RegistryCount", manager.Registry.Count, 3);
        foreach (string id in new[] { "world", "tacticboard", "report" })
            CheckTrue($"A.Has_{id}", manager.TryGetWindow(id, out _));
        foreach (string gone in new[] { "log", "situation", "weekly_action", "calendar" })
            CheckFalse($"A.Gone_{gone}", manager.TryGetWindow(gone, out _));

        Free(workspace);
    }

    // Situation/WeeklyAction/Calendar 성격의 placeholder가 World 거점 모드 내부 패널로 이관됐다.
    private void TestWorldHasHubPanels()
    {
        GD.Print("[B] World hub-mode internal panels present");
        WorkspaceWindowManager _ = MakeManager(out Node workspace);

        CheckTrue("B.Panel_ContentMode",
            workspace.GetNodeOrNull("Windows/WorldWindow/Panels/ContentMode") != null);
        foreach (string panel in new[] { "CalendarStrip", "DangerGauge", "ActionCards" })
            CheckTrue($"B.HubPanel_{panel}",
                workspace.GetNodeOrNull($"Windows/WorldWindow/Panels/HubPanels/{panel}") != null);

        Free(workspace);
    }

    // 국면별 visibility + content_mode.
    private void TestPresetVisibilityAndContentMode()
    {
        GD.Print("[C] Preset visibility + content_mode per phase");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);
        manager.TryGetWindow("world", out WorkspaceWindow world);
        manager.TryGetWindow("tacticboard", out WorkspaceWindow board);

        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        CheckTrue("C.Outgame_world_visible", manager.IsWindowVisible("world"));
        CheckEqual("C.Outgame_world_hub", world.ContentMode, WorldContentMode.Hub);
        CheckFalse("C.Outgame_board_hidden", manager.IsWindowVisible("tacticboard"));
        CheckFalse("C.Outgame_report_hidden", manager.IsWindowVisible("report"));

        manager.ApplyPreset(WorkspaceLayoutPreset.Battle);
        CheckEqual("C.Battle_world_battle", world.ContentMode, WorldContentMode.Battle);
        CheckTrue("C.Battle_board_visible", manager.IsWindowVisible("tacticboard"));
        CheckEqual("C.Battle_board_read", board.ContentMode, TacticBoardContentMode.Read);
        CheckFalse("C.Battle_report_hidden", manager.IsWindowVisible("report"));

        manager.ApplyPreset(WorkspaceLayoutPreset.Analysis);
        CheckEqual("C.Analysis_world_replay", world.ContentMode, WorldContentMode.Replay);
        CheckEqual("C.Analysis_board_edit", board.ContentMode, TacticBoardContentMode.Edit);
        CheckTrue("C.Analysis_report_visible", manager.IsWindowVisible("report"));

        Free(workspace);
    }

    // World는 preset 전환 중 재생성되지 않는다(같은 instance id).
    private void TestWorldNotRecreatedAcrossPresets()
    {
        GD.Print("[D] World instance is stable across presets");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);

        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        manager.TryGetWindow("world", out WorkspaceWindow world);
        ulong id = world.GetInstanceId();

        manager.ApplyPreset(WorkspaceLayoutPreset.Battle);
        manager.TryGetWindow("world", out WorkspaceWindow world2);
        CheckEqual("D.SameAfterBattle", world2.GetInstanceId(), id);

        manager.ApplyPreset(WorkspaceLayoutPreset.Analysis);
        manager.TryGetWindow("world", out WorkspaceWindow world3);
        CheckEqual("D.SameAfterAnalysis", world3.GetInstanceId(), id);

        Free(workspace);
    }

    // E-7 정규화 비율이 content 기준 px로 resolve된다(battle: world 65% + board 34%).
    private void TestPresetRatiosResolveToContent()
    {
        GD.Print("[E] E-7 ratios resolve against content area");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);
        manager.ApplyPreset(WorkspaceLayoutPreset.Battle);

        Rect2I content = manager.GetContentRect();
        System.Collections.Generic.IReadOnlyDictionary<string, Rect2I> slots = manager.GetCurrentSlots();

        CheckEqual("E.WorldWidth", slots["world"].Size.X, Mathf.RoundToInt(content.Size.X * 0.64f));
        CheckEqual("E.BoardWidth", slots["tacticboard"].Size.X, Mathf.RoundToInt(content.Size.X * 0.33f));
        // 좌→우 배치: world가 board보다 왼쪽.
        CheckTrue("E.WorldLeftOfBoard", slots["world"].Position.X < slots["tacticboard"].Position.X);
        // 둘 다 content 안.
        CheckTrue("E.WorldInside", WorkspaceGeometry.IsInside(slots["world"], content));
        CheckTrue("E.BoardInside", WorkspaceGeometry.IsInside(slots["tacticboard"], content));

        Free(workspace);
    }

    // World 거점 패널(캘린더/위험/행동 카드)은 hub content mode에서만 보인다. battle/replay에서 숨겨진다.
    private void TestHubPanelsGatedByContentMode()
    {
        GD.Print("[G] World hub panels are gated by content mode");
        WorkspaceWindowManager manager = MakeManager(out Node workspace);
        var hubPanels = workspace.GetNode<Control>("Windows/WorldWindow/Panels/HubPanels");

        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame); // World=hub
        CheckTrue("G.HubVisibleInHub", hubPanels.Visible);

        manager.ApplyPreset(WorkspaceLayoutPreset.Battle); // World=battle
        CheckFalse("G.HubHiddenInBattle", hubPanels.Visible);

        manager.ApplyPreset(WorkspaceLayoutPreset.Analysis); // World=replay
        CheckFalse("G.HubHiddenInReplay", hubPanels.Visible);

        // 다시 거점으로 돌아오면 패널이 복원된다.
        manager.ApplyPreset(WorkspaceLayoutPreset.Outgame);
        CheckTrue("G.HubRestoredInHub", hubPanels.Visible);

        Free(workspace);
    }

    // old(v1) config는 UnsupportedVersion → 기본 preset fallback, 파일은 파괴하지 않는다.
    private void TestOldConfigVersionFallsBack()
    {
        GD.Print("[F] Old (v1) config falls back to default preset, file preserved");
        string path = "user://ws002_step4_oldconfig_test.cfg";
        if (Godot.FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(path);

        // WS-001 시절 v1 파일(구 창 세트 native 좌표)을 흉내낸다.
        var config = new ConfigFile();
        config.SetValue("meta", "version", 1);
        config.SetValue("window.situation", "visible", true);
        config.SetValue("window.situation", "position", new Vector2I(680, 20));
        config.SetValue("window.situation", "size", new Vector2I(360, 240));
        config.SetValue("window.situation", "content_mode", "");
        config.Save(path);

        // 순수 store: v1 → UnsupportedVersion.
        LayoutLoadResult result = new WorkspaceLayoutStore(path).Load();
        CheckEqual("F.UnsupportedVersion", result.Status, LayoutLoadStatus.UnsupportedVersion);
        CheckFalse("F.NoUsableSnapshots", result.HasUsableSnapshots);
        CheckTrue("F.FilePreserved", Godot.FileAccess.FileExists(path));

        // manager: v1 파일이면 기본 preset(outgame)으로 fallback → World visible.
        WorkspaceWindowManager manager = MakeManager(out Node workspace);
        manager.UseLayoutStorePath(path);
        LayoutLoadStatus status = manager.LoadLayout();
        CheckEqual("F.ManagerFallbackStatus", status, LayoutLoadStatus.UnsupportedVersion);
        CheckTrue("F.FallbackWorldVisible", manager.IsWindowVisible("world"));
        CheckFalse("F.SituationNotRegistered", manager.TryGetWindow("situation", out _));

        Free(workspace);
        DirAccess.RemoveAbsolute(path);
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
