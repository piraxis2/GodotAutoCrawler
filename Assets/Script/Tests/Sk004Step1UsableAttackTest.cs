#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.SkillSystem;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// SK-004 Step 1: usable attack gate and dynamic attack range.
// 제한 스킬이 ammo/mana 때문에 시작 불가하면 이동 판단 사거리에서 제외되어야 한다.
public partial class Sk004Step1UsableAttackTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-004 Step1] Running usable attack tests...");
        try
        {
            await TestSpentLimitedSkillDoesNotHoldMovementRange();
            await TestManaFailureDoesNotHoldMovementRange();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-004 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-004 Step1] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-004 Step1] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private async Task TestSpentLimitedSkillDoesNotHoldMovementRange()
    {
        GD.Print("[A] Spent limited skill is excluded from movement attack range");
        var battle = await LoadBattle(9041);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            ReplaceRootWithActions(caster,
                Skill("sk004_chain_spent", range: 3, SkillAmmoResetScope.Battle, ammo: 1),
                Skill("sk004_basic", range: 1, SkillAmmoResetScope.Unlimited, ammo: 0));

            Check("A.has_basic", caster.HasUsableAttack, true);
            Check("A.range_uses_basic_only", MaxDistance(caster.CalculatedAttackRange, caster.TilePosition) <= 1, true);

            caster.SkillAmmoState.Charge(new StringName("sk004_chain_spent"), 1, SkillAmmoResetScope.Battle);
            Check("A.range_uses_limited_when_charged", MaxDistance(caster.CalculatedAttackRange, caster.TilePosition) == 3, true);
        }
        finally { await FreeBattle(battle); }
    }

    private async Task TestManaFailureDoesNotHoldMovementRange()
    {
        GD.Print("[B] Mana-blocked skill is excluded from movement attack range");
        var battle = await LoadBattle(9042);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            ReplaceRootWithActions(caster,
                Skill("sk004_expensive", range: 3, SkillAmmoResetScope.Unlimited, ammo: 0, manaCost: 999),
                Skill("sk004_free_basic", range: 1, SkillAmmoResetScope.Unlimited, ammo: 0));

            Check("B.has_free_basic", caster.HasUsableAttack, true);
            Check("B.range_ignores_mana_blocked", MaxDistance(caster.CalculatedAttackRange, caster.TilePosition) <= 1, true);
        }
        finally { await FreeBattle(battle); }
    }

    private void Check(string name, bool actual, bool expected)
    {
        if (actual == expected) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    private static SkillDefinition Skill(string id, int range, SkillAmmoResetScope scope, int ammo, int manaCost = 0)
    {
        return new SkillDefinition
        {
            Id = new StringName(id),
            Range = range,
            ManaCost = manaCost,
            AmmoResetScope = scope,
            Ammo = ammo,
        };
    }

    private static void ReplaceRootWithActions(CharacterArticle caster, params SkillDefinition[] definitions)
    {
        var bt = caster.GetNode<BehaviorTree>("BehaviorTree");
        foreach (var child in bt.GetChildren().ToArray())
        {
            bt.RemoveChild(child);
            child.Free();
        }

        var root = new BehaviorTree_Selector { Name = "SK004Root" };
        foreach (var definition in definitions)
        {
            var node = new BehaviorTree_TurnAction { Name = $"Action_{definition.Id}" };
            typeof(BehaviorTree_TurnAction).GetProperty(nameof(BehaviorTree_TurnAction.TurnAction))!
                .SetValue(node, new TurnAction_Skill { Definition = definition });
            root.AddChild(node);
        }

        bt.AddChild(root);
        SetTreeRecursive(root, bt);
    }

    private static void SetTreeRecursive(BehaviorTree_Node node, BehaviorTree tree)
    {
        SetTree(node, tree);
        foreach (var child in node.GetChildren().OfType<BehaviorTree_Node>())
        {
            SetTreeRecursive(child, tree);
        }
    }

    private static void SetTree(BehaviorTree_Node node, BehaviorTree tree)
    {
        typeof(BehaviorTree_Node).GetProperty(nameof(BehaviorTree_Node.Tree))!
            .SetValue(node, tree);
    }

    private static int MaxDistance(IEnumerable<Vector2I> positions, Vector2I origin)
    {
        return positions.Select(p => Math.Abs(p.X - origin.X) + Math.Abs(p.Y - origin.Y)).DefaultIfEmpty(0).Max();
    }

    private async Task<BattleFieldScene> LoadBattle(long seed)
    {
        var packed = GD.Load<PackedScene>(BattleScenePath);
        var battle = packed.Instantiate<BattleFieldScene>();
        var turnHelper = battle.GetNode<TurnHelper>("TurnHelper");
        typeof(TurnHelper).GetField("_combatSeed", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(turnHelper, seed);
        typeof(TurnHelper).GetProperty(nameof(TurnHelper.Speed))!.SetValue(turnHelper, 0.0f);

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

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }
}
#endif