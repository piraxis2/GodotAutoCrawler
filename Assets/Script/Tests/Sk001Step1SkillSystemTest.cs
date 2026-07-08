#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.SkillSystem.Blocks;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Common;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Sk001Step1SkillSystemTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private const string SlashSkillPath = "res://Assets/SkillData/slash.tres";

    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-001 Step1] Running data-driven skill skeleton tests...");
        try
        {
            TestSlashDefinitionRoundTrip();
            await TestSlashMatchesLegacyAttackBaseline();
            await TestSkillStateIsolationForSharedResources();
            await TestInvalidDefinitionFailsClosed();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-001 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-001 Step1] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-001 Step1] FAILED: {_failures} assertion(s)");
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

    private void CheckEqual<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected))
        {
            GD.Print($"  PASS: {name}");
        }
        else
        {
            _failures++;
            GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}");
        }
    }

    private void TestSlashDefinitionRoundTrip()
    {
        GD.Print("[A] Slash SkillDefinition resource round-trip");
        var skill = ResourceLoader.Load<SkillDefinition>(SlashSkillPath);
        Check("A.load_slash", skill != null, true);
        if (skill == null) return;

        CheckEqual("A.id", skill.Id.ToString(), "slash");
        CheckEqual("A.display_name", skill.DisplayName, "베기");
        CheckEqual("A.range", skill.Range, 1);
        CheckEqual("A.effects_count", skill.Effects.Count, 1);
        Check("A.effect_type", skill.Effects[0] is DamageBlock, true);
        if (skill.Effects[0] is DamageBlock damage)
        {
            CheckEqual("A.damage_min", damage.MinDamage, 10);
            CheckEqual("A.damage_max", damage.MaxDamage, 20);
            CheckEqual("A.damage_type", damage.DamageType, SkillDamageType.Physical);
        }

        string tempPath = "user://sk001_step1_slash_roundtrip.tres";
        Error saveError = ResourceSaver.Save(skill, tempPath);
        CheckEqual("A.save_error", saveError, Error.Ok);
        var reloaded = ResourceLoader.Load<SkillDefinition>(tempPath, cacheMode: ResourceLoader.CacheMode.Ignore);
        Check("A.reload", reloaded != null, true);
        if (reloaded == null) return;
        CheckEqual("A.reload_id", reloaded.Id.ToString(), "slash");
        CheckEqual("A.reload_range", reloaded.Range, 1);
        CheckEqual("A.reload_effects_count", reloaded.Effects.Count, 1);
        Check("A.reload_effect_type", reloaded.Effects[0] is DamageBlock, true);
    }

    private async Task TestSlashMatchesLegacyAttackBaseline()
    {
        GD.Print("[B] Slash vs legacy Attack baseline");
        string legacy = await RunAttackBaseline(useSkill: false, seed: 13579);
        string slash = await RunAttackBaseline(useSkill: true, seed: 13579);
        CheckEqual("B.baseline", slash, legacy);
    }

    private async Task<string> RunAttackBaseline(bool useSkill, long seed)
    {
        var battle = await LoadBattle(seed);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            PrepareAdjacentTarget(battle, caster, target);

            var helper = battle.GetNode<TurnHelper>("TurnHelper");
            var targetHealth = GetHealth(target);
            int beforeHp = targetHealth.CurrentHealth;

            TurnActionBase action = useSkill
                ? new TurnAction_Skill { Definition = ResourceLoader.Load<SkillDefinition>(SlashSkillPath) }
                : new TurnAction_Attack();

            var ownerNode = MakeActionOwner(caster);
            List<ActionState> statuses = RunActionToEnd(action, caster, ownerNode);
            int afterHp = targetHealth.CurrentHealth;
            string nextRng = $"{helper.CombatRandRange(0.0, 100.0):F6}:{helper.CombatRandRange(1, 20)}";
            ownerNode.Free();

            return string.Join("|", statuses.Select(s => s.ToString()))
                   + $";hp={beforeHp}->{afterHp};next_rng={nextRng};anim={caster.AnimationPlayer.CurrentAnimation}";
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    private async Task TestSkillStateIsolationForSharedResources()
    {
        GD.Print("[C] SkillState isolation for shared SkillDefinition and TurnAction_Skill");
        var battle = await LoadBattle(24680);
        try
        {
            var ally = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var opponent = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            PrepareAdjacentTarget(battle, ally, opponent);

            var sharedDefinition = ResourceLoader.Load<SkillDefinition>(SlashSkillPath);
            var sharedAction = new TurnAction_Skill { Definition = sharedDefinition };
            var allyOwner = MakeActionOwner(ally);
            var opponentOwner = MakeActionOwner(opponent);

            sharedAction.Init(allyOwner);
            var allyState = ally.CurrentTurnActionState as SkillState;
            sharedAction.Init(opponentOwner);
            var opponentState = opponent.CurrentTurnActionState as SkillState;

            Check("C.ally_state_created", allyState != null, true);
            Check("C.opponent_state_created", opponentState != null, true);
            if (allyState != null && opponentState != null)
            {
                Check("C.states_are_distinct", ReferenceEquals(allyState, opponentState), false);
                Check("C.definition_shared", ReferenceEquals(allyState.Definition, opponentState.Definition), true);
                Check("C.owner_action_shared", ReferenceEquals(allyState.OwnerAction, opponentState.OwnerAction), true);
                CheckEqual("C.ally_target", allyState.ConfirmedTargets.FirstOrDefault(), opponent);
                CheckEqual("C.opponent_target", opponentState.ConfirmedTargets.FirstOrDefault(), ally);
                allyState.MarkCostUsed();
                CheckEqual("C.ally_used_cost", allyState.UsedCost, 1);
                CheckEqual("C.opponent_used_cost_unchanged", opponentState.UsedCost, 0);
                Check("C.phase_queues_distinct", ReferenceEquals(allyState.PhaseQueue, opponentState.PhaseQueue), false);
            }

            allyOwner.Free();
            opponentOwner.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    private async Task TestInvalidDefinitionFailsClosed()
    {
        GD.Print("[D] Invalid definition fail-closed");
        var battle = await LoadBattle(98765);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            PrepareAdjacentTarget(battle, caster, target);
            int beforeHp = GetHealth(target).CurrentHealth;

            var invalid = new SkillDefinition
            {
                Id = new StringName("invalid"),
                Range = -1,
                Effects = new Array<EffectBlock> { new DamageBlock() }
            };
            var action = new TurnAction_Skill { Definition = invalid };
            var ownerNode = MakeActionOwner(caster);
            action.Init(ownerNode);
            Check("D.no_state", caster.CurrentTurnActionState == null, true);
            CheckEqual("D.action_returns_end", action.Action(100.0, caster), ActionState.End);
            CheckEqual("D.hp_unchanged", GetHealth(target).CurrentHealth, beforeHp);

            var unsupportedSide = new SkillDefinition
            {
                Id = new StringName("unsupported_side"),
                Range = 1,
                TargetSide = (SkillTargetSide)999, // Undefined side to trigger fail-closed
                Effects = new Array<EffectBlock> { new DamageBlock() }
            };
            var unsupportedAction = new TurnAction_Skill { Definition = unsupportedSide };
            unsupportedAction.Init(ownerNode);
            Check("D.unsupported_side_no_state", caster.CurrentTurnActionState == null, true);
            CheckEqual("D.unsupported_side_returns_end", unsupportedAction.Action(100.0, caster), ActionState.End);
            CheckEqual("D.unsupported_side_hp_unchanged", GetHealth(target).CurrentHealth, beforeHp);
            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
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

    private static void PrepareAdjacentTarget(BattleFieldScene battle, CharacterArticle caster, CharacterArticle target)
    {
        var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
        Vector2I targetPosition = caster.TilePosition + Vector2I.Right;
        target.TilePosition = targetPosition;
        target.GlobalPosition = tileMap.ToGlobal(tileMap.MapToLocal(targetPosition));

        var health = GetHealth(target);
        health.MaxHealth = 1000;
        health.CurrentHealth = 1000;
    }

    private static Health GetHealth(CharacterArticle article)
    {
        return article.ArticleStatus.StatusElementsDictionary[typeof(Health)] as Health;
    }

    private static BehaviorTree_TurnAction MakeActionOwner(CharacterArticle caster)
    {
        var ownerNode = new BehaviorTree_TurnAction { Name = "SK001ActionOwner" };
        typeof(BehaviorTree_Node).GetProperty(nameof(BehaviorTree_Node.Tree))!
            .SetValue(ownerNode, caster.BehaviorTree);
        return ownerNode;
    }

    private static List<ActionState> RunActionToEnd(TurnActionBase action, CharacterArticle caster, Node ownerNode)
    {
        action.Init(ownerNode);
        var statuses = new List<ActionState>();
        for (int i = 0; i < 16; i++)
        {
            ActionState status = action.Action(100.0, caster);
            statuses.Add(status);
            if (status == ActionState.End) break;
        }
        return statuses;
    }

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }
}
#endif