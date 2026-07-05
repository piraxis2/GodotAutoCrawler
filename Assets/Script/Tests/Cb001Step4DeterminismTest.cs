#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Decorator;
using AutoCrawler.Assets.Script.TurnAction.Common;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Cb001Step4DeterminismTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private const int MaxPhysicsFrames = 36000;

    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[CB-001 Step4] Running determinism regression...");
        double previousTimeScale = Engine.TimeScale;
        Engine.TimeScale = 16.0;
        try
        {
            string runA1 = await RunBattle(777, 2.0f);
            string runA2 = await RunBattle(777, 2.0f);
            string runB = await RunBattle(999, 2.0f);
            string runC = await RunBattle(777, 6.0f);

            Check("A.same_seed_same_speed_identical", runA1 == runA2, true);
            if (runA1 != runA2) PrintDivergence("A", runA1, runA2);

            // 근접 PhysicalDamage(전투 RNG 소비)가 실제로 발생했는지 — 시드 스모크의 전제 조건
            Check("B.rng_exercised_in_runA", runA1.Contains("HP Ally/"), true);
            Check("B.rng_exercised_in_runB", runB.Contains("HP Ally/"), true);
            Check("B.different_seed_differs", runB != runA1, true);

            Check("C.same_seed_other_speed_identical", runC == runA1, true);
            if (runC != runA1) PrintDivergence("C", runA1, runC);

            // Step 3 P2 종결 조건: 근접(GetTarget)/체인(GetChainTarget)/이동 배선이 실제로 소비됐는지
            Check("D.melee_attack_wired", runA1.Contains("HP Ally/"), true);
            Check("D.chain_damage_wired", runA1.Contains("HP Opponent/"), true);
            Check("D.deaths_recorded", runA1.Contains("DEAD Opponent/"), true);
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[CB-001 Step4] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            Engine.TimeScale = previousTimeScale;
        }

        if (_failures == 0)
        {
            GD.Print("[CB-001 Step4] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[CB-001 Step4] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private void Check(string name, bool actual, bool expected)
    {
        if (actual == expected)
        {
            GD.Print($"  PASS: {name}");
        }
        else
        {
            _failures++;
            GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}");
        }
    }

    private async Task<string> RunBattle(long seed, float speed)
    {
        var packed = GD.Load<PackedScene>(BattleScenePath);
        var battle = packed.Instantiate<BattleFieldScene>();

        // 대표 전투 구성 보강: Puppet 1기에 근접 BT(단일 Move + TurnAction_Attack)를 부여해
        // PhysicalDamage(전투 RNG)와 단일 이동 경로를 보장한다. 나머지 Puppet은 체인 대상으로 남긴다.
        var meleePuppet = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
        BuildMeleeBehaviorTree(meleePuppet.GetNode("BehaviorTree"));

        var turnHelper = battle.GetNode<TurnHelper>("TurnHelper");
        typeof(TurnHelper).GetField("_combatSeed", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(turnHelper, seed);
        typeof(TurnHelper).GetProperty(nameof(TurnHelper.Speed))!.SetValue(turnHelper, speed);

        AddChild(battle);

        // 근접 Puppet이 체인에 즉사하지 않고 전투 내내 공격하도록 HP를 올린다.
        // PhysicalDamage 롤이 수십 회 누적돼야 시드 차이가 로그에 확실히 드러난다(시드 스모크의 전제).
        if (meleePuppet.ArticleStatus.StatusElementsDictionary[typeof(Health)] is Health meleeHealth)
        {
            meleeHealth.MaxHealth = 100000;
            meleeHealth.CurrentHealth = 100000;
        }

        var log = new List<string>();
        var container = battle.Articles;
        foreach (var (team, articles) in container.Articles)
        {
            foreach (var article in articles.ToList())
            {
                string label = $"{team}/{article.Name}";
                if (article.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Health)) is Health health)
                {
                    health.OnHealthChanged += (oldHealth, newHealth) => log.Add($"HP {label} {oldHealth}->{newHealth}");
                }
                article.OnDead += _ => log.Add($"DEAD {label}");
            }
        }

        var currentTurnField = typeof(TurnHelper)
            .GetField("_currentTurnArticle", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string lastTurnLabel = null;
        int frames = 0;
        while (frames < MaxPhysicsFrames && !IsBattleOver(container))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            frames++;

            if (currentTurnField.GetValue(turnHelper) is ArticleBase currentArticle
                && GodotObject.IsInstanceValid(currentArticle))
            {
                string turnLabel = $"{currentArticle.GetParent()?.Name}/{currentArticle.Name}";
                if (turnLabel != lastTurnLabel)
                {
                    lastTurnLabel = turnLabel;
                    log.Add($"TURN {turnLabel}");
                }
            }
        }

        if (frames >= MaxPhysicsFrames)
        {
            _failures++;
            log.Add("TIMEOUT");
            GD.Print($"[CB-001 Step4] FAIL: battle timed out (seed={seed}, speed={speed})");
        }

        log.Add("END");
        foreach (var (team, articles) in container.Articles)
        {
            foreach (var article in articles)
            {
                int hp = (article.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Health)) as Health)?.CurrentHealth ?? -1;
                log.Add($"FINAL {team}/{article.Name} hp={hp} tile=({article.TilePosition.X},{article.TilePosition.Y})");
            }
        }

        battle.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, null);

        GD.Print($"[CB-001 Step4] run(seed={seed}, speed={speed}) physicsFrames={frames} logLines={log.Count}");
        return string.Join("\n", log);
    }

    private static bool IsBattleOver(ArticlesContainer container)
    {
        return container.Articles["Opponent"].Count == 0 || container.Articles["Ally"].Count == 0;
    }

    private static void BuildMeleeBehaviorTree(Node behaviorTreeNode)
    {
        var selector = new BehaviorTree_Selector { Name = "Selector" };
        var findOpponent = new BehaviorTree_FindOpponent { Name = "FindOpponent" };
        var move = new BehaviorTree_Move { Name = "Move" };
        var turnAction = new BehaviorTree_TurnAction { Name = "MeleeAttack" };
        typeof(BehaviorTree_TurnAction).GetProperty(nameof(BehaviorTree_TurnAction.TurnAction))!
            .SetValue(turnAction, new TurnAction_Attack());

        findOpponent.AddChild(move);
        selector.AddChild(findOpponent);
        selector.AddChild(turnAction);
        behaviorTreeNode.AddChild(selector);
    }

    private static void PrintDivergence(string tag, string expected, string actual)
    {
        string[] expectedLines = expected.Split('\n');
        string[] actualLines = actual.Split('\n');
        int max = Math.Max(expectedLines.Length, actualLines.Length);
        for (int i = 0; i < max; i++)
        {
            string left = i < expectedLines.Length ? expectedLines[i] : "<missing>";
            string right = i < actualLines.Length ? actualLines[i] : "<missing>";
            if (left != right)
            {
                GD.Print($"  [{tag}] first divergence at line {i}: '{left}' vs '{right}'");
                return;
            }
        }
    }
}
#endif
