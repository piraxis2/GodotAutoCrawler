#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.GameLog;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// GL-001 Step 2. LogWindowController(읽기 전용 GameLog 표시 UI)를 headless로 검증한다.
/// 탭 필터(전체=Event, 채널 탭=Event+Detail), Detail/importance 시각 구분, 대화 아카이브 접힘/펼침,
/// 200줄 trim의 UI 반영, append/clear 갱신, 샘플 seed를 다룬다.
/// </summary>
public partial class Gl001Step2LogWindowTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[GL-001 Step2] Running LogWindow UI tests...");
        try
        {
            TestControllerReflectsAppendedEntries();
            TestTabFilters();
            TestDetailAndImportanceVisualDistinction();
            TestDialogueArchiveCollapseStartsCollapsedAndExpands();
            TestTrimReflectedInUi();
            TestRefreshOnAppendAndClear();
            TestSampleSeedPopulates();
            TestEntriesForTabPureFunction();
            TestWorkspaceSceneWiresLogController();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[GL-001 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0)
        {
            GD.Print("[GL-001 Step2] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[GL-001 Step2] FAILED: {_failures} assertion(s)");
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

    private LogWindowController NewController()
    {
        var controller = new LogWindowController();
        // 격리된 로컬 모델을 주입해 autoload GameLogService 공유 모델과 분리한다(ADR-020 주입 경로).
        controller.UseModel(new GameLogModel());
        AddChild(controller); // 트리 진입 -> _Ready가 UI를 빌드한다.
        return controller;
    }

    private void Dispose(LogWindowController controller)
    {
        RemoveChild(controller);
        controller.QueueFree();
    }

    private static GameLogEntry Append(LogWindowController c, GameLogChannel channel,
        GameLogSeverity severity, GameLogImportance importance, string title,
        string body = null, bool collapsed = false)
    {
        var entry = new GameLogEntry(channel, severity, importance, title, body: body, isCollapsed: collapsed);
        c.Model.Append(entry);
        return entry; // Append가 Id를 부여한 뒤 돌려준다.
    }

    private static bool ContainsTitle(IReadOnlyList<GameLogEntry> entries, string title)
    {
        foreach (GameLogEntry e in entries)
            if (e.Title == title) return true;
        return false;
    }

    // [A] append한 entry가 UI 행에 반영된다(기본 전체 탭 = Event).
    private void TestControllerReflectsAppendedEntries()
    {
        GD.Print("[A] Controller reflects appended entries");
        LogWindowController c = NewController();
        Append(c, GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Normal, "first");
        Append(c, GameLogChannel.Story, GameLogSeverity.Event, GameLogImportance.Normal, "second");

        CheckEqual("A.CurrentCount", c.CurrentEntries().Count, 2);
        CheckEqual("A.RowCount", c.RowCount, 2);
        CheckEqual("A.Order0", c.CurrentEntries()[0].Title, "first");
        CheckEqual("A.Order1", c.CurrentEntries()[1].Title, "second");
        Dispose(c);
    }

    // [B] 탭 필터: 전체=Event만(모든 채널), 채널 탭=해당 채널 Event+Detail, 획득·시스템=Reward+System.
    private void TestTabFilters()
    {
        GD.Print("[B] Tab filters");
        LogWindowController c = NewController();
        Append(c, GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Normal, "c-ev");
        Append(c, GameLogChannel.Combat, GameLogSeverity.Detail, GameLogImportance.Normal, "c-de");
        Append(c, GameLogChannel.Story, GameLogSeverity.Event, GameLogImportance.Normal, "s-ev");
        Append(c, GameLogChannel.Reward, GameLogSeverity.Event, GameLogImportance.Normal, "r-ev");
        Append(c, GameLogChannel.System, GameLogSeverity.Event, GameLogImportance.Normal, "sys-ev");
        Append(c, GameLogChannel.Story, GameLogSeverity.Detail, GameLogImportance.Normal, "s-de");

        c.SelectTab(LogChannelTab.All);
        IReadOnlyList<GameLogEntry> all = c.CurrentEntries();
        CheckEqual("B.AllCount", all.Count, 4); // Event만: c-ev, s-ev, r-ev, sys-ev
        CheckFalse("B.AllNoDetail", ContainsTitle(all, "c-de") || ContainsTitle(all, "s-de"));

        c.SelectTab(LogChannelTab.Combat);
        IReadOnlyList<GameLogEntry> combat = c.CurrentEntries();
        CheckEqual("B.CombatCount", combat.Count, 2);
        CheckTrue("B.CombatHasEventAndDetail", ContainsTitle(combat, "c-ev") && ContainsTitle(combat, "c-de"));
        CheckFalse("B.CombatNoOther", ContainsTitle(combat, "s-ev"));

        c.SelectTab(LogChannelTab.Story);
        CheckEqual("B.StoryCount", c.CurrentEntries().Count, 2); // s-ev, s-de

        c.SelectTab(LogChannelTab.RewardSystem);
        IReadOnlyList<GameLogEntry> rs = c.CurrentEntries();
        CheckEqual("B.RewardSystemCount", rs.Count, 2);
        CheckTrue("B.RewardSystemMembers", ContainsTitle(rs, "r-ev") && ContainsTitle(rs, "sys-ev"));
        CheckFalse("B.RewardSystemNoCombat", ContainsTitle(rs, "c-ev"));
        // 합친 탭도 append 순서(Id)를 보존한다: r-ev(먼저) -> sys-ev.
        CheckEqual("B.RewardSystemOrder0", rs[0].Title, "r-ev");
        CheckEqual("B.RewardSystemOrder1", rs[1].Title, "sys-ev");

        CheckEqual("B.CurrentTab", c.CurrentTab, LogChannelTab.RewardSystem);
        Dispose(c);
    }

    // [C] Detail은 감광으로, importance는 색으로 시각 구분된다. 노출 판단엔 안 쓰인다([B]에서 확인).
    private void TestDetailAndImportanceVisualDistinction()
    {
        GD.Print("[C] Detail dimming + importance coloring");
        LogWindowController c = NewController();
        GameLogEntry ev = Append(c, GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Normal, "ev");
        GameLogEntry de = Append(c, GameLogChannel.Combat, GameLogSeverity.Detail, GameLogImportance.Normal, "de");
        GameLogEntry warn = Append(c, GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Warning, "warn");
        GameLogEntry crit = Append(c, GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Critical, "crit");

        c.SelectTab(LogChannelTab.Combat);
        Color evColor = c.GetRowTitleColor(ev.Id);
        Color deColor = c.GetRowTitleColor(de.Id);
        Color warnColor = c.GetRowTitleColor(warn.Id);
        Color critColor = c.GetRowTitleColor(crit.Id);

        // Detail은 같은 importance의 Event보다 어둡다.
        CheckTrue("C.DetailDimmerThanEvent", deColor.R < evColor.R - 0.05f);
        // importance 색이 Normal과 다르다.
        CheckTrue("C.WarningDiffersFromNormal", ColorDiffers(warnColor, evColor));
        CheckTrue("C.CriticalDiffersFromNormal", ColorDiffers(critColor, evColor));
        CheckTrue("C.WarningDiffersFromCritical", ColorDiffers(warnColor, critColor));
        Dispose(c);
    }

    // [D] 대화 아카이브는 접힘으로 시작하고 펼칠 수 있다.
    private void TestDialogueArchiveCollapseStartsCollapsedAndExpands()
    {
        GD.Print("[D] Dialogue archive starts collapsed and expands");
        LogWindowController c = NewController();
        GameLogEntry archive = Append(c, GameLogChannel.Story, GameLogSeverity.Event,
            GameLogImportance.Normal, "모닥불의 대화", body: "A: ...\nB: ...", collapsed: true);

        c.SelectTab(LogChannelTab.All); // archive는 Event라 전체 탭에 보인다.
        CheckFalse("D.StartsCollapsed", c.IsExpanded(archive.Id));
        CheckFalse("D.BodyHiddenInitially", c.IsBodyVisible(archive.Id));

        c.ToggleExpand(archive.Id);
        CheckTrue("D.ExpandedAfterToggle", c.IsExpanded(archive.Id));
        CheckTrue("D.BodyVisibleAfterToggle", c.IsBodyVisible(archive.Id));

        c.ToggleExpand(archive.Id);
        CheckFalse("D.CollapsedAgain", c.IsExpanded(archive.Id));
        CheckFalse("D.BodyHiddenAgain", c.IsBodyVisible(archive.Id));

        // 접힘 불가 entry나 없는 id는 no-op (P3: _expanded에 임의 id를 남기지 않는다).
        GameLogEntry plain = Append(c, GameLogChannel.Combat, GameLogSeverity.Event,
            GameLogImportance.Normal, "plain");
        c.ToggleExpand(plain.Id);
        CheckFalse("D.NonCollapsibleNoOp", c.IsExpanded(plain.Id));
        c.ToggleExpand(999999L);
        CheckFalse("D.UnknownIdNoOp", c.IsExpanded(999999L));
        Dispose(c);
    }

    // [E] 200줄 trim이 UI 행에도 반영된다.
    private void TestTrimReflectedInUi()
    {
        GD.Print("[E] 200-line trim reflected in UI rows");
        LogWindowController c = NewController();
        for (int i = 1; i <= 205; i++)
            Append(c, GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Normal, $"e{i}");

        c.SelectTab(LogChannelTab.All);
        CheckEqual("E.CurrentCapped", c.CurrentEntries().Count, GameLogModel.MaxEntries);
        CheckEqual("E.RowCountCapped", c.RowCount, GameLogModel.MaxEntries);
        CheckEqual("E.OldestSurvivor", c.CurrentEntries()[0].Title, "e6");
        Dispose(c);
    }

    // [F] append/clear에서 UI가 자동 갱신된다.
    private void TestRefreshOnAppendAndClear()
    {
        GD.Print("[F] Refresh on append and clear");
        LogWindowController c = NewController();
        CheckEqual("F.EmptyStart", c.RowCount, 0);

        Append(c, GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Normal, "a");
        CheckEqual("F.OneRow", c.RowCount, 1);
        Append(c, GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Normal, "b");
        Append(c, GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Normal, "c");
        CheckEqual("F.ThreeRows", c.RowCount, 3);

        c.Model.Clear();
        CheckEqual("F.ClearedRows", c.RowCount, 0);
        CheckEqual("F.ClearedEntries", c.CurrentEntries().Count, 0);
        Dispose(c);
    }

    // [G] 샘플 seed가 다양한 채널/등급/접힘 아카이브를 채운다.
    private void TestSampleSeedPopulates()
    {
        GD.Print("[G] Sample seed populates");
        LogWindowController c = NewController();
        c.SeedSampleLog();

        CheckEqual("G.ModelCount", c.Model.Count, 8);
        c.SelectTab(LogChannelTab.All);
        CheckEqual("G.AllEventCount", c.CurrentEntries().Count, 7); // 8개 중 Detail 1개 제외
        c.SelectTab(LogChannelTab.Combat);
        CheckEqual("G.CombatCount", c.CurrentEntries().Count, 4);

        // 접힘 아카이브 entry가 있고 접힘으로 시작한다.
        c.SelectTab(LogChannelTab.Story);
        GameLogEntry archive = null;
        foreach (GameLogEntry e in c.CurrentEntries())
            if (e.IsCollapsed && !string.IsNullOrEmpty(e.Body)) { archive = e; break; }
        CheckTrue("G.ArchiveExists", archive != null);
        if (archive != null)
        {
            CheckFalse("G.ArchiveStartsCollapsed", c.IsExpanded(archive.Id));
            CheckFalse("G.ArchiveBodyHidden", c.IsBodyVisible(archive.Id));
        }
        Dispose(c);
    }

    // [H] EntriesForTab 순수 함수: severity 정책은 모델, 채널 집합은 함수가 좁힌다.
    private void TestEntriesForTabPureFunction()
    {
        GD.Print("[H] EntriesForTab pure function");
        var model = new GameLogModel();
        model.Append(new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Event, GameLogImportance.Normal, "c-ev"));
        model.Append(new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Detail, GameLogImportance.Normal, "c-de"));
        model.Append(new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Trace, GameLogImportance.Normal, "c-tr"));
        model.Append(new GameLogEntry(GameLogChannel.Reward, GameLogSeverity.Event, GameLogImportance.Normal, "r-ev"));
        model.Append(new GameLogEntry(GameLogChannel.System, GameLogSeverity.Detail, GameLogImportance.Normal, "sys-de"));

        // 전체 탭 = Event만, Trace 제외.
        IReadOnlyList<GameLogEntry> all = LogWindowController.EntriesForTab(model, LogChannelTab.All, false);
        CheckEqual("H.AllCount", all.Count, 2); // c-ev, r-ev
        CheckFalse("H.AllNoTrace", ContainsTitle(all, "c-tr"));

        // 채널 탭 = Event+Detail, Trace는 includeTrace=false라 제외.
        IReadOnlyList<GameLogEntry> combat = LogWindowController.EntriesForTab(model, LogChannelTab.Combat, false);
        CheckEqual("H.CombatCount", combat.Count, 2); // c-ev, c-de (c-tr 제외)
        CheckFalse("H.CombatNoTrace", ContainsTitle(combat, "c-tr"));

        // 획득·시스템 = Reward+System, Event+Detail.
        IReadOnlyList<GameLogEntry> rs = LogWindowController.EntriesForTab(model, LogChannelTab.RewardSystem, false);
        CheckEqual("H.RewardSystemCount", rs.Count, 2); // r-ev, sys-de
        CheckTrue("H.RewardSystemMembers", ContainsTitle(rs, "r-ev") && ContainsTitle(rs, "sys-de"));

        // includeTrace=true면 채널 탭에서 Trace도 보인다(debug 경로 확인).
        IReadOnlyList<GameLogEntry> combatDebug = LogWindowController.EntriesForTab(model, LogChannelTab.Combat, true);
        CheckEqual("H.CombatDebugCount", combatDebug.Count, 3);
    }

    // [I] workspace.tscn의 Log 창이 placeholder Label이 아니라 실제 LogWindowController로 배선돼 있다.
    private void TestWorkspaceSceneWiresLogController()
    {
        GD.Print("[I] workspace.tscn wires the LogWindowController");
        var scene = GD.Load<PackedScene>("res://Assets/Scenes/workspace.tscn");
        Node workspace = scene.Instantiate();
        workspace.Set("_restoreLayoutOnReady", false); // 배치 저장/복원 side effect 차단(WS 테스트와 동일).
        AddChild(workspace);

        var logView = workspace.GetNodeOrNull<LogWindowController>("Windows/LogWindow/LogView");
        CheckTrue("I.LogViewIsController", logView != null);
        if (logView != null)
        {
            CheckEqual("I.StartsEmpty", logView.RowCount, 0);
            logView.SeedSampleLog();
            logView.SelectTab(LogChannelTab.All);
            CheckTrue("I.RowsAfterSeed", logView.RowCount > 0);
            CheckEqual("I.AllEventCount", logView.CurrentEntries().Count, 7);
        }

        RemoveChild(workspace);
        workspace.QueueFree();
    }

    private static bool ColorDiffers(Color a, Color b)
    {
        return Mathf.Abs(a.R - b.R) > 0.02f || Mathf.Abs(a.G - b.G) > 0.02f || Mathf.Abs(a.B - b.B) > 0.02f;
    }
}
#endif
