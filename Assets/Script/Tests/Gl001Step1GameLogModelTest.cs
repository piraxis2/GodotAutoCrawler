#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.GameLog;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// GL-001 Step 1. GameLog core model의 데이터/필터 정책을 headless로 검증한다.
/// append 순서 보존, 200줄 trim, severity/channel 필터, importance 독립, link 보존, clear를 다룬다.
/// </summary>
public partial class Gl001Step1GameLogModelTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[GL-001 Step1] Running GameLog model tests...");
        try
        {
            TestAppendPreservesOrderAndAssignsIds();
            TestTrimKeepsNewest200OldestFirst();
            TestDefaultViewReturnsEventsOnly();
            TestChannelTabReturnsEventAndDetail();
            TestTraceOnlyWithDebugFilter();
            TestImportanceDoesNotAffectVisibility();
            TestChannelFilterIsolatesChannels();
            TestBoundaryValues();
            TestClearEmptiesAndKeepsIdMonotonic();
            TestEntryAddedEventFires();
            TestEntriesDoNotInvokeInteractionHandler();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[GL-001 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0)
        {
            GD.Print("[GL-001 Step1] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[GL-001 Step1] FAILED: {_failures} assertion(s)");
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

    private static GameLogEntry Event(GameLogChannel channel, string title,
        GameLogImportance importance = GameLogImportance.Normal)
        => new(channel, GameLogSeverity.Event, importance, title);

    private static GameLogEntry Detail(GameLogChannel channel, string title)
        => new(channel, GameLogSeverity.Detail, GameLogImportance.Normal, title);

    private static GameLogEntry Trace(GameLogChannel channel, string title)
        => new(channel, GameLogSeverity.Trace, GameLogImportance.Normal, title);

    // [A] append 순서가 보존되고 Id가 1부터 증가한다.
    private void TestAppendPreservesOrderAndAssignsIds()
    {
        GD.Print("[A] Append preserves order and assigns incrementing ids");
        var model = new GameLogModel();
        model.Append(Event(GameLogChannel.Combat, "first"));
        model.Append(Event(GameLogChannel.Story, "second"));
        model.Append(Event(GameLogChannel.System, "third"));

        IReadOnlyList<GameLogEntry> all = model.GetEntries(null, true, true);
        CheckEqual("A.Count", all.Count, 3);
        CheckEqual("A.Order0", all[0].Title, "first");
        CheckEqual("A.Order1", all[1].Title, "second");
        CheckEqual("A.Order2", all[2].Title, "third");
        CheckEqual("A.Id0", all[0].Id, 1L);
        CheckEqual("A.Id1", all[1].Id, 2L);
        CheckEqual("A.Id2", all[2].Id, 3L);
    }

    // [B] 200줄 초과 시 오래된 entry부터 제거되고 최신은 남는다.
    private void TestTrimKeepsNewest200OldestFirst()
    {
        GD.Print("[B] Trim keeps the newest 200, drops oldest first");
        var model = new GameLogModel();
        for (int i = 1; i <= 205; i++)
        {
            model.Append(Event(GameLogChannel.Combat, $"e{i}"));
        }

        IReadOnlyList<GameLogEntry> all = model.GetEntries(null, true, true);
        CheckEqual("B.CountCappedAt200", all.Count, GameLogModel.MaxEntries);
        CheckEqual("B.CountProperty", model.Count, GameLogModel.MaxEntries);
        // Id 1~5는 폐기, 6~205 생존. 순서 보존.
        CheckEqual("B.OldestSurvivorTitle", all[0].Title, "e6");
        CheckEqual("B.OldestSurvivorId", all[0].Id, 6L);
        CheckEqual("B.NewestTitle", all[all.Count - 1].Title, "e205");
        CheckEqual("B.NewestId", all[all.Count - 1].Id, 205L);
    }

    // [C] 기본 뷰(전체 + Event)는 사건만 반환하고 Detail/Trace를 제외한다.
    private void TestDefaultViewReturnsEventsOnly()
    {
        GD.Print("[C] Default view returns Event only, excludes Detail/Trace");
        var model = new GameLogModel();
        model.Append(Event(GameLogChannel.Combat, "ev"));
        model.Append(Detail(GameLogChannel.Combat, "de"));
        model.Append(Trace(GameLogChannel.Combat, "tr"));

        IReadOnlyList<GameLogEntry> view = model.GetEntries(null, false, false);
        CheckEqual("C.OnlyEvent", view.Count, 1);
        CheckEqual("C.EventKept", view[0].Title, "ev");
    }

    // [D] 채널 탭(channel + Event/Detail)은 그 채널의 Event/Detail을 반환하고 Trace/타 채널을 제외한다.
    private void TestChannelTabReturnsEventAndDetail()
    {
        GD.Print("[D] Channel tab returns Event+Detail of that channel, excludes Trace and other channels");
        var model = new GameLogModel();
        model.Append(Event(GameLogChannel.Combat, "c-ev"));
        model.Append(Detail(GameLogChannel.Combat, "c-de"));
        model.Append(Trace(GameLogChannel.Combat, "c-tr"));
        model.Append(Event(GameLogChannel.Story, "s-ev"));
        model.Append(Detail(GameLogChannel.Story, "s-de"));

        IReadOnlyList<GameLogEntry> combat = model.GetEntries(GameLogChannel.Combat, true, false);
        CheckEqual("D.CombatCount", combat.Count, 2);
        CheckEqual("D.CombatEvent", combat[0].Title, "c-ev");
        CheckEqual("D.CombatDetail", combat[1].Title, "c-de");
        CheckFalse("D.NoTrace", ContainsTitle(combat, "c-tr"));
        CheckFalse("D.NoOtherChannel", ContainsTitle(combat, "s-ev"));
    }

    // [E] Trace는 debug filter(includeTrace)에서만 반환된다.
    private void TestTraceOnlyWithDebugFilter()
    {
        GD.Print("[E] Trace surfaces only under debug filter");
        var model = new GameLogModel();
        model.Append(Event(GameLogChannel.Combat, "ev"));
        model.Append(Detail(GameLogChannel.Combat, "de"));
        model.Append(Trace(GameLogChannel.Combat, "tr"));

        CheckFalse("E.HiddenInChannelTab",
            ContainsTitle(model.GetEntries(GameLogChannel.Combat, true, false), "tr"));
        IReadOnlyList<GameLogEntry> debug = model.GetEntries(GameLogChannel.Combat, true, true);
        CheckEqual("E.DebugCount", debug.Count, 3);
        CheckTrue("E.TraceShown", ContainsTitle(debug, "tr"));
        // 전체 debug(모든 채널 + trace)도 확인.
        CheckTrue("E.TraceShownAllChannels", ContainsTitle(model.GetEntries(null, false, true), "tr"));
    }

    // [F] Importance는 노출 범위 판단에 쓰이지 않는다. Critical Detail도 기본 뷰에서 숨는다.
    private void TestImportanceDoesNotAffectVisibility()
    {
        GD.Print("[F] Importance does not affect visibility");
        var model = new GameLogModel();
        model.Append(new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Detail,
            GameLogImportance.Critical, "critical-detail"));
        model.Append(Event(GameLogChannel.Combat, "warn-event", GameLogImportance.Warning));

        IReadOnlyList<GameLogEntry> view = model.GetEntries(null, false, false);
        // Critical이어도 Detail이면 기본 뷰에서 숨는다.
        CheckFalse("F.CriticalDetailHidden", ContainsTitle(view, "critical-detail"));
        CheckTrue("F.WarningEventShown", ContainsTitle(view, "warn-event"));
        // importance 값은 보존된다.
        CheckEqual("F.ImportancePreserved", view[0].Importance, GameLogImportance.Warning);
    }

    // [G] 채널 필터가 4채널을 각각 격리하고, null은 전부 반환한다.
    private void TestChannelFilterIsolatesChannels()
    {
        GD.Print("[G] Channel filter isolates each channel; null returns all");
        var model = new GameLogModel();
        model.Append(Event(GameLogChannel.Combat, "combat"));
        model.Append(Event(GameLogChannel.Story, "story"));
        model.Append(Event(GameLogChannel.Reward, "reward"));
        model.Append(Event(GameLogChannel.System, "system"));

        CheckEqual("G.Combat", model.GetEntries(GameLogChannel.Combat, true, true).Count, 1);
        CheckEqual("G.Story", model.GetEntries(GameLogChannel.Story, true, true).Count, 1);
        CheckEqual("G.Reward", model.GetEntries(GameLogChannel.Reward, true, true).Count, 1);
        CheckEqual("G.System", model.GetEntries(GameLogChannel.System, true, true).Count, 1);
        CheckEqual("G.AllChannels", model.GetEntries(null, true, true).Count, 4);
    }

    // [H] 경계값: 빈/null title 정규화, null body/link 허용, null entry는 예외.
    private void TestBoundaryValues()
    {
        GD.Print("[H] Boundary values (empty title, null body/link, null entry)");
        var model = new GameLogModel();
        model.Append(new GameLogEntry(GameLogChannel.System, GameLogSeverity.Event,
            GameLogImportance.Normal, null, body: null, occurredAt: null, link: null));

        IReadOnlyList<GameLogEntry> all = model.GetEntries(null, true, true);
        CheckEqual("H.NullTitleNormalized", all[0].Title, string.Empty);
        CheckEqual("H.NullBodyKept", all[0].Body, (string)null);
        CheckEqual("H.NullLinkKept", all[0].Link, (GameLogLink)null);

        bool threw = false;
        try { model.Append(null); }
        catch (ArgumentNullException) { threw = true; }
        CheckTrue("H.NullEntryThrows", threw);
        // 예외 후에도 모델 상태는 온전하다.
        CheckEqual("H.CountUnchanged", model.Count, 1);
    }

    // [I] Clear는 비우고 Cleared를 발행하며, Id 카운터는 리셋하지 않는다.
    private void TestClearEmptiesAndKeepsIdMonotonic()
    {
        GD.Print("[I] Clear empties, fires Cleared, keeps id monotonic");
        var model = new GameLogModel();
        model.Append(Event(GameLogChannel.Combat, "a")); // Id 1
        model.Append(Event(GameLogChannel.Combat, "b")); // Id 2

        int clearedFired = 0;
        model.Cleared += () => clearedFired++;
        model.Clear();
        CheckEqual("I.Emptied", model.Count, 0);
        CheckEqual("I.ClearedFired", clearedFired, 1);

        model.Append(Event(GameLogChannel.Combat, "c")); // Id는 3으로 이어져야 한다.
        CheckEqual("I.IdContinuesAfterClear", model.GetEntries(null, true, true)[0].Id, 3L);

        // 빈 모델 Clear는 이벤트를 다시 쏘지 않는다.
        model.Clear();
        clearedFired = 0;
        model.Clear();
        CheckEqual("I.EmptyClearNoEvent", clearedFired, 0);
    }

    // [J] EntryAdded는 추가된 entry와 함께 발행된다.
    private void TestEntryAddedEventFires()
    {
        GD.Print("[J] EntryAdded fires with the appended entry");
        var model = new GameLogModel();
        GameLogEntry received = null;
        int fired = 0;
        model.EntryAdded += e => { received = e; fired++; };

        var entry = Event(GameLogChannel.Reward, "loot");
        model.Append(entry);
        CheckEqual("J.Fired", fired, 1);
        CheckTrue("J.SameEntry", ReferenceEquals(received, entry));
        CheckEqual("J.IdAssignedBeforeEvent", received.Id, 1L);
    }

    // [K] entry는 interaction handler를 직접 호출하지 않는다. link 데이터는 보존된다.
    private void TestEntriesDoNotInvokeInteractionHandler()
    {
        GD.Print("[K] Entries preserve link data and never invoke an interaction handler");
        var handler = new SpyHandler();
        var link = new GameLogLink(GameLogLinkType.UnitDetail, "unit-42");
        var entry = new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Event,
            GameLogImportance.Critical, "ally down", link: link);

        var model = new GameLogModel();
        model.EntryAdded += _ => { /* UI 갱신 hook 자리. handler를 부르지 않는다. */ };
        model.Append(entry);

        // append/filter/clear 어떤 경로도 handler를 건드리지 않는다.
        model.GetEntries(null, false, false);
        model.GetEntries(GameLogChannel.Combat, true, true);
        model.Clear();

        CheckEqual("K.HandlerNeverCanHandle", handler.CanHandleCalls, 0);
        CheckEqual("K.HandlerNeverHandle", handler.HandleCalls, 0);
        // link 데이터 보존.
        CheckEqual("K.LinkType", entry.Link.Type, GameLogLinkType.UnitDetail);
        CheckEqual("K.LinkTargetId", entry.Link.TargetId, "unit-42");
    }

    private static bool ContainsTitle(IReadOnlyList<GameLogEntry> entries, string title)
    {
        foreach (GameLogEntry e in entries)
            if (e.Title == title) return true;
        return false;
    }

    // v0 handler가 외부 계약임을 보이기 위한 테스트 스파이. 모델/entry는 이걸 절대 부르지 않는다.
    private sealed class SpyHandler : IGameLogInteractionHandler
    {
        public int CanHandleCalls;
        public int HandleCalls;

        public bool CanHandle(GameLogLink link)
        {
            CanHandleCalls++;
            return link != null && link.Type != GameLogLinkType.None;
        }

        public void Handle(GameLogLink link) => HandleCalls++;
    }
}
#endif
