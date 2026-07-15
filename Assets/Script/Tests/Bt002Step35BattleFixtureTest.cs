#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.SkillSystem.Blocks;
using AutoCrawler.Assets.Script.TurnAction.Common;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BT-002 Step 3.5: 실제 전투 어댑터/제품 어휘를 실제 CharacterArticle 전장에서 검증한다.
//
// 완료 조건 매핑:
//  A  Mobility+1 단일 소스: 정확히 도달 / 1칸 부족 경계에서 Immediate 성립이 갈린다(실제 AStar).
//  B  성립 검사 무변형: grid 점유·유닛 위치·마나가 검사 전후로 불변이다.
//  C  실제 택틱 보드가 턴을 진행: 접근 -> 같은 대상 근접 공격(대상 HP 감소).
//  D  턴 reset: 다음 자기 턴에 latch/문맥이 초기화되고 첫 행부터 재평가된다.
//
// 상대 로스터에 의존하지 않도록 ArticlesContainer에서 참가자를 발견하고 타일을 직접 배치한다.
public partial class Bt002Step35BattleFixtureTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BT-002 Step3.5] Running real battle fixture tests...");
        try
        {
            await RunTests();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-002 Step3.5] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0)
        {
            GD.Print("[BT-002 Step3.5] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[BT-002 Step3.5] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task RunTests()
    {
        var battle = await MakeBattle();

        var (ally, enemy) = FindCombatants(battle);
        if (ally == null || enemy == null)
        {
            _failures++;
            GD.Print("  FAIL: fixture.combatants_found -> Ally/Opponent CharacterArticle을 찾지 못했습니다.");
            return;
        }

        var tileMap = BattleFieldScene.BattleField.BattleFieldTileMap;
        Rect2I region = tileMap.GetUsedRect();
        GD.Print($"  info: used_rect={region}, ally={ally.Name}, enemy={enemy.Name}");

        // 근접 공격 사거리(실제 규칙). TurnAction_Attack의 Range=1.
        var melee = new TurnAction_Attack();
        IReadOnlyList<Vector2I> meleeRange = melee.AttackRangePositions;

        var adapter = (CharacterTacticAdapter)ally.TacticAdapter;
        SetMobility(ally, 2);   // 경로 노드 예산 = 3 => 실제 이동 최대 2칸

        // 두 유닛을 한 행에 배치할 수 있는 시작점을 고른다(맵 폭 의존 제거).
        int y = region.Position.Y + region.Size.Y / 2;
        int x0 = region.Position.X + 1;
        if (region.Size.X < 7)
        {
            _failures++;
            GD.Print($"  FAIL: fixture.map_too_narrow -> width={region.Size.X}");
            return;
        }

        // ---- [A] Mobility+1 경계: 정확히 도달 vs 1칸 부족 ----------------------------------------
        // 근접 목표 타일 = 대상의 이웃. ally(x0,y), enemy(x0+3,y)이면 목표 (x0+2,y)까지 2칸 => 예산 3노드 내 도달.
        Park(enemy, new Vector2I(x0 + 3, y));
        Park(ally, new Vector2I(x0, y));
        CheckEq("A.reach_exactly_at_budget", adapter.CanReachAndActThisTurn(ally, enemy, meleeRange), true);

        // enemy를 1칸 더 밀면 목표 타일까지 3칸 => 예산 초과 => 이번 턴 행동 불가.
        Park(enemy, new Vector2I(x0 + 4, y));
        CheckEq("A.one_tile_short_not_reachable", adapter.CanReachAndActThisTurn(ally, enemy, meleeRange), false);
        // 단, 경로 자체는 존재하므로 ApproachAllowed는 성립한다.
        CheckEq("A.path_still_exists", adapter.HasApproachPath(ally, enemy, meleeRange), true);
        CheckEq("A.not_in_range_now", adapter.IsInRange(ally, enemy, meleeRange), false);

        // ---- [B] 성립 검사 무변형 ------------------------------------------------------------------
        Vector2I allyBefore = ally.TilePosition;
        Vector2I enemyBefore = enemy.TilePosition;
        var occupiedBefore = SnapshotOccupancy(tileMap, region);
        float manaBefore = GetMana(ally);

        for (int i = 0; i < 3; i++)
        {
            adapter.IsInRange(ally, enemy, meleeRange);
            adapter.CanReachAndActThisTurn(ally, enemy, meleeRange);
            adapter.HasApproachPath(ally, enemy, meleeRange);
        }

        CheckEq("B.ally_position_unchanged", ally.TilePosition, allyBefore);
        CheckEq("B.enemy_position_unchanged", enemy.TilePosition, enemyBefore);
        CheckEq("B.occupancy_unchanged", SnapshotOccupancy(tileMap, region).SequenceEqual(occupiedBefore), true);
        CheckEq("B.mana_unchanged", Math.Abs(GetMana(ally) - manaBefore) < 0.001f, true);

        // ---- [C] 실제 택틱 보드가 턴을 진행: 접근 -> 같은 대상 근접 공격 --------------------------
        // 보드: [적이 있음 -> 셀렉터(ApproachAllowed) -> 접근 -> 근접공격], [대기]
        Park(enemy, new Vector2I(x0 + 3, y));
        Park(ally, new Vector2I(x0, y));
        var root = BuildTacticBoard(ally);
        await NextFrame();   // BehaviorTree.SetTree 지연 갱신 반영

        int enemyHpBefore = GetHealth(enemy);
        BtStatus last = BtStatus.Failure;
        int ticks = 0;

        ally.ApplyTurnStartEffects();
        while (ticks++ < 400)
        {
            last = ally.TurnPlay(0.1);
            if (last != BtStatus.Running) break;
        }

        CheckEq("C.turn_completed", last, BtStatus.Success);
        CheckEq("C.approached_into_melee_range", adapter.IsInRange(ally, enemy, meleeRange), true);
        CheckEq("C.same_target_damaged", GetHealth(enemy) < enemyHpBefore, true);

        // ---- [D] 다음 자기 턴: latch/문맥 초기화 후 첫 행부터 재평가 ------------------------------
        int enemyHpAfterFirstTurn = GetHealth(enemy);

        ally.ApplyTurnStartEffects();   // 실제 턴 경계 -> ResetForNewTurn
        CheckEq("D.latch_cleared", root.LastResult, TacticResult.Ineligible);

        ticks = 0;
        while (ticks++ < 400)
        {
            last = ally.TurnPlay(0.1);
            if (last != BtStatus.Running) break;
        }

        CheckEq("D.second_turn_completed", last, BtStatus.Success);
        CheckEq("D.second_turn_attacked_again", GetHealth(enemy) < enemyHpAfterFirstTurn, true);

        // ---- [E] 실제 Immediate 행: 정확히 도달 / 1칸 부족 (adapter 직접 호출이 아닌 row/selector 경유) ----
        var immediateRoot = BuildImmediateBoard(ally);
        await NextFrame();

        // 정확히 도달 가능한 거리 -> Immediate 행이 성립하고 접근 후 공격한다.
        Park(enemy, new Vector2I(x0 + 3, y));
        Park(ally, new Vector2I(x0, y));
        int hpBeforeE1 = GetHealth(enemy);
        Vector2I allyStartE1 = ally.TilePosition;
        ally.ApplyTurnStartEffects();
        last = DriveTurn(ally);
        CheckEq("E.reach_row_completed", last, BtStatus.Success);
        CheckEq("E.reach_row_approached", ally.TilePosition != allyStartE1, true);
        CheckEq("E.reach_row_in_melee_range", adapter.IsInRange(ally, enemy, meleeRange), true);
        CheckEq("E.reach_row_attacked", GetHealth(enemy) < hpBeforeE1, true);

        // 1칸 부족 -> Immediate 행 불성립. 접근하지 않고(위치 불변) 대기 행이 턴을 소비한다.
        Park(enemy, new Vector2I(x0 + 5, y));
        Park(ally, new Vector2I(x0, y));
        int hpBeforeE2 = GetHealth(enemy);
        Vector2I allyBeforeE2 = ally.TilePosition;
        ally.ApplyTurnStartEffects();
        last = DriveTurn(ally);
        CheckEq("E.one_short_turn_completed", last, BtStatus.Success);
        CheckEq("E.one_short_did_not_approach", ally.TilePosition, allyBeforeE2);
        CheckEq("E.one_short_no_damage", GetHealth(enemy), hpBeforeE2);   // wait 행이 턴을 소비

        // ---- [F] EnemyCasting -> ContextTarget: 영창 중인 적만 성립하고 그 적을 친다 ------------------
        var (castRoot, skill) = BuildCastingInterruptBoard(ally);
        await NextFrame();

        Park(enemy, new Vector2I(x0 + 1, y));   // 근접 사거리 안
        Park(ally, new Vector2I(x0, y));
        AddMana(ally, 10, 10);
        ally.ChargeBattleAmmo();                // 택틱 보드의 제한 스킬도 충전돼야 한다(P1#2)
        CheckEq("F.tactic_skill_ammo_charged", TryAmmo(ally, skill), 2);

        // 아직 영창 중이 아니면 조건 거짓 -> 행 불성립 -> 대기.
        int hpBeforeF0 = GetHealth(enemy);
        ally.ApplyTurnStartEffects();
        last = DriveTurn(ally);
        CheckEq("F.not_casting_falls_to_wait", GetHealth(enemy), hpBeforeF0);
        CheckEq("F.not_casting_no_ammo_spent", TryAmmo(ally, skill), 2);   // 성립 검사는 탄약을 쓰지 않는다

        // 적을 영창 중으로 만든다 -> 조건 성립, 그 적을 확정 대상으로 친다.
        MakeCasting(enemy);
        int manaBeforeF = (int)GetMana(ally);
        ally.ApplyTurnStartEffects();
        last = DriveTurn(ally);

        var skillState = ally.CurrentTurnActionState as AutoCrawler.Assets.Script.SkillSystem.SkillState;
        CheckEq("F.skill_locked_on", skillState != null, true);
        CheckEq("F.confirmed_target_identity",
            skillState != null && skillState.ConfirmedTargets.Count == 1
            && ReferenceEquals(skillState.ConfirmedTargets[0], enemy), true);
        CheckEq("F.ammo_consumed_once", TryAmmo(ally, skill), 1);
        CheckEq("F.mana_consumed_once", (int)GetMana(ally), manaBeforeF - 2);
        // 멀티턴(windup) 행동은 CurrentTurnAction으로 다음 턴에 재개된다 — 이게 재개의 신호다.
        // (IsCasting은 windup phase가 끝나는 첫 턴 말에 이미 false로 내려간다.)
        CheckEq("F.current_turn_action_set", ally.CurrentTurnAction != null, true);
        CheckEq("F.turn_consumed", last, BtStatus.Success);

        // ---- [G] 멀티턴 재개: 보드 재평가 없이 CurrentTurnAction으로 이어진다 ------------------------
        int hpBeforeResume = GetHealth(enemy);
        ally.ApplyTurnStartEffects();                       // 실제 턴 경계 -> 트리 reset (+ 자연 회복)
        CheckEq("G.board_reset_before_resume", castRoot.LastResult, TacticResult.Ineligible);

        // 재개 턴 시작 시점의 자원을 기준으로 삼는다(턴 시작 자연 회복 이후).
        int ammoAtResume = TryAmmo(ally, skill);
        int manaAtResume = (int)GetMana(ally);

        last = DriveTurn(ally);

        // BT가 평가됐다면 root.LastResult가 갱신됐을 것 -> Ineligible 유지 = 보드 재평가 없이 재개했다는 증거.
        CheckEq("G.resumed_without_board_eval", castRoot.LastResult, TacticResult.Ineligible);
        CheckEq("G.resume_turn_completed", last, BtStatus.Success);
        CheckEq("G.skill_hit_confirmed_target", GetHealth(enemy) < hpBeforeResume, true);
        // 재개는 재지불하지 않는다(탄약·마나 추가 소모 없음).
        CheckEq("G.no_extra_ammo_on_resume", TryAmmo(ally, skill), ammoAtResume);
        CheckEq("G.no_extra_mana_on_resume", (int)GetMana(ally), manaAtResume);
        CheckEq("G.action_cleaned_up", ally.CurrentTurnAction == null, true);
        CheckEq("G.casting_finished", ally.IsCasting, false);

        // ---- [H] 그다음 자기 턴: 첫 행부터 재평가 ---------------------------------------------------
        MakeCasting(enemy);                                  // 다시 영창 -> 첫 행이 성립해야 한다
        ally.ApplyTurnStartEffects();
        last = DriveTurn(ally);
        CheckEq("H.reevaluated_from_first_row", castRoot.LastResult != TacticResult.Ineligible, true);
        CheckEq("H.second_cast_consumed_last_ammo", TryAmmo(ally, skill), 0);

        // teardown: 진행 중 이동 연출을 정리한다(어댑터 tween은 Kill+Dispose로 즉시 반납된다).
        adapter.CancelApproach(ally);

        // 피해 숫자 연출(`damage_floater.gd`)은 `get_tree().create_tween()`으로 SceneTree에 붙는 ~0.5초짜리
        // tween이다. 마지막 공격 직후 종료하면 이 tween이 미완료로 남아 leak으로 보고된다(택틱 런타임과 무관한
        // 기존 전투 VFX). 실제 시간을 줘서 끝나게 한 뒤 teardown한다.
        // FxPlayer 큐를 완전히 비워 남은 연출을 모두 띄우고(여기서 floater tween이 생성된다), 그 다음 실제
        // 시간을 줘서 띄워진 tween이 끝나게 한다. 순서를 바꾸거나 시간을 덜 주면 마지막 floater가 미완료로 남아
        // 종료 시 leak으로 보고된다. 이 fixture는 공격이 많아 큐가 길다.
        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < 30; i++)
            {
                BattleFieldScene.BattleField?.FxPlayer?.Tick(0.1);
                await NextFrame();
            }
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        }

        RemoveChild(battle);
        battle.QueueFree();
        await NextFrame();
        await NextFrame();
        CheckEq("teardown.battle_freed", GodotObject.IsInstanceValid(battle), false);

        // 어댑터가 들고 있던 AStarGrid2D 같은 RefCounted가 종료 전에 회수되도록 finalizer를 돌린다.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await NextFrame();
    }

    // 턴 하나를 끝까지 구동한다(Running이 아닐 때까지). TurnHelper를 끄고 직접 구동하므로, 실제 스케줄러가
    // 하듯 FxPlayer도 함께 tick해야 전투 연출(피해 숫자 등)이 쌓이지 않고 정상적으로 소진된다.
    private static BtStatus DriveTurn(CharacterArticle ally)
    {
        BtStatus last = BtStatus.Failure;
        for (int i = 0; i < 400; i++)
        {
            BattleFieldScene.BattleField?.FxPlayer?.Tick(0.1);
            last = ally.TurnPlay(0.1);
            if (last != BtStatus.Running) break;
        }
        return last;
    }

    // ---- 헬퍼 -----------------------------------------------------------------------------------

    private async System.Threading.Tasks.Task<Node> MakeBattle()
    {
        var packed = GD.Load<PackedScene>(BattleScenePath);
        var battle = packed.Instantiate<BattleFieldScene>();

        // 턴 스케줄러가 자동으로 전투를 돌리지 않게 한다(테스트가 직접 TurnPlay를 구동).
        var turnHelper = battle.GetNodeOrNull<TurnHelper>("TurnHelper");
        if (turnHelper != null) turnHelper.AutoStart = false;

        AddChild(battle);
        await NextFrame();
        return battle;
    }

    private async System.Threading.Tasks.Task NextFrame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    // 로스터 이름에 의존하지 않고 컨테이너에서 아군/상대 CharacterArticle을 찾는다.
    private static (CharacterArticle ally, CharacterArticle enemy) FindCombatants(Node battle)
    {
        var container = BattleFieldScene.BattleField?.Articles;
        if (container == null) return (null, null);

        CharacterArticle ally = container.Articles.TryGetValue("Ally", out var allies)
            ? allies.OfType<CharacterArticle>().FirstOrDefault() : null;
        CharacterArticle enemy = container.Articles.TryGetValue("Opponent", out var opponents)
            ? opponents.OfType<CharacterArticle>().FirstOrDefault() : null;

        return (ally, enemy);
    }

    // 택틱 보드를 ally의 BehaviorTree에 배선한다: [적이 있음 -> 셀렉터 -> 접근 -> 근접공격], [대기].
    private static TacticPrioritySelector BuildTacticBoard(CharacterArticle ally)
    {
        var bt = ally.GetNode<BehaviorTree>("BehaviorTree");
        foreach (Node child in bt.GetChildren())
        {
            bt.RemoveChild(child);
            child.QueueFree();
        }

        var root = new TacticPrioritySelector { Name = "TacticRoot" };

        var attackRow = new TacticRow { Name = "RowAttack", RowId = "attack" };
        attackRow.AddChild(new TacticConditionAnyEnemy { Name = "AnyEnemy" });
        attackRow.AddChild(new TacticTargetSelector
        {
            Name = "NearestEnemy",
            Policy = TacticApproachPolicy.ApproachAllowed
        });
        var seq = new TacticActionSequence { Name = "SeqAttack" };
        seq.AddChild(new TacticApproach { Name = "Approach" });
        seq.AddChild(new TacticTurnAction { Name = "Melee", TurnAction = new TurnAction_Attack() });
        attackRow.AddChild(seq);
        root.AddChild(attackRow);

        var waitRow = new TacticRow { Name = "RowWait", RowId = "wait" };
        waitRow.AddChild(new TacticWait { Name = "Wait" });
        root.AddChild(waitRow);

        bt.AddChild(root);   // 트리 진입 -> 각 노드 _Ready로 자식 목록 구성
        bt.UpdateRequest();  // BehaviorTree.SetTree(new Root) 지연 갱신 요청
        return root;
    }

    // Immediate 보드: [적이 있음 -> 셀렉터(Immediate) -> 접근 -> 근접공격], [대기].
    private static TacticPrioritySelector BuildImmediateBoard(CharacterArticle ally)
    {
        var root = NewRoot(ally);

        var row = new TacticRow { Name = "RowImmediate", RowId = "immediate" };
        row.AddChild(new TacticConditionAnyEnemy { Name = "AnyEnemy" });
        row.AddChild(new TacticTargetSelector { Name = "Nearest", Policy = TacticApproachPolicy.Immediate });
        var seq = new TacticActionSequence { Name = "Seq" };
        seq.AddChild(new TacticApproach { Name = "Approach" });
        seq.AddChild(new TacticTurnAction { Name = "Melee", TurnAction = new TurnAction_Attack() });
        row.AddChild(seq);
        root.AddChild(row);

        AddWaitRow(root);
        return Attach(ally, root);
    }

    // 영창 방해 보드: [적이 영창 중 -> 셀렉터(Immediate) -> 접근 -> 멀티턴 스킬], [대기].
    private static (TacticPrioritySelector root, SkillDefinition skill) BuildCastingInterruptBoard(CharacterArticle ally)
    {
        var root = NewRoot(ally);

        // WindupCost>0 => 멀티턴 영창 => 첫 턴은 Executed(비용 잔여)로 턴 소비, 다음 턴 CurrentTurnAction 재개.
        var definition = new SkillDefinition
        {
            Id = new StringName("bt002_tactic_bolt"),
            Range = 1,
            ActCost = 1,
            WindupCost = 1,
            ManaCost = 2,
            AmmoResetScope = SkillAmmoResetScope.Battle,
            Ammo = 2,
            TargetSide = SkillTargetSide.Enemy,
            Effects = new Godot.Collections.Array<EffectBlock> { new DamageBlock { MinDamage = 5, MaxDamage = 5 } }
        };

        var row = new TacticRow { Name = "RowInterrupt", RowId = "interrupt" };
        row.AddChild(new TacticConditionEnemyCasting { Name = "EnemyCasting" });
        row.AddChild(new TacticTargetSelector { Name = "ContextTarget", Policy = TacticApproachPolicy.Immediate });
        var seq = new TacticActionSequence { Name = "Seq" };
        seq.AddChild(new TacticApproach { Name = "Approach" });
        seq.AddChild(new TacticTurnAction
        {
            Name = "Bolt",
            TurnAction = new TurnAction_Skill { Definition = definition }
        });
        row.AddChild(seq);
        root.AddChild(row);

        AddWaitRow(root);
        return (Attach(ally, root), definition);
    }

    private static TacticPrioritySelector NewRoot(CharacterArticle ally)
    {
        var bt = ally.GetNode<BehaviorTree>("BehaviorTree");
        foreach (Node child in bt.GetChildren())
        {
            bt.RemoveChild(child);
            child.QueueFree();
        }
        return new TacticPrioritySelector { Name = "TacticRoot" };
    }

    private static void AddWaitRow(TacticPrioritySelector root)
    {
        var waitRow = new TacticRow { Name = "RowWait", RowId = "wait" };
        waitRow.AddChild(new TacticWait { Name = "Wait" });
        root.AddChild(waitRow);
    }

    private static TacticPrioritySelector Attach(CharacterArticle ally, TacticPrioritySelector root)
    {
        var bt = ally.GetNode<BehaviorTree>("BehaviorTree");
        bt.AddChild(root);
        bt.UpdateRequest();
        return root;
    }

    // 적을 영창 중 상태로 만든다(CharacterArticle.IsCasting의 진실은 CurrentTurnActionState).
    private static void MakeCasting(CharacterArticle enemy)
    {
        var definition = new SkillDefinition
        {
            Id = new StringName("bt002_enemy_cast"),
            Range = 1,
            WindupCost = 1,
            Effects = new Godot.Collections.Array<EffectBlock> { new DamageBlock { MinDamage = 1, MaxDamage = 1 } }
        };
        var skill = new TurnAction_Skill { Definition = definition };
        enemy.CurrentTurnActionState = new SkillState(skill, definition) { IsCasting = true };
    }

    private static int TryAmmo(CharacterArticle article, SkillDefinition definition)
        => article.SkillAmmoState.TryGetAmmo(definition.Id, out int remaining, out _) ? remaining : -1;

    private static void AddMana(CharacterArticle article, int max, int current)
    {
        var mana = new Mana { MaxMana = max };
        article.ArticleStatus.StatusElementsDictionary[typeof(Mana)] = mana;
        mana.Init(article);
        mana.CurrentMana = current;
    }

    private static void Park(ArticleBase article, Vector2I tile)
    {
        var tileMap = BattleFieldScene.BattleField.BattleFieldTileMap;
        article.TilePosition = tile;                                   // OnMove -> 타일 점유 갱신
        article.GlobalPosition = tileMap.ToGlobal(tileMap.MapToLocal(tile));
    }

    private static void SetMobility(ArticleBase article, int value)
    {
        if (article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Mobility), out var element)
            && element is Mobility mobility)
        {
            mobility.Value = value;
        }
    }

    private static float GetMana(ArticleBase article)
        => article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Mana), out var m) && m is Mana mana
            ? mana.CurrentMana : 0f;

    private static int GetHealth(ArticleBase article)
        => article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Health), out var h) && h is Health health
            ? health.CurrentHealth : 0;

    // 전장 타일 점유 스냅샷(성립 검사가 월드를 건드리지 않았는지 비교용).
    private static List<string> SnapshotOccupancy(BattleFieldTileMapLayer tileMap, Rect2I region)
    {
        var snapshot = new List<string>();
        for (int x = region.Position.X; x < region.Position.X + region.Size.X; x++)
        {
            for (int y = region.Position.Y; y < region.Position.Y + region.Size.Y; y++)
            {
                ArticleBase occupant = tileMap.GetArticle(new Vector2I(x, y));
                if (occupant != null) snapshot.Add($"{x},{y}={occupant.Name}");
            }
        }
        return snapshot;
    }

    private void CheckEq<T>(string name, T actual, T expected)
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
}
#endif
