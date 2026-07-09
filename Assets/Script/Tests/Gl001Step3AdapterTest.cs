#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.GameLog;
using AutoCrawler.Assets.Script.UI.Window;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

/// <summary>
/// GL-001 Step 3. SkillReport -> GameLogEntry adapter, 진영 resolver, GameLogService autoload,
/// LogWindowController service 구독을 headless로 검증한다.
/// </summary>
public partial class Gl001Step3AdapterTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[GL-001 Step3] Running skill report adapter tests...");
        try
        {
            TestDetailKindConversions();
            TestCastCancelFactionSplit();
            TestMalformedAndUnknownFailClosed();
            TestPublishReports();
            TestSceneTreeFactionResolver();
            TestGameLogServiceAutoload();
            TestControllerResolvesServiceModelAndInjectionIsolates();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[GL-001 Step3] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0)
        {
            GD.Print("[GL-001 Step3] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[GL-001 Step3] FAILED: {_failures} assertion(s)");
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

    // [A] Detail 등급 report들이 Combat/Detail/Normal + TitleKey/Args/fallback으로 변환된다.
    private void TestDetailKindConversions()
    {
        GD.Print("[A] Detail-kind conversions");
        AssertDetail("damage", "damage:physical:/root/M/Opponent/Goblin", "combat.skill.damage",
            ("tag", "physical"), ("target", "Goblin"));
        AssertDetail("miss", "miss:magical:/root/M/Opponent/Goblin", "combat.skill.miss",
            ("tag", "magical"), ("target", "Goblin"));
        AssertDetail("stun", "stun:2:/root/M/Opponent/Goblin", "combat.skill.stun",
            ("turns", "2"), ("target", "Goblin"));
        AssertDetail("stun_miss", "stun_miss:/root/M/Opponent/Goblin", "combat.skill.stun_miss",
            ("target", "Goblin"));
        AssertDetail("bind", "bind:3:/root/M/Opponent/Goblin", "combat.skill.bind",
            ("turns", "3"), ("target", "Goblin"));
        AssertDetail("chain", "chain:1:/root/M/Opponent/Goblin", "combat.skill.chain",
            ("index", "1"), ("target", "Goblin"));
        AssertDetail("knockback", "knockback:2:/root/M/Opponent/Goblin", "combat.skill.knockback",
            ("distance", "2"), ("target", "Goblin"));
        AssertDetail("manadrain", "manadrain:16:/root/M/Opponent/Goblin", "combat.skill.manadrain",
            ("amount", "16"), ("target", "Goblin"));

        // selfbuff는 caster path + "multxturns" 파싱을 확인한다.
        GameLogEntry buff = SkillReportToGameLogAdapter.Convert("selfbuff:DamageDealt:1.5x2:/root/M/Ally/Hero", null);
        CheckTrue("A.selfbuff.notNull", buff != null);
        if (buff != null)
        {
            CheckEqual("A.selfbuff.channel", buff.Channel, GameLogChannel.Combat);
            CheckEqual("A.selfbuff.severity", buff.Severity, GameLogSeverity.Detail);
            CheckEqual("A.selfbuff.importance", buff.Importance, GameLogImportance.Normal);
            CheckEqual("A.selfbuff.key", buff.TitleKey, "combat.skill.selfbuff");
            CheckEqual("A.selfbuff.kind", buff.Args["kind"], "DamageDealt");
            CheckEqual("A.selfbuff.multiplier", buff.Args["multiplier"], "1.5");
            CheckEqual("A.selfbuff.turns", buff.Args["turns"], "2");
            CheckEqual("A.selfbuff.target", buff.Args["target"], "Hero");
        }
    }

    private void AssertDetail(string label, string raw, string expectedKey,
        params (string Name, string Value)[] expectedArgs)
    {
        GameLogEntry e = SkillReportToGameLogAdapter.Convert(raw, null);
        CheckTrue($"A.{label}.notNull", e != null);
        if (e == null) return;

        CheckEqual($"A.{label}.channel", e.Channel, GameLogChannel.Combat);
        CheckEqual($"A.{label}.severity", e.Severity, GameLogSeverity.Detail);
        CheckEqual($"A.{label}.importance", e.Importance, GameLogImportance.Normal);
        CheckEqual($"A.{label}.key", e.TitleKey, expectedKey);
        CheckTrue($"A.{label}.fallbackNonEmpty", !string.IsNullOrEmpty(e.Title));
        foreach ((string name, string value) in expectedArgs)
            CheckEqual($"A.{label}.arg[{name}]", e.Args.TryGetValue(name, out string v) ? v : "<missing>", value);
    }

    // [B] cast_cancel은 진영으로 갈린다: enemy=Event/Normal, ally=Event/Warning, 그 외=fail-closed.
    private void TestCastCancelFactionSplit()
    {
        GD.Print("[B] cast_cancel faction split");
        var resolver = new FakeResolver();
        resolver.Map["/root/M/Opponent/Goblin"] = LogFaction.Enemy;
        resolver.Map["/root/M/Ally/Hero"] = LogFaction.Ally;

        GameLogEntry enemy = SkillReportToGameLogAdapter.Convert("cast_cancel:/root/M/Opponent/Goblin", resolver);
        CheckTrue("B.enemy.notNull", enemy != null);
        if (enemy != null)
        {
            CheckEqual("B.enemy.severity", enemy.Severity, GameLogSeverity.Event);
            CheckEqual("B.enemy.importance", enemy.Importance, GameLogImportance.Normal);
            CheckEqual("B.enemy.key", enemy.TitleKey, "combat.skill.cast_cancel.enemy");
            CheckEqual("B.enemy.target", enemy.Args["target"], "Goblin");
        }

        GameLogEntry ally = SkillReportToGameLogAdapter.Convert("cast_cancel:/root/M/Ally/Hero", resolver);
        CheckTrue("B.ally.notNull", ally != null);
        if (ally != null)
        {
            CheckEqual("B.ally.severity", ally.Severity, GameLogSeverity.Event);
            CheckEqual("B.ally.importance", ally.Importance, GameLogImportance.Warning);
            CheckEqual("B.ally.key", ally.TitleKey, "combat.skill.cast_cancel.ally");
        }

        // 진영 불명(map에 없음) -> Unknown -> fail-closed.
        CheckTrue("B.unknownFaction.null",
            SkillReportToGameLogAdapter.Convert("cast_cancel:/root/M/Neutral/Rock", resolver) == null);
        // resolver 없음 -> fail-closed.
        CheckTrue("B.nullResolver.null",
            SkillReportToGameLogAdapter.Convert("cast_cancel:/root/M/Opponent/Goblin", null) == null);
    }

    // [C] malformed/unknown/빈 path는 로그를 만들지 않는다.
    private void TestMalformedAndUnknownFailClosed()
    {
        GD.Print("[C] Malformed/unknown fail-closed");
        CheckTrue("C.null", SkillReportToGameLogAdapter.Convert(null, null) == null);
        CheckTrue("C.empty", SkillReportToGameLogAdapter.Convert("", null) == null);
        CheckTrue("C.oneToken", SkillReportToGameLogAdapter.Convert("damage", null) == null);
        CheckTrue("C.tooFewTokens", SkillReportToGameLogAdapter.Convert("damage:physical", null) == null);
        CheckTrue("C.tooManyTokens",
            SkillReportToGameLogAdapter.Convert("damage:physical:extra:/root/x", null) == null);
        CheckTrue("C.unknownKind", SkillReportToGameLogAdapter.Convert("bogus:/root/x", null) == null);
        CheckTrue("C.emptyPath", SkillReportToGameLogAdapter.Convert("stun:2:", null) == null);
    }

    // [D] PublishReports는 non-null만 sink에 append하고 개수를 돌려준다.
    private void TestPublishReports()
    {
        GD.Print("[D] PublishReports");
        var resolver = new FakeResolver();
        resolver.Map["/root/M/Ally/Hero"] = LogFaction.Ally;

        var reports = new List<string>
        {
            "damage:physical:/root/M/Opponent/Goblin", // Detail -> 1
            "bogus",                                    // unknown -> skip
            "cast_cancel:/root/M/Ally/Hero",            // ally Event -> 1
            "cast_cancel:/root/M/Neutral/Rock",         // 진영 불명 -> skip
        };

        var sink = new GameLogModel();
        int published = SkillReportToGameLogAdapter.PublishReports(reports, resolver, sink);
        CheckEqual("D.publishedCount", published, 2);
        CheckEqual("D.sinkCount", sink.Count, 2);
    }

    // [E] SceneTreeFactionResolver는 부모 노드 이름으로 진영을 판정한다(ArticlesContainer 규약).
    private void TestSceneTreeFactionResolver()
    {
        GD.Print("[E] SceneTreeFactionResolver parent-name faction");
        var fRoot = new Node { Name = "FactionRoot" };
        AddChild(fRoot);
        var opponent = new Node { Name = "Opponent" };
        fRoot.AddChild(opponent);
        var goblin = new Node { Name = "Goblin" };
        opponent.AddChild(goblin);
        var allies = new Node { Name = "Ally" };
        fRoot.AddChild(allies);
        var hero = new Node { Name = "Hero" };
        allies.AddChild(hero);
        var neutral = new Node { Name = "Neutral" };
        fRoot.AddChild(neutral);
        var rock = new Node { Name = "Rock" };
        neutral.AddChild(rock);

        var resolver = new SceneTreeFactionResolver(this);
        CheckEqual("E.enemy", resolver.Resolve(goblin.GetPath()), LogFaction.Enemy);
        CheckEqual("E.ally", resolver.Resolve(hero.GetPath()), LogFaction.Ally);
        CheckEqual("E.neutralUnknown", resolver.Resolve(rock.GetPath()), LogFaction.Unknown);
        CheckEqual("E.missingUnknown", resolver.Resolve("/root/does/not/exist"), LogFaction.Unknown);
        CheckEqual("E.emptyUnknown", resolver.Resolve(""), LogFaction.Unknown);

        RemoveChild(fRoot);
        fRoot.QueueFree();
    }

    // [F] GameLogService autoload가 존재하고 IGameLogSink로 모델에 append한다.
    private void TestGameLogServiceAutoload()
    {
        GD.Print("[F] GameLogService autoload wiring");
        var service = GetNodeOrNull<GameLogService>("/root/GameLogService");
        CheckTrue("F.autoloadPresent", service != null);
        if (service == null) return;

        service.Model.Clear();
        IGameLogSink sink = service; // 서비스가 IGameLogSink다.
        sink.Append(new GameLogEntry(GameLogChannel.System, GameLogSeverity.Event, GameLogImportance.Normal, "boot"));
        CheckEqual("F.appendedToModel", service.Model.Count, 1);
        service.Model.Clear();
    }

    // [G] 컨트롤러는 기본적으로 service 모델을 구독하고, 주입 시 격리된다.
    private void TestControllerResolvesServiceModelAndInjectionIsolates()
    {
        GD.Print("[G] Controller service subscription + injection isolation");
        var service = GetNodeOrNull<GameLogService>("/root/GameLogService");
        CheckTrue("G.autoloadPresent", service != null);
        if (service == null) return;
        service.Model.Clear();

        // 주입 없음 -> service 모델을 권위로 쓴다.
        var subscriber = new LogWindowController();
        AddChild(subscriber);
        CheckTrue("G.usesServiceModel", ReferenceEquals(subscriber.Model, service.Model));

        service.Model.Append(new GameLogEntry(GameLogChannel.Combat, GameLogSeverity.Event,
            GameLogImportance.Normal, "svc-event"));
        subscriber.SelectTab(LogChannelTab.All);
        CheckEqual("G.showsServiceEntry", subscriber.CurrentEntries().Count, 1);
        CheckEqual("G.rowRendered", subscriber.RowCount, 1);

        // 주입 -> 격리된 로컬 모델.
        var isolated = new LogWindowController();
        var local = new GameLogModel();
        isolated.UseModel(local);
        AddChild(isolated);
        CheckTrue("G.usesInjectedModel", ReferenceEquals(isolated.Model, local));
        CheckFalse("G.notServiceModel", ReferenceEquals(isolated.Model, service.Model));
        CheckEqual("G.isolatedEmpty", isolated.CurrentEntries().Count, 0);

        RemoveChild(subscriber);
        subscriber.QueueFree();
        RemoveChild(isolated);
        isolated.QueueFree();
        service.Model.Clear();
    }

    private sealed class FakeResolver : ISkillTargetFactionResolver
    {
        public readonly Dictionary<string, LogFaction> Map = new();
        public LogFaction Resolve(string targetPath)
            => targetPath != null && Map.TryGetValue(targetPath, out LogFaction f) ? f : LogFaction.Unknown;
    }
}
#endif
