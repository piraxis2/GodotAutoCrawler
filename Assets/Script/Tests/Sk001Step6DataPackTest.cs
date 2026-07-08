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

public partial class Sk001Step6DataPackTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private const string DataDir = "res://Assets/SkillData/Phase1";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[SK-001 Step6] Running Phase1 data pack tests...");
        try
        {
            LoadAndValidate();
            await TestExecuteCategories();
            await TestStunChanceAndBind();
            await TestChain();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[SK-001 Step6] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            SetBattleFieldScene(null);
        }

        if (_failures == 0)
        {
            GD.Print("[SK-001 Step6] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[SK-001 Step6] FAILED: {_failures} assertion(s)");
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

    // 커밋된 12종 .tres를 읽기 전용으로 로드하고 스펙(Phase1SkillSpec)과 전체 필드/이펙트 파라미터를
    // 비교한다. 테스트는 res://에 쓰지 않는다(생성은 Sk001Phase1DataGenerator가 담당). 파일 누락/값
    // 드리프트를 모두 잡는다.
    private void LoadAndValidate()
    {
        GD.Print("[A] Load(read-only) + strict-validate 12 committed definitions");
        var spec = Phase1SkillSpec.Build();
        CheckEqual("A.SpecTwelve", spec.Count, 12);

        foreach (var (id, expected) in spec)
        {
            string path = $"{DataDir}/{id}.tres";
            Check($"A.Exists[{id}]", Godot.FileAccess.FileExists(path), true);
            var loaded = ResourceLoader.Load<SkillDefinition>(path, "", ResourceLoader.CacheMode.Ignore);
            if (loaded == null) { _failures++; GD.Print($"  FAIL: A.Load[{id}] -> null"); continue; }
            Check($"A.Valid[{id}]", loaded.IsValidV0(), true);
            // 전체 필드 + 이펙트 타입/파라미터 드리프트 검출
            CheckEqual($"A.Match[{id}]", Phase1SkillSpec.Signature(loaded), Phase1SkillSpec.Signature(expected));
        }

        // 디렉터리에 정확히 12개의 .tres만 있는지(누락/과잉 검출).
        var dir = DirAccess.Open(DataDir);
        int tresCount = dir == null ? -1 : dir.GetFiles().Count(f => f.EndsWith(".tres"));
        CheckEqual("A.ExactlyTwelveFiles", tresCount, 12);

        // 직렬화 round-trip(스크래치, 커밋 파일 비의존): 저장 -> 재로드 서명 보존.
        var probe = spec["sword_smash"];
        const string scratch = "user://sk006_roundtrip.tres";
        ResourceSaver.Save(probe, scratch);
        var rt = ResourceLoader.Load<SkillDefinition>(scratch, "", ResourceLoader.CacheMode.Ignore);
        Check("A.Roundtrip.Valid", rt is { } r && r.IsValidV0(), true);
        CheckEqual("A.Roundtrip.Match", rt == null ? "" : Phase1SkillSpec.Signature(rt), Phase1SkillSpec.Signature(probe));

        // combo 미입력: SkillDefinition에 Combo 필드 자체가 없다(ADR-018 예약, 데이터/구현 없음).
        Check("A.NoComboField", typeof(SkillDefinition).GetProperty("Combo") == null, true);
    }

    // [B] 대표 전투 fixture에서 기본기/강기/제어/흡수 실행.
    private async Task TestExecuteCategories()
    {
        GD.Print("[B] Execute categories (basic/power/control/absorb)");
        var battle = await LoadBattle(111);
        try
        {
            var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
            Rect2I rect = tileMap.GetUsedRect();
            int cx = rect.Position.X + rect.Size.X / 2;
            int topY = rect.Position.Y + 1;

            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            var ownerNode = MakeActionOwner(caster);
            AddMana(caster, 60, 60); // PC 기준 마나. 마나 소비 스킬 지불용.

            // 기본기: 베기 -> HP 감소.
            Setup(battle, tileMap, caster, target, new Vector2I(cx, topY), new Vector2I(cx, topY + 1));
            ZeroDefense(target); GetHealth(target).MaxHealth = 100000; GetHealth(target).CurrentHealth = 500;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RunSkill("sword_slash", caster, ownerNode);
            Check("B.Basic_SlashDamage", GetHealth(target).CurrentHealth < 500, true);

            // 강기: 강타 -> HP 감소 + 넉백 1칸.
            Setup(battle, tileMap, caster, target, new Vector2I(cx, topY), new Vector2I(cx, topY + 1));
            ZeroDefense(target); GetHealth(target).CurrentHealth = 500;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RunSkill("sword_smash", caster, ownerNode);
            Check("B.Power_SmashDamage", GetHealth(target).CurrentHealth < 500, true);
            CheckEqual("B.Power_SmashKnockback", target.TilePosition, new Vector2I(cx, topY + 2));

            // 제어: 금강체 -> 시전자 받는 피해 배율 0.5.
            RunSkill("sword_ironbody", caster, ownerNode);
            CheckEqual("B.Control_IronBodyTaken", caster.StatusController.DamageTakenMultiplier, 0.5f);

            // 흡수: 마나 흡수 -> 시전자 마나 회복(대상 마나 흡수).
            Setup(battle, tileMap, caster, target, new Vector2I(cx, topY), new Vector2I(cx, topY + 1));
            GetHealth(target).CurrentHealth = 500;
            var casterMana = AddMana(caster, 60, 10);
            var targetMana = AddMana(target, 30, 20);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RunSkill("staff_manaabsorb", caster, ownerNode);
            CheckEqual("B.Absorb_TargetManaDrained", targetMana.CurrentMana, 4);
            CheckEqual("B.Absorb_CasterManaGained", casterMana.CurrentMana, 26);

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // 스턴 발동 확률(Chance) 결정론 + 속박 실행.
    private async Task TestStunChanceAndBind()
    {
        GD.Print("[C] StunBlock chance (deterministic) + bind");
        var battle = await LoadBattle(222);
        try
        {
            var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
            Rect2I rect = tileMap.GetUsedRect();
            int cx = rect.Position.X + rect.Size.X / 2;
            int topY = rect.Position.Y + 1;
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var target = battle.GetNode<CharacterArticle>("Articles/Opponent/Character");
            var ownerNode = MakeActionOwner(caster);
            AddMana(caster, 60, 60); // 속박 화살(마나 20) 지불용.

            // Chance 0 -> 절대 발동 안 함.
            Setup(battle, tileMap, caster, target, new Vector2I(cx, topY), new Vector2I(cx, topY + 1));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            RunTurnAction(StunSkill(0), caster, ownerNode);
            CheckEqual("C.Chance0_NoStun", target.StatusController.StunTurns, 0);

            // Chance 100 -> 항상 발동.
            RunTurnAction(StunSkill(100), caster, ownerNode);
            CheckEqual("C.Chance100_Stun", target.StatusController.StunTurns, 1);

            // 속박 화살 -> bind 3턴.
            RunSkill("bow_bindingarrow", caster, ownerNode);
            CheckEqual("C.BindingArrow", target.StatusController.BindTurns, 3);

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    // 연쇄 뇌격 실행: 다수 대상 연쇄 피해.
    private async Task TestChain()
    {
        GD.Print("[D] Chain lightning executes");
        var battle = await LoadBattle(333);
        try
        {
            var tileMap = battle.GetNode<BattleFieldTileMapLayer>("TileMapLayer");
            Rect2I rect = tileMap.GetUsedRect();
            int cx = rect.Position.X + rect.Size.X / 2;
            int topY = rect.Position.Y + 1;
            var caster = battle.GetNode<CharacterArticle>("Articles/Ally/Character");
            var opps = battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>().ToList();
            caster.TilePosition = new Vector2I(cx, topY);
            for (int i = 0; i < opps.Count; i++)
            {
                opps[i].TilePosition = new Vector2I(cx, topY + 1 + i);
                GetHealth(opps[i]).MaxHealth = 1000; GetHealth(opps[i]).CurrentHealth = 500;
            }
            AddMana(caster, 60, 60); // 마나 30 지불 가능
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var ownerNode = MakeActionOwner(caster);
            RunSkill("staff_chainlightning", caster, ownerNode);
            int hitCount = opps.Count(o => GetHealth(o).CurrentHealth < 500);
            CheckEqual("D.ChainHitsThree", hitCount, 3);

            ownerNode.Free();
        }
        finally { await FreeBattle(battle); }
    }

    private static TurnAction_Skill StunSkill(int chance)
    {
        var def = new SkillDefinition
        {
            Id = new StringName("stun_probe"), Range = 3, ManaCost = 0, AnimationName = "",
            TargetSide = SkillTargetSide.Enemy, TargetSelectorDefault = SkillTargetSelector.Nearest,
            Effects = new Array<EffectBlock> { new StunBlock { Turns = 1, Chance = chance } }
        };
        return new TurnAction_Skill { Definition = def };
    }

    private void RunSkill(string id, CharacterArticle caster, BehaviorTree_TurnAction ownerNode)
    {
        var def = ResourceLoader.Load<SkillDefinition>($"{DataDir}/{id}.tres", "", ResourceLoader.CacheMode.Ignore);
        RunTurnAction(new TurnAction_Skill { Definition = def }, caster, ownerNode);
    }

    private static void RunTurnAction(TurnAction_Skill action, CharacterArticle caster, BehaviorTree_TurnAction ownerNode)
    {
        action.Init(ownerNode);
        for (int i = 0; i < 48; i++)
        {
            if (action.Action(100.0, caster) == ActionState.End) break;
        }
    }

    private void Setup(BattleFieldScene battle, BattleFieldTileMapLayer tileMap, CharacterArticle caster,
        CharacterArticle target, Vector2I casterPos, Vector2I targetPos)
    {
        caster.TilePosition = casterPos;
        target.TilePosition = targetPos;
        // 나머지 상대는 사거리 밖 in-bounds 칸으로 격리(맵 밖 좌표 금지).
        Rect2I rect = tileMap.GetUsedRect();
        var occupied = new HashSet<Vector2I> { casterPos, targetPos, targetPos + new Vector2I(0, 1), targetPos + new Vector2I(0, 2) };
        foreach (var opp in battle.GetNode("Articles/Opponent").GetChildren().Cast<CharacterArticle>())
        {
            if (opp == target) continue;
            for (int y = rect.Position.Y; y < rect.End.Y; y++)
            for (int x = rect.Position.X; x < rect.End.X; x++)
            {
                var t = new Vector2I(x, y);
                if (occupied.Contains(t)) continue;
                if (Math.Abs(t.X - casterPos.X) + Math.Abs(t.Y - casterPos.Y) > 5) { opp.TilePosition = t; occupied.Add(t); goto next; }
            }
            next: ;
        }
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

    private static Health GetHealth(CharacterArticle a) => a.ArticleStatus.StatusElementsDictionary[typeof(Health)] as Health;

    private static Mana AddMana(CharacterArticle a, int max, int current)
    {
        var mana = new Mana { MaxMana = max };
        a.ArticleStatus.StatusElementsDictionary[typeof(Mana)] = mana;
        mana.Init(a);
        mana.CurrentMana = current;
        return mana;
    }

    private static void ZeroDefense(CharacterArticle a)
    {
        if (a.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Defense)) is Defense d) d.Value = 0;
    }

    private static BehaviorTree_TurnAction MakeActionOwner(CharacterArticle caster)
    {
        var ownerNode = new BehaviorTree_TurnAction { Name = "SK001Step6ActionOwner" };
        typeof(BehaviorTree_Node).GetProperty(nameof(BehaviorTree_Node.Tree))!.SetValue(ownerNode, caster.BehaviorTree);
        return ownerNode;
    }

    private static void SetBattleFieldScene(BattleFieldScene battleField)
    {
        typeof(BattleFieldScene).GetField("_battleFieldScene", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, battleField);
    }
}
#endif
