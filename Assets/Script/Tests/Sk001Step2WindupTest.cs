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
using AutoCrawler.Assets.Script.TurnAction.Skill.Magic;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Sk001Step2WindupTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private const string MagicBoltPath = "res://Assets/SkillData/magicbolt.tres";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-001 Step2] Running data-driven skill windup tests...");
        try
        {
            await TestTargetSelectors();
            await TestWindupBaseline();
            await TestTurnInterleave();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-001 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-001 Step2] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-001 Step2] FAILED: {_failures} assertion(s)");
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

    private async Task TestTargetSelectors()
    {
        GD.Print("[A] Target Selectors and Sides");
        var battle = await LoadBattle(111);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var opponents = battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>().ToList();
            var opp1 = opponents[0];
            var opp2 = opponents.Count > 1 ? opponents[1] : (CharacterArticle)opp1.Duplicate();
            if (opponents.Count <= 1)
            {
                opp2.Name = "Opponent2";
                battle.GetNode("Articles/Opponent").AddChild(opp2);
            }

            // isolate other opponents by moving them out of range
            int isolationY = 1000;
            foreach (var child in opponents)
            {
                if (child != opp1 && child != opp2)
                {
                    child.TilePosition = new Vector2I(isolationY, isolationY);
                    isolationY++;
                }
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            
            var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
            caster.TilePosition = new Vector2I(5, 5);
            opp1.TilePosition = new Vector2I(5, 6);
            opp2.TilePosition = new Vector2I(5, 7);
            
            GetHealth(opp1).MaxHealth = 100; GetHealth(opp1).CurrentHealth = 50;
            GetHealth(opp2).MaxHealth = 100; GetHealth(opp2).CurrentHealth = 20;

            var definition = new SkillDefinition { Id = new StringName("test"), Range = 3, TargetSide = SkillTargetSide.Enemy };
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);

            // Nearest
            definition.TargetSelectorDefault = SkillTargetSelector.Nearest;
            action.Init(ownerNode);
            var state = caster.CurrentTurnActionState as SkillState;
            CheckEqual("A.Nearest", state?.ConfirmedTargets.FirstOrDefault(), opp1);

            // LowestHp
            definition.TargetSelectorDefault = SkillTargetSelector.LowestHp;
            action.Init(ownerNode);
            state = caster.CurrentTurnActionState as SkillState;
            CheckEqual("A.LowestHp", state?.ConfirmedTargets.FirstOrDefault(), opp2);

            // HighestHp
            definition.TargetSelectorDefault = SkillTargetSelector.HighestHp;
            action.Init(ownerNode);
            state = caster.CurrentTurnActionState as SkillState;
            CheckEqual("A.HighestHp", state?.ConfirmedTargets.FirstOrDefault(), opp1);

            // Tie-break must follow ADR-017 canonical order (Manhattan -> Y -> X), not Euclidean distance.
            // opp1 is Manhattan-nearer (3) but Euclidean-farther (9); opp2 is Manhattan-farther (4) but Euclidean-nearer (8).
            definition.Range = 5;
            definition.TargetSelectorDefault = SkillTargetSelector.LowestHp;
            opp1.TilePosition = new Vector2I(5, 8);
            opp2.TilePosition = new Vector2I(7, 7);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetHealth(opp1).CurrentHealth = 30; GetHealth(opp2).CurrentHealth = 30;
            action.Init(ownerNode);
            state = caster.CurrentTurnActionState as SkillState;
            CheckEqual("A.TieCanonical", state?.ConfirmedTargets.FirstOrDefault(), opp1);

            // Self
            definition.TargetSide = SkillTargetSide.Self;
            action.Init(ownerNode);
            state = caster.CurrentTurnActionState as SkillState;
            CheckEqual("A.Self", state?.ConfirmedTargets.FirstOrDefault(), caster);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    private async Task TestWindupBaseline()
    {
        GD.Print("[B] Windup vs legacy MagicBolt baseline");
        string legacy = await RunMagicBoltBaseline(useSkill: false, seed: 999);
        string newSkill = await RunMagicBoltBaseline(useSkill: true, seed: 999);
        CheckEqual("B.baseline", newSkill, legacy);
    }

    private async Task<string> RunMagicBoltBaseline(bool useSkill, long seed)
    {
        var battle = await LoadBattle(seed);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            
            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 7);
            
            var helper = battle.GetNode<TurnHelper>("TurnHelper");
            var targetHealth = GetHealth(target);
            int beforeHp = targetHealth.CurrentHealth;

            TurnActionBase action = useSkill
                ? new TurnAction_Skill { Definition = ResourceLoader.Load<SkillDefinition>(MagicBoltPath) }
                : new TurnAction_MagicBolt();

            var ownerNode = MakeActionOwner(caster);
            List<ActionState> statuses = new();
            
            action.Init(ownerNode);
            for (int i = 0; i < 16; i++)
            {
                ActionState status = action.Action(100.0, caster);
                statuses.Add(status);
                if (status == ActionState.End) break;
            }
            
            int afterHp = targetHealth.CurrentHealth;
            ownerNode.Free();

            return string.Join("|", statuses.Select(s => s.ToString()))
                   + $";hp={beforeHp}->{afterHp};anim={caster.AnimationPlayer.CurrentAnimation}";
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    private async Task TestTurnInterleave()
    {
        GD.Print("[C] Turn Interleave and BT Suspension");
        var battle = await LoadBattle(333);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            
            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 7);

            var definition = ResourceLoader.Load<SkillDefinition>(MagicBoltPath);
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);
            
            // Simulate first turn action loop
            action.Init(ownerNode);
            ActionState status1 = ActionState.Running;
            for (int i = 0; i < 16; i++)
            {
                status1 = action.Action(100.0, caster);
                if (status1 != ActionState.Running) break;
            }
            CheckEqual("C.FirstTurnStatus", status1, ActionState.Executed);
            
            var state = caster.CurrentTurnActionState as SkillState;
            Check("C.CastingClearedAfterWindup", state != null && !state.IsCasting, true);
            
            // Assign as current action to mock BT logic
            caster.CurrentTurnAction = action;
            
            // Next turn for caster should bypass BT and resolve instantly
            var btStatus = caster.TurnPlay(100.0);
            CheckEqual("C.BypassBT_Status", btStatus, BtStatus.Success);
            Check("C.NoLongerCasting", !state.IsCasting, true);
            
            var status2 = caster.CurrentTurnAction?.Action(100.0, caster) ?? ActionState.End;
            CheckEqual("C.SecondTurnStatus", status2, ActionState.End);
            
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

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }
}
#endif
