#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.SkillSystem.Blocks;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

public partial class Sk001Step4StatusControlTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-001 Step4] Running status/control block tests...");
        try
        {
            TestControllerLogic();
            await TestStunBlock();
            await TestBindBlock();
            await TestSelfBuff();
            await TestManaDrain();
            await TestStunCancelsCasting();
            await TestTurnCirculation();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-001 Step4] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-001 Step4] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-001 Step4] FAILED: {_failures} assertion(s)");
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

    // [G] StatusController apply/expire/re-entry — 순수 로직(씬 불필요, frame 비종속).
    private void TestControllerLogic()
    {
        GD.Print("[G] StatusController apply/expire/re-entry");
        var c = new StatusController();

        // Stun: 소비는 TurnPlay가 하는 ConsumeStunForTurn로 모사.
        c.ApplyStun(2);
        Check("G.Stun_t1", c.ConsumeStunForTurn(), true);
        Check("G.Stun_t2", c.ConsumeStunForTurn(), true);
        Check("G.Stun_expired", c.ConsumeStunForTurn(), false);
        c.ApplyStun(1); // 재진입
        Check("G.Stun_reentry", c.ConsumeStunForTurn(), true);
        Check("G.Stun_reentry_expired", c.ConsumeStunForTurn(), false);

        // Bind: OnTurnStart가 이번 턴 BoundThisTurn을 확정하고 지속시간을 소모.
        c.ApplyBind(2);
        c.OnTurnStart(); Check("G.Bind_turn1", c.BoundThisTurn, true);
        c.OnTurnStart(); Check("G.Bind_turn2", c.BoundThisTurn, true);
        c.OnTurnStart(); Check("G.Bind_expired", c.BoundThisTurn, false);

        // DamageDealt buff.
        c.ApplyDamageDealtBuff(2f, 2);
        CheckEqual("G.Dealt_applied", c.DamageDealtMultiplier, 2f);
        c.OnTurnStart(); CheckEqual("G.Dealt_turn1", c.DamageDealtMultiplier, 2f);
        c.OnTurnStart(); CheckEqual("G.Dealt_expired", c.DamageDealtMultiplier, 1f);

        // DamageTaken debuff (수신자 배율 hook).
        c.ApplyDamageTakenDebuff(1.5f, 1);
        CheckEqual("G.Taken_applied", c.DamageTakenMultiplier, 1.5f);
        c.OnTurnStart(); CheckEqual("G.Taken_expired", c.DamageTakenMultiplier, 1f);

        // turns <= 0 버프는 영구 배율을 만들지 않는다(무시).
        var d = new StatusController();
        d.ApplyDamageDealtBuff(2f, 0);
        CheckEqual("G.Dealt_zeroTurns_noop", d.DamageDealtMultiplier, 1f);
        d.ApplyDamageTakenDebuff(1.5f, -1);
        CheckEqual("G.Taken_negTurns_noop", d.DamageTakenMultiplier, 1f);
    }

    // [A] StunBlock: 대상에 스턴 적용 + TurnPlay 스킵 소비.
    private async Task TestStunBlock()
    {
        GD.Print("[A] StunBlock apply + TurnPlay skip");
        var battle = await LoadBattle(111);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 6);
            IsolateOtherOpponents(battle, target);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 블록이 컨트롤러에 스턴을 배선하는지.
            var action = BuildSkill("stun_test", 3, SkillTargetSide.Enemy, new EffectBlock[] { new StunBlock { Turns = 2 } });
            var ownerNode = MakeActionOwner(caster);
            action.Init(ownerNode);
            var state = caster.CurrentTurnActionState as SkillState;
            RunToEnd(action, caster);
            CheckEqual("A.StunApplied", target.StatusController.StunTurns, 2);
            Check("A.StunReported", state != null && state.Reports.Any(r => r.StartsWith("stun:")), true);

            // TurnPlay가 스턴 턴을 스킵하고(이동 없음) 소비한다. BehaviorTree가 있는 ally로 검증.
            var stunee = caster;
            stunee.StatusController.ApplyStun(2);
            Vector2I pos = stunee.TilePosition;
            CheckEqual("A.Skip_turn1", stunee.TurnPlay(100.0), BtStatus.Success);
            CheckEqual("A.Skip_turn2", stunee.TurnPlay(100.0), BtStatus.Success);
            CheckEqual("A.NoMoveWhileStunned", stunee.TilePosition, pos);
            CheckEqual("A.StunConsumedToZero", stunee.StatusController.StunTurns, 0);

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // [B] BindBlock: 이동 노드가 이번 턴 이동을 막고 위치가 유지된다.
    private async Task TestBindBlock()
    {
        GD.Print("[B] BindBlock blocks movement");
        var battle = await LoadBattle(222);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 6);
            IsolateOtherOpponents(battle, target);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var action = BuildSkill("bind_test", 3, SkillTargetSide.Enemy, new EffectBlock[] { new BindBlock { Turns = 1 } });
            var ownerNode = MakeActionOwner(caster);
            action.Init(ownerNode);
            RunToEnd(action, caster);
            CheckEqual("B.BindApplied", target.StatusController.BindTurns, 1);

            // 대상 턴 시작: BoundThisTurn 확정. 이동 노드는 이동 없이 Success.
            target.StatusController.OnTurnStart();
            Check("B.BoundThisTurn", target.StatusController.BoundThisTurn, true);
            Vector2I pos = target.TilePosition;
            var move = new BehaviorTree_Move();
            var status = (BtStatus)typeof(BehaviorTree_Move)
                .GetMethod("PerformAction", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(move, new object[] { 100.0, target });
            CheckEqual("B.MoveBlockedReturnsSuccess", status, BtStatus.Success);
            CheckEqual("B.NoMoveWhileBound", target.TilePosition, pos);
            move.Free();

            // 다음 턴: bind 만료.
            target.StatusController.OnTurnStart();
            Check("B.BindExpired", target.StatusController.BoundThisTurn, false);

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // [C] SelfBuffBlock: 시전자 주는 피해 배율이 실제 피해에 적용된다(피해 배율 hook).
    private async Task TestSelfBuff()
    {
        GD.Print("[C] SelfBuffBlock damage multiplier");
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
            GetHealth(target).MaxHealth = 100000;
            var ownerNode = MakeActionOwner(caster);

            int baseDelta = DealDamage(caster, target, ownerNode, helper, 4242,
                new EffectBlock[] { new DamageBlock { MinDamage = 10, MaxDamage = 20, HitChance = 100 } });
            int buffedDelta = DealDamage(caster, target, ownerNode, helper, 4242,
                new EffectBlock[] { new SelfBuffBlock { DamageMultiplier = 3f, Turns = 2 }, new DamageBlock { MinDamage = 10, MaxDamage = 20, HitChance = 100 } });

            Check("C.BaseDamagePositive", baseDelta > 0, true);
            CheckEqual("C.BuffedIs3x", buffedDelta, baseDelta * 3);

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // [D] ManaDrainBlock: 대상 마나 감소 + 시전자 마나 회복.
    private async Task TestManaDrain()
    {
        GD.Print("[D] ManaDrainBlock");
        var battle = await LoadBattle(444);
        try
        {
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            caster.TilePosition = new Vector2I(5, 5);
            target.TilePosition = new Vector2I(5, 6);
            IsolateOtherOpponents(battle, target);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var casterMana = AddMana(caster, 10, 4);
            var targetMana = AddMana(target, 10, 8);

            var action = BuildSkill("drain_test", 3, SkillTargetSide.Enemy, new EffectBlock[] { new ManaDrainBlock { Amount = 3, RestoreToCaster = true } });
            var ownerNode = MakeActionOwner(caster);
            action.Init(ownerNode);
            var state = caster.CurrentTurnActionState as SkillState;
            RunToEnd(action, caster);

            CheckEqual("D.TargetManaDrained", targetMana.CurrentMana, 5);
            CheckEqual("D.CasterManaRestored", casterMana.CurrentMana, 7);
            Check("D.DrainReported", state != null && state.Reports.Any(r => r.StartsWith("manadrain:")), true);

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // [E] 스턴이 windup 중 대상의 영창을 취소하고 지불한 마나를 되돌리지 않는다.
    private async Task TestStunCancelsCasting()
    {
        GD.Print("[E] Stun cancels casting without refund");
        var battle = await LoadBattle(555);
        try
        {
            var stunner = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var victim = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            stunner.TilePosition = new Vector2I(5, 5);
            victim.TilePosition = new Vector2I(5, 6);
            IsolateOtherOpponents(battle, victim);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var victimMana = AddMana(victim, 10, 10);

            // victim이 windup 스킬을 시전(마나 5 지불) 후 영창 중 상태로 턴을 양보한다.
            var windup = BuildSkill("victim_cast", 3, SkillTargetSide.Enemy,
                new EffectBlock[] { new DamageBlock { MinDamage = 10, MaxDamage = 20, HitChance = 100 } }, manaCost: 5);
            windup.Definition.WindupCost = 1;
            var victimOwner = MakeActionOwner(victim);
            // WindupCost>0이면 Init에서 IsCasting=true가 된다. Cast 애니메이션(fixture에 없음)을 트리거하지
            // 않도록 Action은 호출하지 않고, BT가 Running action을 보관한 상태만 모사한다.
            windup.Init(victimOwner);
            CheckEqual("E.ManaPaidOnCast", victimMana.CurrentMana, 5);
            Check("E.VictimIsCasting", victim.IsCasting, true);
            victim.CurrentTurnAction = windup;

            // stunner가 victim에게 스턴 적용 -> 영창 취소.
            var stun = BuildSkill("stunner_cast", 3, SkillTargetSide.Enemy, new EffectBlock[] { new StunBlock { Turns = 1 } });
            var stunnerOwner = MakeActionOwner(stunner);
            stun.Init(stunnerOwner);
            var stunState = stunner.CurrentTurnActionState as SkillState;
            RunToEnd(stun, stunner);

            Check("E.CastingCanceled", victim.CurrentTurnAction == null && victim.CurrentTurnActionState == null, true);
            Check("E.NoLongerCasting", victim.IsCasting, false);
            CheckEqual("E.ManaNotRefunded", victimMana.CurrentMana, 5);
            CheckEqual("E.StunAppliedToVictim", victim.StatusController.StunTurns, 1);
            Check("E.CancelReported", stunState != null && stunState.Reports.Any(r => r.StartsWith("cast_cancel:")), true);

            victimOwner.Free();
            stunnerOwner.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // [F] 현재 턴 유닛의 상태를 외부에서 바꿔도 다음 턴 순서가 Priority->SpawnIndex로 유지된다(리스트 처음으로 리셋 금지).
    private async Task TestTurnCirculation()
    {
        GD.Print("[F] Turn circulation not disrupted by external state change");
        var battle = await LoadBattle(666);
        try
        {
            var helper = battle.GetNode<TurnHelper>("TurnHelper");
            var listField = typeof(TurnHelper).GetField("_turnAffectedArticleList", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var list = ((System.Collections.IEnumerable)listField.GetValue(helper)!).Cast<object>().ToList();
            Check("F.HasEnoughUnits", list.Count >= 3, true);

            // 리스트 중간 유닛을 현재 턴으로 두고, 영창 취소(외부 상태 변경)를 가한다.
            object current = list[1];
            typeof(TurnHelper).GetField("_currentTurnArticle", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(helper, current);
            if (current is CharacterArticle c) c.CancelCasting();

            var next = typeof(TurnHelper).GetMethod("GetNextTurnArticle", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(helper, null);

            Check("F.NextIsSuccessor", ReferenceEquals(next, list[2]), true);
            Check("F.NotResetToFront", ReferenceEquals(next, list[0]), false);
        }
        finally { await FreeBattle(battle); }
    }

    private int DealDamage(CharacterArticle caster, CharacterArticle target, BehaviorTree_TurnAction ownerNode,
        TurnHelper helper, long seed, EffectBlock[] effects)
    {
        GetHealth(target).CurrentHealth = 50000;
        int before = GetHealth(target).CurrentHealth;
        helper.ResetCombatRng(seed);
        var action = BuildSkill("dmg", 3, SkillTargetSide.Enemy, effects);
        action.Init(ownerNode);
        RunToEnd(action, caster);
        return before - GetHealth(target).CurrentHealth;
    }

    private static TurnAction_Skill BuildSkill(string id, int range, SkillTargetSide side, EffectBlock[] effects, int manaCost = 0)
    {
        var def = new SkillDefinition
        {
            Id = new StringName(id), Range = range, ManaCost = manaCost, AnimationName = "",
            TargetSide = side, TargetSelectorDefault = SkillTargetSelector.Nearest,
            Effects = new Array<EffectBlock>()
        };
        foreach (var e in effects) def.Effects.Add(e);
        return new TurnAction_Skill { Definition = def };
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

    private static Mana AddMana(CharacterArticle article, int max, int current)
    {
        var mana = new Mana { MaxMana = max };
        article.ArticleStatus.StatusElementsDictionary[typeof(Mana)] = mana;
        mana.Init(article);
        mana.CurrentMana = current;
        return mana;
    }

    private static void ZeroDefense(CharacterArticle article)
    {
        if (article.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Defense)) is Defense defense)
        {
            defense.Value = 0;
        }
    }

    private static void IsolateOtherOpponents(BattleFieldScene battle, CharacterArticle keep)
    {
        int far = 900;
        foreach (var opp in battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>())
        {
            if (opp != keep) { opp.TilePosition = new Vector2I(far, far); far++; }
        }
    }

    private static BehaviorTree_TurnAction MakeActionOwner(CharacterArticle caster)
    {
        var ownerNode = new BehaviorTree_TurnAction { Name = "SK001Step4ActionOwner" };
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
