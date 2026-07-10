#if TOOLS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.SkillSystem;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// SK-002 Step 3: battle-start reset scope integration. ADR-021 §4.
// self-sufficient: battle_field.tscn을 로드하되 Ally 캐릭터에만 스킬을 주입하므로 상대 로스터에 의존하지 않는다.
public partial class Sk002Step3ResetTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-002 Step3] Running battle-start ammo reset tests...");
        try
        {
            await TestChargeChargesLimitedNotUnlimited();
            await TestBattleStartLifecycleCharges();
            await TestRechargeResetsToFullNoCarryover();
            await TestResourceNotMutated();
            await TestExpeditionTreatedAsBattle();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-002 Step3] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-002 Step3] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-002 Step3] FAILED: {_failures} assertion(s)");
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

    // [A] ChargeBattleAmmo가 BT의 제한 TurnAction_Skill을 full 충전하고 Unlimited는 등록하지 않는다.
    private async Task TestChargeChargesLimitedNotUnlimited()
    {
        GD.Print("[A] ChargeBattleAmmo charges limited, ignores unlimited");
        var battle = await LoadBattle(111);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var limited = LimitedDef("s3_limited", 2);
            var unlimited = new SkillDefinition
            {
                Id = new StringName("s3_unlimited"), Range = 1, AmmoResetScope = SkillAmmoResetScope.Unlimited, Ammo = 0
            };
            InjectSkill(caster, limited);
            InjectSkill(caster, unlimited);

            caster.ChargeBattleAmmo();

            bool hasLimited = caster.SkillAmmoState.TryGetAmmo(limited.Id, out int rem, out int max);
            Check("A.limited_registered", hasLimited, true);
            CheckEqual("A.limited_remaining", rem, 2);
            CheckEqual("A.limited_max", max, 2);
            Check("A.unlimited_no_entry", caster.SkillAmmoState.TryGetAmmo(unlimited.Id, out _, out _), false);

        }
        finally { await FreeBattle(battle); }
    }

    // [B] battle-start(_Ready)에서 TurnHelper가 각 유닛의 ChargeBattleAmmo를 호출한다(P1 실제 수명주기 배선).
    private async Task TestBattleStartLifecycleCharges()
    {
        GD.Print("[B] Battle-start (_Ready) charges via TurnHelper");
        var packed = GD.Load<PackedScene>(BattleScenePath);
        var battle = packed.Instantiate<BattleFieldScene>();
        try
        {
            var turnHelper = battle.GetNode<TurnHelper>("TurnHelper");
            typeof(TurnHelper).GetField("_combatSeed", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(turnHelper, 222L);
            typeof(TurnHelper).GetProperty(nameof(TurnHelper.Speed))!.SetValue(turnHelper, 0.0f);

            // _Ready 이전에 제한 스킬을 Ally BT에 주입한다.
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var limited = LimitedDef("s3_lifecycle", 3);
            InjectSkill(caster, limited);

            AddChild(battle); // TurnHelper._Ready 발화 → ChargeBattleAmmo 호출돼야 한다.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            bool charged = caster.SkillAmmoState.TryGetAmmo(limited.Id, out int rem, out _);
            Check("B.charged_at_battle_start", charged, true);
            CheckEqual("B.remaining_is_full", rem, 3);
        }
        finally { await FreeBattle(battle); }
    }

    // [C] Battle scope는 이월 없음: 소모 후 재충전하면 full로 리셋된다.
    private async Task TestRechargeResetsToFullNoCarryover()
    {
        GD.Print("[C] Recharge resets to full (Battle, no carryover)");
        var battle = await LoadBattle(333);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var def = LimitedDef("s3_recharge", 2);
            InjectSkill(caster, def);

            caster.ChargeBattleAmmo();
            caster.SkillAmmoState.Consume(def.Id);
            caster.SkillAmmoState.TryGetAmmo(def.Id, out int afterConsume, out _);
            CheckEqual("C.after_consume", afterConsume, 1);

            caster.ChargeBattleAmmo(); // 다음 전투 시작 reset
            caster.SkillAmmoState.TryGetAmmo(def.Id, out int afterRecharge, out _);
            CheckEqual("C.recharged_to_full", afterRecharge, 2);

        }
        finally { await FreeBattle(battle); }
    }

    // [D] remaining은 SkillAmmoState에만 살고 SkillDefinition Resource의 Ammo는 불변이다.
    private async Task TestResourceNotMutated()
    {
        GD.Print("[D] SkillDefinition Resource is not mutated by charge/consume");
        var battle = await LoadBattle(444);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var def = LimitedDef("s3_resource", 2);
            InjectSkill(caster, def);

            caster.ChargeBattleAmmo();
            caster.SkillAmmoState.Consume(def.Id);
            caster.SkillAmmoState.Consume(def.Id);

            caster.SkillAmmoState.TryGetAmmo(def.Id, out int rem, out _);
            CheckEqual("D.state_depleted", rem, 0);
            CheckEqual("D.resource_ammo_unchanged", def.Ammo, 2);
            Check("D.no_remaining_field_on_resource", typeof(SkillDefinition).GetProperty("Remaining") == null, true);

        }
        finally { await FreeBattle(battle); }
    }

    // [E] 예약값 Expedition은 런타임에서 이월 없는 Battle로 안전 처리돼 full 충전된다.
    private async Task TestExpeditionTreatedAsBattle()
    {
        GD.Print("[E] Expedition (reserved) is charged like Battle");
        var battle = await LoadBattle(555);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var def = LimitedDef("s3_expedition", 2, SkillAmmoResetScope.Expedition);
            Check("E.valid", def.IsValidV0(), true);
            InjectSkill(caster, def);

            caster.ChargeBattleAmmo();
            bool charged = caster.SkillAmmoState.TryGetAmmo(def.Id, out int rem, out _);
            Check("E.charged", charged, true);
            CheckEqual("E.remaining_full", rem, 2);

        }
        finally { await FreeBattle(battle); }
    }

    private static SkillDefinition LimitedDef(string id, int ammo, SkillAmmoResetScope scope = SkillAmmoResetScope.Battle)
    {
        return new SkillDefinition { Id = new StringName(id), Range = 1, AmmoResetScope = scope, Ammo = ammo };
    }

    // Ally BT에 제한/무제한 TurnAction_Skill 노드를 주입한다. Root(GetChild(0)) 밖 자식이라 BT 실행을 건드리지 않고
    // ChargeBattleAmmo의 raw 노드 워크에만 잡힌다.
    private static BehaviorTree_TurnAction InjectSkill(CharacterArticle caster, SkillDefinition def)
    {
        var bt = caster.GetNode<BehaviorTree>("BehaviorTree");
        var node = new BehaviorTree_TurnAction();
        typeof(BehaviorTree_TurnAction).GetProperty(nameof(BehaviorTree_TurnAction.TurnAction))!
            .SetValue(node, new TurnAction_Skill { Definition = def });
        bt.AddChild(node);
        return node;
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
