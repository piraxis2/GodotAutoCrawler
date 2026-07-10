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

// SK-002 Step 1: runtime ammo gate skeleton. ADR-021.
// Phase1 .tres는 수정하지 않고 test-only definition으로 게이트/차감/격리/무환불을 검증한다.
public partial class Sk002Step1AmmoTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-002 Step1] Running runtime ammo gate tests...");
        try
        {
            TestDefinitionRoundTrip();
            await TestUnlimitedBaselinePreserved();
            await TestLimitedConsumeThenEmptyFailure();
            await TestManaFailureDoesNotConsumeAmmo();
            await TestNoTargetDoesNotConsume();
            await TestWindupStunCancelNoRefund();
            await TestSharedDefinitionPerUnitIsolation();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-002 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-002 Step1] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-002 Step1] FAILED: {_failures} assertion(s)");
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

    // [A] 신규 ammo 필드가 .tres 저장/재로드에서 보존된다.
    private void TestDefinitionRoundTrip()
    {
        GD.Print("[A] Ammo field serialization round-trip");
        var definition = new SkillDefinition
        {
            Id = new StringName("ammo_roundtrip"),
            Range = 2,
            AmmoResetScope = SkillAmmoResetScope.Battle,
            Ammo = 3,
            Effects = new Array<EffectBlock> { new DamageBlock { MinDamage = 1, MaxDamage = 2 } }
        };

        Check("A.valid", definition.IsValidV0(), true);

        string tempPath = "user://sk002_step1_ammo_roundtrip.tres";
        Error saveError = ResourceSaver.Save(definition, tempPath);
        CheckEqual("A.save_error", saveError, Error.Ok);
        var reloaded = ResourceLoader.Load<SkillDefinition>(tempPath, cacheMode: ResourceLoader.CacheMode.Ignore);
        Check("A.reload", reloaded != null, true);
        if (reloaded == null) return;
        CheckEqual("A.reload_scope", reloaded.AmmoResetScope, SkillAmmoResetScope.Battle);
        CheckEqual("A.reload_ammo", reloaded.Ammo, 3);

        // 음수 ammo는 fail-closed.
        var invalid = new SkillDefinition { Id = new StringName("bad_ammo"), Ammo = -1 };
        Check("A.negative_ammo_invalid", invalid.IsValidV0(), false);
    }

    // [B] Unlimited 스킬은 ammo counter를 만들지 않고 기존처럼 실행된다(baseline 보존).
    private async Task TestUnlimitedBaselinePreserved()
    {
        GD.Print("[B] Unlimited skill keeps baseline, no ammo entry");
        var battle = await LoadBattle(111);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            SetAdjacent(battle, caster, target);

            var definition = MakeDamageSkill("unlimited_skill", SkillAmmoResetScope.Unlimited, 0);
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);

            int hpBefore = GetHealth(target).CurrentHealth;
            action.Init(ownerNode);
            var state = caster.CurrentTurnActionState as SkillState;
            Check("B.not_failed", state is { Failed: false }, true);
            RunToEnd(action, caster);
            Check("B.damage_applied", GetHealth(target).CurrentHealth < hpBefore, true);
            // Unlimited은 등록되지 않으므로 조회는 false.
            Check("B.no_ammo_entry", caster.SkillAmmoState.TryGetAmmo(definition.Id, out _, out _), false);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    // [C]+[D] 제한 스킬은 remaining이 있으면 1회 차감 후 실행되고, 0이면 HP/RNG 변화 없이 Failure.
    private async Task TestLimitedConsumeThenEmptyFailure()
    {
        GD.Print("[C/D] Limited skill consumes then fails when empty");
        var battle = await LoadBattle(222);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            SetAdjacent(battle, caster, target);
            var helper = battle.GetNode<TurnHelper>("TurnHelper");

            var definition = MakeDamageSkill("limited_2", SkillAmmoResetScope.Battle, 2);
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);
            caster.SkillAmmoState.Charge(definition.Id, definition.Ammo, definition.AmmoResetScope);

            // 1발째: 차감 후 실행.
            int hpBefore = GetHealth(target).CurrentHealth;
            action.Init(ownerNode);
            var state1 = caster.CurrentTurnActionState as SkillState;
            Check("C.first_not_failed", state1 is { Failed: false }, true);
            caster.SkillAmmoState.TryGetAmmo(definition.Id, out int rem1, out int max1);
            CheckEqual("C.remaining_after_1", rem1, 1);
            CheckEqual("C.max_preserved", max1, 2);
            Check("C.consume_reported", state1 != null && state1.Reports.Any(r => r.StartsWith("ammo:") && r.Contains("consumed")), true);
            RunToEnd(action, caster);
            Check("C.first_damage", GetHealth(target).CurrentHealth < hpBefore, true);

            // 2발째: 차감 후 실행 -> remaining 0.
            hpBefore = GetHealth(target).CurrentHealth;
            action.Init(ownerNode);
            RunToEnd(action, caster);
            caster.SkillAmmoState.TryGetAmmo(definition.Id, out int rem2, out _);
            CheckEqual("C.remaining_after_2", rem2, 0);
            Check("C.second_damage", GetHealth(target).CurrentHealth < hpBefore, true);

            // 3발째: remaining 0 -> HP/RNG 변화 없이 Failure.
            hpBefore = GetHealth(target).CurrentHealth;
            helper.ResetCombatRng(4242);
            action.Init(ownerNode);
            var emptyState = caster.CurrentTurnActionState as SkillState;
            Check("D.empty_failed_flagged", emptyState is { Failed: true }, true);
            Check("D.empty_reported", emptyState != null && emptyState.Reports.Any(r => r.EndsWith(":empty")), true);
            caster.CurrentTurnAction = action; // stale running action must be cleared when the skill fails.
            CheckEqual("D.empty_returns_failure", action.Action(100.0, caster), ActionState.Failure);
            Check("D.state_cleared", caster.CurrentTurnActionState == null, true);
            Check("D.current_action_cleared", caster.CurrentTurnAction == null, true);
            CheckEqual("D.hp_unchanged", GetHealth(target).CurrentHealth, hpBefore);
            double afterFail = helper.CombatRandRange(0.0, 100.0);
            helper.ResetCombatRng(4242);
            CheckEqual("D.rng_unchanged", afterFail, helper.CombatRandRange(0.0, 100.0));

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    // [E] 마나 부족 실패는 ammo를 차감하지 않는다.
    private async Task TestManaFailureDoesNotConsumeAmmo()
    {
        GD.Print("[E] Mana failure does not consume ammo");
        var battle = await LoadBattle(333);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            SetAdjacent(battle, caster, target);

            var definition = MakeDamageSkill("limited_mana", SkillAmmoResetScope.Battle, 1);
            definition.ManaCost = 5;
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);
            caster.SkillAmmoState.Charge(definition.Id, definition.Ammo, definition.AmmoResetScope);
            var mana = AddMana(caster, 10, 3); // 부족

            int hpBefore = GetHealth(target).CurrentHealth;
            action.Init(ownerNode);
            var state = caster.CurrentTurnActionState as SkillState;
            Check("E.failed_flagged", state is { Failed: true }, true);
            CheckEqual("E.returns_failure", action.Action(100.0, caster), ActionState.Failure);
            caster.SkillAmmoState.TryGetAmmo(definition.Id, out int remaining, out _);
            CheckEqual("E.ammo_not_consumed", remaining, 1);
            CheckEqual("E.mana_not_spent", mana.CurrentMana, 3);
            CheckEqual("E.hp_unchanged", GetHealth(target).CurrentHealth, hpBefore);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    // [F] 무대상 실패는 ammo/마나를 차감하지 않는다(시전 시작 시점에 사거리 내 대상 0).
    private async Task TestNoTargetDoesNotConsume()
    {
        GD.Print("[F] No target does not consume ammo/mana");
        var battle = await LoadBattle(444);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            caster.TilePosition = new Vector2I(5, 5);
            PushAllOpponentsFar(battle); // 사거리 내 대상 0
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var definition = MakeDamageSkill("limited_notarget", SkillAmmoResetScope.Battle, 1);
            definition.Range = 1;
            definition.ManaCost = 5;
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);
            caster.SkillAmmoState.Charge(definition.Id, definition.Ammo, definition.AmmoResetScope);
            var mana = AddMana(caster, 10, 10); // 충분

            action.Init(ownerNode);
            var state = caster.CurrentTurnActionState as SkillState;
            Check("F.failed_flagged", state is { Failed: true }, true);
            Check("F.no_target_locked", state != null && state.ConfirmedTargets.Count == 0, true);
            CheckEqual("F.returns_failure", action.Action(100.0, caster), ActionState.Failure);
            caster.SkillAmmoState.TryGetAmmo(definition.Id, out int remaining, out _);
            CheckEqual("F.ammo_not_consumed", remaining, 1);
            CheckEqual("F.mana_not_spent", mana.CurrentMana, 10);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    // [G] windup 중 스턴 취소는 이미 차감된 ammo를 환불하지 않는다.
    private async Task TestWindupStunCancelNoRefund()
    {
        GD.Print("[G] Windup stun cancel does not refund ammo");
        var battle = await LoadBattle(555);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            SetAdjacent(battle, caster, target);

            var definition = MakeDamageSkill("limited_windup", SkillAmmoResetScope.Battle, 1);
            definition.WindupCost = 2; // 여러 턴 장전
            var action = new TurnAction_Skill { Definition = definition };
            var ownerNode = MakeActionOwner(caster);
            caster.SkillAmmoState.Charge(definition.Id, definition.Ammo, definition.AmmoResetScope);

            // 시전 시작(커밋)에서 ammo 차감 + IsCasting.
            action.Init(ownerNode);
            var state = caster.CurrentTurnActionState as SkillState;
            Check("G.casting_started", state is { IsCasting: true, Failed: false }, true);
            caster.SkillAmmoState.TryGetAmmo(definition.Id, out int afterCommit, out _);
            CheckEqual("G.consumed_at_commit", afterCommit, 0);

            // windup 도중 스턴 -> TurnPlay가 CancelCasting.
            caster.StatusController.ApplyStun(1);
            BtStatus stunStatus = caster.TurnPlay(100.0);
            CheckEqual("G.stun_skips_turn", stunStatus, BtStatus.Success);
            Check("G.casting_canceled", caster.CurrentTurnActionState == null, true);

            // 환불 없음: remaining 그대로 0.
            caster.SkillAmmoState.TryGetAmmo(definition.Id, out int afterCancel, out _);
            CheckEqual("G.no_refund", afterCancel, 0);

            ownerNode.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    // [H] 같은 SkillDefinition을 여러 유닛이 공유해도 remaining ammo가 섞이지 않는다.
    private async Task TestSharedDefinitionPerUnitIsolation()
    {
        GD.Print("[H] Shared definition, per-unit ammo isolation");
        var battle = await LoadBattle(666);
        try
        {
            var ally = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var opponent = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            SetAdjacent(battle, ally, opponent);

            var shared = MakeDamageSkill("shared_limited", SkillAmmoResetScope.Battle, 1);
            var sharedAction = new TurnAction_Skill { Definition = shared };
            ally.SkillAmmoState.Charge(shared.Id, shared.Ammo, shared.AmmoResetScope);
            opponent.SkillAmmoState.Charge(shared.Id, shared.Ammo, shared.AmmoResetScope);

            var allyOwner = MakeActionOwner(ally);
            sharedAction.Init(allyOwner);

            ally.SkillAmmoState.TryGetAmmo(shared.Id, out int allyRemaining, out _);
            opponent.SkillAmmoState.TryGetAmmo(shared.Id, out int opponentRemaining, out _);
            CheckEqual("H.ally_consumed", allyRemaining, 0);
            CheckEqual("H.opponent_untouched", opponentRemaining, 1);
            Check("H.states_distinct", ReferenceEquals(ally.SkillAmmoState, opponent.SkillAmmoState), false);

            allyOwner.Free();
        }
        finally
        {
            await FreeBattle(battle);
        }
    }

    private static SkillDefinition MakeDamageSkill(string id, SkillAmmoResetScope scope, int ammo)
    {
        return new SkillDefinition
        {
            Id = new StringName(id),
            Range = 1,
            ManaCost = 0,
            AmmoResetScope = scope,
            Ammo = ammo,
            TargetSide = SkillTargetSide.Enemy,
            TargetSelectorDefault = SkillTargetSelector.Nearest,
            Effects = new Array<EffectBlock>
            {
                new DamageBlock { MinDamage = 10, MaxDamage = 20, DamageType = SkillDamageType.Physical, HitChance = 100 }
            }
        };
    }

    // 대상만 인접시키고 나머지 상대는 사거리 밖으로 밀며, 대상 방어력/체력을 피해 확정용으로 세팅한다.
    private void SetAdjacent(BattleFieldScene battle, CharacterArticle caster, CharacterArticle target)
    {
        var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
        caster.TilePosition = new Vector2I(5, 5);
        Vector2I targetPosition = caster.TilePosition + Vector2I.Right;
        target.TilePosition = targetPosition;
        target.GlobalPosition = tileMap.ToGlobal(tileMap.MapToLocal(targetPosition));

        foreach (var opp in battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>())
        {
            if (opp != target) { opp.TilePosition = new Vector2I(900, 900); }
        }

        ZeroDefense(target);
        var health = GetHealth(target);
        health.MaxHealth = 1000;
        health.CurrentHealth = 1000;
    }

    private static void PushAllOpponentsFar(BattleFieldScene battle)
    {
        int far = 900;
        foreach (var opp in battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>())
        {
            opp.TilePosition = new Vector2I(far, far);
            far++;
        }
    }

    private static void ZeroDefense(CharacterArticle article)
    {
        if (article.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Defense)) is Defense defense)
        {
            defense.Value = 0;
        }
    }

    private static void RunToEnd(TurnAction_Skill action, CharacterArticle caster)
    {
        for (int i = 0; i < 16; i++)
        {
            if (action.Action(100.0, caster) == ActionState.End) break;
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
        var ownerNode = new BehaviorTree_TurnAction { Name = "SK002ActionOwner" };
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
