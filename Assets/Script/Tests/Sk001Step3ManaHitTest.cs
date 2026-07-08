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
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Sk001Step3ManaHitTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-001 Step3] Running mana + hit chance payment gate tests...");
        try
        {
            await TestManaGate();
            await TestHitChanceReportSink();
            await TestHitDeterminism();
            await TestBtFailureFallback();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-001 Step3] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-001 Step3] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-001 Step3] FAILED: {_failures} assertion(s)");
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

    private async Task TestManaGate()
    {
        GD.Print("[A] Mana payment gate");
        var battle = await LoadBattle(111);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 6);
            IsolateOtherOpponents(battle, target);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var helper = battle.GetNode<TurnHelper>("TurnHelper");
            ZeroDefense(target);
            GetHealth(target).MaxHealth = 1000;
            GetHealth(target).CurrentHealth = 500;

            var damage = new DamageBlock { MinDamage = 10, MaxDamage = 20, DamageType = SkillDamageType.Physical, HitChance = 100 };
            var definition = new SkillDefinition
            {
                Id = new StringName("mana_test"), Range = 3, ManaCost = 5,
                TargetSide = SkillTargetSide.Enemy, TargetSelectorDefault = SkillTargetSelector.Nearest,
                Effects = new Array<EffectBlock> { damage }
            };
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);

            // Insufficient mana: fail-closed with no state change.
            var mana = AddMana(caster, 10, 3);
            int hpBefore = GetHealth(target).CurrentHealth;
            helper.ResetCombatRng(999);
            action.Init(ownerNode);
            var failedState = caster.CurrentTurnActionState as SkillState;
            Check("A.FailedStateFlagged", failedState is { Failed: true }, true);
            CheckEqual("A.InsufficientReturnsFailure", action.Action(100.0, caster), ActionState.Failure);
            Check("A.StateClearedAfterFailure", caster.CurrentTurnActionState == null, true);
            CheckEqual("A.ManaUnchangedOnFail", mana.CurrentMana, 3);
            CheckEqual("A.TargetHpUnchangedOnFail", GetHealth(target).CurrentHealth, hpBefore);
            double afterFail = helper.CombatRandRange(0.0, 100.0);
            helper.ResetCombatRng(999);
            CheckEqual("A.RngUnchangedOnFail", afterFail, helper.CombatRandRange(0.0, 100.0));

            // Sufficient mana: pays cost and runs.
            mana.CurrentMana = 10;
            hpBefore = GetHealth(target).CurrentHealth;
            action.Init(ownerNode);
            var runState = caster.CurrentTurnActionState as SkillState;
            Check("A.SufficientRunnable", runState is { Failed: false }, true);
            CheckEqual("A.ManaSpent", mana.CurrentMana, 5);
            RunToEnd(action, caster);
            Check("A.DamageAppliedWhenPaid", GetHealth(target).CurrentHealth < hpBefore, true);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    private async Task TestHitChanceReportSink()
    {
        GD.Print("[B] Hit chance and report sink");
        var battle = await LoadBattle(222);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 6);
            IsolateOtherOpponents(battle, target);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ZeroDefense(target);
            GetHealth(target).MaxHealth = 1000;

            var damage = new DamageBlock { MinDamage = 10, MaxDamage = 20, DamageType = SkillDamageType.Physical, HitChance = 0 };
            var definition = new SkillDefinition
            {
                Id = new StringName("hit_test"), Range = 3, ManaCost = 0,
                Effects = new Array<EffectBlock> { damage }
            };
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);

            // Guaranteed miss: no damage, miss report.
            GetHealth(target).CurrentHealth = 500;
            int hpBefore = GetHealth(target).CurrentHealth;
            action.Init(ownerNode);
            var missState = caster.CurrentTurnActionState as SkillState;
            RunToEnd(action, caster);
            CheckEqual("B.MissNoDamage", GetHealth(target).CurrentHealth, hpBefore);
            Check("B.MissReported", missState != null && missState.Reports.Any(r => r.StartsWith("miss:")), true);

            // Guaranteed hit: damage applied, damage report.
            damage.HitChance = 100;
            GetHealth(target).CurrentHealth = 500;
            hpBefore = GetHealth(target).CurrentHealth;
            action.Init(ownerNode);
            var hitState = caster.CurrentTurnActionState as SkillState;
            RunToEnd(action, caster);
            Check("B.HitDamage", GetHealth(target).CurrentHealth < hpBefore, true);
            Check("B.HitReported", hitState != null && hitState.Reports.Any(r => r.StartsWith("damage:")), true);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    private async Task TestHitDeterminism()
    {
        GD.Print("[C] Hit roll RNG determinism");
        var battle = await LoadBattle(333);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 6);
            IsolateOtherOpponents(battle, target);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var helper = battle.GetNode<TurnHelper>("TurnHelper");
            ZeroDefense(target);
            GetHealth(target).MaxHealth = 1000;

            var damage = new DamageBlock { MinDamage = 10, MaxDamage = 20, DamageType = SkillDamageType.Physical, HitChance = 0 };
            var definition = new SkillDefinition
            {
                Id = new StringName("det_test"), Range = 3, ManaCost = 0,
                Effects = new Array<EffectBlock> { damage }
            };
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);

            // Guaranteed miss consumes exactly one CombatRng draw (hit roll only, no crit/damage draws).
            GetHealth(target).CurrentHealth = 500;
            helper.ResetCombatRng(777);
            action.Init(ownerNode);
            RunToEnd(action, caster);
            double afterMiss = helper.CombatRandRange(0.0, 100.0);
            helper.ResetCombatRng(777);
            helper.CombatRandRange(0.0, 100.0); // the single hit roll the miss consumed
            CheckEqual("C.MissConsumesExactlyOneRng", afterMiss, helper.CombatRandRange(0.0, 100.0));

            // Partial hit chance is reproducible from the same seed.
            damage.HitChance = 50;
            string run1 = RunHitSignature(action, ownerNode, caster, target, helper, 555);
            string run2 = RunHitSignature(action, ownerNode, caster, target, helper, 555);
            CheckEqual("C.Reproducible", run2, run1);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    private async Task TestBtFailureFallback()
    {
        GD.Print("[D] BT failure signal on mana fail");
        var battle = await LoadBattle(444);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 6);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var damage = new DamageBlock { MinDamage = 10, MaxDamage = 20, DamageType = SkillDamageType.Physical, HitChance = 100 };
            var definition = new SkillDefinition
            {
                Id = new StringName("bt_test"), Range = 3, ManaCost = 5,
                Effects = new Array<EffectBlock> { damage }
            };
            var action = new TurnAction_Skill { Definition = definition };
            var mana = AddMana(caster, 10, 1); // insufficient

            // PerformAction path: a BT_TurnAction holding the skill reports Failure to its Selector.
            var ownerNode = MakeActionOwner(caster);
            SetTurnAction(ownerNode, action);
            BtStatus performStatus = ownerNode.Behave(100.0, caster);
            CheckEqual("D.PerformActionFailure", performStatus, BtStatus.Failure);
            Check("D.NoCurrentTurnActionState", caster.CurrentTurnActionState == null, true);
            CheckEqual("D.ManaUntouched", mana.CurrentMana, 1);

            // Resume path: TurnPlay maps a Failed state to BtStatus.Failure and clears CurrentTurnAction.
            action.Init(ownerNode);
            caster.CurrentTurnAction = action;
            BtStatus resumeStatus = caster.TurnPlay(100.0);
            CheckEqual("D.TurnPlayFailure", resumeStatus, BtStatus.Failure);
            Check("D.CurrentTurnActionCleared", caster.CurrentTurnAction == null, true);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    private string RunHitSignature(TurnAction_Skill action, BehaviorTree_TurnAction ownerNode,
        CharacterArticle caster, CharacterArticle target, TurnHelper helper, long seed)
    {
        GetHealth(target).CurrentHealth = 500;
        int hpBefore = GetHealth(target).CurrentHealth;
        helper.ResetCombatRng(seed);
        action.Init(ownerNode);
        var state = caster.CurrentTurnActionState as SkillState;
        RunToEnd(action, caster);
        int hpAfter = GetHealth(target).CurrentHealth;
        string outcome = state != null && state.Reports.Any(r => r.StartsWith("miss:")) ? "miss" : "hit";
        double nextRng = helper.CombatRandRange(0.0, 100.0);
        return $"{outcome};hp={hpBefore}->{hpAfter};next={nextRng}";
    }

    private static void RunToEnd(TurnAction_Skill action, CharacterArticle caster)
    {
        for (int i = 0; i < 16; i++)
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

    // battle_field에는 상대가 4명 있으므로, 지정 대상만 남기고 나머지를 사거리 밖으로 밀어 대상 확정을 고정한다.
    private static void IsolateOtherOpponents(BattleFieldScene battle, CharacterArticle keep)
    {
        int far = 900;
        foreach (var opp in battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>())
        {
            if (opp != keep) { opp.TilePosition = new Vector2I(far, far); far++; }
        }
    }

    // 방어력을 0으로 낮춰 피해가 확실히 양수가 되게 한다(방어력이 높으면 피해 공식이 음수가 될 수 있다).
    private static void ZeroDefense(CharacterArticle article)
    {
        if (article.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Defense)) is Defense defense)
        {
            defense.Value = 0;
        }
    }

    private static Mana AddMana(CharacterArticle article, int max, int current)
    {
        var mana = new Mana { MaxMana = max };
        article.ArticleStatus.StatusElementsDictionary[typeof(Mana)] = mana;
        mana.Init(article);
        mana.CurrentMana = current;
        return mana;
    }

    private static BehaviorTree_TurnAction MakeActionOwner(CharacterArticle caster)
    {
        var ownerNode = new BehaviorTree_TurnAction { Name = "SK001Step3ActionOwner" };
        typeof(BehaviorTree_Node).GetProperty(nameof(BehaviorTree_Node.Tree))!
            .SetValue(ownerNode, caster.BehaviorTree);
        return ownerNode;
    }

    private static void SetTurnAction(BehaviorTree_TurnAction ownerNode, TurnActionBase action)
    {
        typeof(BehaviorTree_TurnAction).GetProperty(nameof(BehaviorTree_TurnAction.TurnAction))!
            .SetValue(ownerNode, action);
    }

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene)
            .GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }
}
#endif
