#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.SkillSystem.Blocks;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Sk001Step5ChainTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;
    private List<string> _lastBlockReports = new();

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-001 Step5] Running ChainBlock migration tests...");
        try
        {
            await TestBaseline();
            await TestChainSelection();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-001 Step5] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-001 Step5] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-001 Step5] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private void Check(string name, bool actual, bool expected)
    {
        if (actual == expected) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    // [A] 같은 레이아웃에서 legacy ChainLightning과 데이터 기반 ChainBlock의 대상별 HP 결과가 일치한다.
    private async Task TestBaseline()
    {
        GD.Print("[A] ChainLightning vs ChainBlock baseline");
        string legacy = await RunChain(useBlock: false, seed: 999);
        string block = await RunChain(useBlock: true, seed: 999);
        GD.Print($"    legacy: {legacy}");
        GD.Print($"    block : {block}");
        CheckEqual("A.Baseline", block, legacy);
    }

    // [B] 체인 대상 선택이 "미타격 우선 -> ADR-017 정규 순서"를 지킨다.
    private async Task TestChainSelection()
    {
        GD.Print("[B] Chain selection: unhit-priority then canonical");
        await RunChain(useBlock: true, seed: 999);
        var reports = _lastBlockReports.Where(r => r.StartsWith("chain:")).ToList();
        CheckEqual("B.ThreeHits", reports.Count, 3);

        // opp2(cy+2) 홉에서 opp1(cy+1, 이미 타격)과 opp3(cy+3, 미타격)이 모두 거리 1이다.
        // canonical(Y)만 보면 opp1(작은 Y)이지만, 미타격 우선으로 opp3가 3번째 대상이 되어야 한다.
        // 각 홉의 대상 경로 끝 이름으로 순서 확인.
        Check("B.Hop0IsOpp1", reports.Count > 0 && reports[0].EndsWith("/Character"), true);
        Check("B.Hop1IsOpp2", reports.Count > 1 && reports[1].EndsWith("/Character2"), true);
        Check("B.Hop2IsOpp3_unhitPriority", reports.Count > 2 && reports[2].EndsWith("/Character3"), true);
    }

    private async Task<string> RunChain(bool useBlock, long seed)
    {
        var battle = await LoadBattle(seed);
        try
        {
            var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
            Rect2I rect = tileMap.GetUsedRect();
            Check("MapBigEnough", rect.Size.Y >= 6, true);

            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var opps = battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>().ToList();

            int cx = rect.Position.X + rect.Size.X / 2;
            int topY = rect.Position.Y + 1;
            caster.TilePosition = new Vector2I(cx, topY);
            // opp1..opp4를 세로로 배치. 각자 이전 대상의 사거리(3) 안이며, chainCount 3은 opp1~opp3만 친다.
            for (int i = 0; i < opps.Count; i++)
            {
                opps[i].TilePosition = new Vector2I(cx, topY + 1 + i);
                GetHealth(opps[i]).MaxHealth = 1000;
                GetHealth(opps[i]).CurrentHealth = 500;
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var before = opps.Select(o => GetHealth(o).CurrentHealth).ToList();

            TurnActionBase action = useBlock
                ? BuildChainSkill()
                : new TurnAction_ChainLightning();
            var ownerNode = MakeActionOwner(caster);
            action.Init(ownerNode);
            if (useBlock) _lastBlockReports = (caster.CurrentTurnActionState as SkillState)?.Reports ?? new List<string>();
            RunToEnd(action, caster);

            var sig = string.Join("|", opps.Select((o, i) => $"{o.Name}:{before[i]}->{GetHealth(o).CurrentHealth}"));
            ownerNode.Free();
            return sig;
        }
        finally { await FreeBattle(battle); }
    }

    private static TurnAction_Skill BuildChainSkill()
    {
        var def = new SkillDefinition
        {
            Id = new StringName("chain"), Range = 3, ManaCost = 0, AnimationName = "",
            TargetSide = SkillTargetSide.Enemy, TargetSelectorDefault = SkillTargetSelector.Nearest,
            Effects = new Array<EffectBlock> { new ChainBlock { ChainCount = 3, Range = 3, MaxDamage = 20 } }
        };
        return new TurnAction_Skill { Definition = def };
    }

    private static void RunToEnd(TurnActionBase action, CharacterArticle caster)
    {
        for (int i = 0; i < 48; i++)
        {
            if (action.Action(100.0, caster) == ActionState.End) break;
        }
    }

    private async Task<BattleFieldScene> LoadBattle(long seed)
    {
        var packed = GD.Load<PackedScene>(BattleScenePath);
        var battle = packed.Instantiate<BattleFieldScene>();
        var turnHelper = battle.GetNode<TurnHelper>("TurnHelper");
        typeof(TurnHelper).GetField("_combatSeed", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(turnHelper, seed);
        typeof(TurnHelper).GetProperty(nameof(TurnHelper.Speed))!
            .SetValue(turnHelper, 0.0f);

        AddChild(battle);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        return battle;
    }

    private async Task FreeBattle(BattleFieldScene battle)
    {
        battle.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        SetBattleFieldScene(null);
    }

    private static Health GetHealth(CharacterArticle article)
    {
        return article.ArticleStatus.StatusElementsDictionary[typeof(Health)] as Health;
    }

    private static BehaviorTree_TurnAction MakeActionOwner(CharacterArticle caster)
    {
        var ownerNode = new BehaviorTree_TurnAction { Name = "SK001Step5ActionOwner" };
        typeof(BehaviorTree_Node).GetProperty(nameof(BehaviorTree_Node.Tree))!
            .SetValue(ownerNode, caster.BehaviorTree);
        return ownerNode;
    }

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }
}
#endif
