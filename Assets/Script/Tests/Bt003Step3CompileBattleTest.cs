#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Compile;
using AutoCrawler.Assets.Script.SkillSystem;
using AutoCrawler.Assets.Script.SkillSystem.Blocks;
using AutoCrawler.Assets.Script.TurnAction.Common;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

// BT-003 Step 3 parity: 컴파일된 snapshot을 실제 CharacterArticle 전장에 설치해 BT-002 수제 fixture와 같은
// 행·대상·접근·행동 결과(접근 -> 같은 대상 근접 공격)를 내는지 검증한다. Step 4의 원자적 root 교체 seam 없이
// 테스트가 직접 컨테이너 root를 배선한다(설치 seam은 Step 4 범위).
public partial class Bt003Step3CompileBattleTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BT-003 Step3 Battle] Running compiled-board parity tests...");
        try { await RunTests(); }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-003 Step3 Battle] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0) { GD.Print("[BT-003 Step3 Battle] ALL PASS"); GetTree().Quit(0); }
        else { GD.Print($"[BT-003 Step3 Battle] FAILED: {_failures} assertion(s)"); GetTree().Quit(1); }
    }

    private async System.Threading.Tasks.Task RunTests()
    {
        var battle = await MakeBattle();
        var (ally, enemy) = FindCombatants();
        if (ally == null || enemy == null) { Fail("fixture.combatants_found"); return; }

        var tileMap = BattleFieldScene.BattleField.BattleFieldTileMap;
        Rect2I region = tileMap.GetUsedRect();
        if (region.Size.X < 7) { Fail("fixture.map_too_narrow"); return; }

        var melee = new TurnAction_Attack();
        IReadOnlyList<Vector2I> meleeRange = melee.AttackRangePositions;
        var adapter = (CharacterTacticAdapter)ally.TacticAdapter;
        SetMobility(ally, 2);   // 예산 3노드 => 실제 이동 최대 2칸

        int y = region.Position.Y + region.Size.Y / 2;
        int x0 = region.Position.X + 1;

        // ---- [A] compile: 최소 유효 보드([default_attack, terminal_wait]) ----------------------------
        var catalog = new TacticActionCatalog
        {
            CatalogRevision = "rev-1",
            Entries = new Array<TacticActionCatalogEntry>
            {
                new() { ActionId = "default_attack", Template = new TurnAction_Attack() }
            }
        };
        var resolver = new TacticActionCatalogResolver(catalog);
        var result = TacticBoardCompiler.Compile(MakeMinimalBoard(), resolver, catalog.CatalogRevision, ally.Name);
        CheckEq("A.compile_success", result.Success, true);
        if (result.Snapshot == null) { Fail("A.snapshot_null"); return; }

        // ---- [B] 설치: 컴파일된 root를 ally의 BehaviorTree 컨테이너에 배선(Step 4 이전 임시 배선) -------
        var root = result.Snapshot.Root;
        InstallRoot(ally, root);
        await NextFrame();   // BehaviorTree.SetTree 지연 갱신 반영

        // ---- [C] 접근 -> 같은 대상 근접 공격(BT-002 parity) -----------------------------------------
        Park(enemy, new Vector2I(x0 + 3, y));
        Park(ally, new Vector2I(x0, y));
        int enemyHpBefore = GetHealth(enemy);

        ally.ApplyTurnStartEffects();
        BtStatus last = DriveTurn(ally);

        CheckEq("C.turn_completed", last, BtStatus.Success);
        CheckEq("C.approached_into_range", adapter.IsInRange(ally, enemy, meleeRange), true);
        CheckEq("C.target_damaged", GetHealth(enemy) < enemyHpBefore, true);

        // ---- [D] 다음 자기 턴: latch/문맥 초기화 후 재평가 -> 다시 공격 ------------------------------
        int hpAfterFirst = GetHealth(enemy);
        ally.ApplyTurnStartEffects();
        CheckEq("D.latch_cleared", root.LastResult, TacticResult.Ineligible);
        last = DriveTurn(ally);
        CheckEq("D.second_turn_completed", last, BtStatus.Success);
        CheckEq("D.second_turn_attacked_again", GetHealth(enemy) < hpAfterFirst, true);

        // ---- [E] 비활성 상위 행은 발동하지 않는다: 비활성 wait를 위에 둬도 default_attack이 공격한다(P1) ----
        var boardWithDisabled = MakeMinimalBoard();
        boardWithDisabled.Rows.Insert(0, new TacticRowDefinition
        {
            RowId = "r_disabled_wait",
            Enabled = false,   // 비활성: 컴파일되지 않아야 한다(생성됐다면 첫 행에서 대기해 피해가 없다)
            Conditions = new Array<TacticConditionDefinition>(),
            Target = null,
            Action = new TacticWaitActionDefinition()
        });
        var r2 = TacticBoardCompiler.Compile(boardWithDisabled, resolver, catalog.CatalogRevision, ally.Name);
        CheckEq("E.compile_success", r2.Success, true);
        if (r2.Snapshot != null)
        {
            root = r2.Snapshot.Root;
            InstallRoot(ally, root);
            await NextFrame();
            Park(enemy, new Vector2I(x0 + 1, y));   // 근접 사거리 안
            Park(ally, new Vector2I(x0, y));
            int hpBeforeE = GetHealth(enemy);
            ally.ApplyTurnStartEffects();
            last = DriveTurn(ally);
            CheckEq("E.disabled_wait_not_floored", GetHealth(enemy) < hpBeforeE, true);
        }

        // ---- [G] EnemyCasting + NearestEnemy: 영창 적 집합 내 최근접만 확정(matrix #20) ------------------
        // A=가까운 비영창(기존 enemy), B=가까운 영창, C=먼 영창. 영창 집합 {B,C} 중 최근접 B만 피해를 받는다.
        var container = BattleFieldScene.BattleField.Articles;
        Node opponentParent = enemy.GetParent();
        CharacterArticle b = SpawnOpponent(enemy, opponentParent);
        CharacterArticle c = SpawnOpponent(enemy, opponentParent);
        await NextFrame();
        container.Articles["Opponent"].Add(b);
        container.Articles["Opponent"].Add(c);

        Park(enemy, new Vector2I(x0, y + 1));   // A: 인접 비영창(비후보)
        Park(b, new Vector2I(x0 + 1, y));       // B: 인접 영창(최근접 후보)
        Park(c, new Vector2I(x0 + 4, y));       // C: 먼 영창
        Park(ally, new Vector2I(x0, y));
        enemy.CurrentTurnActionState = null;    // A는 영창 아님
        MakeCasting(b);
        MakeCasting(c);

        var castResult = TacticBoardCompiler.Compile(MakeCastingInterruptBoard(), resolver, catalog.CatalogRevision, ally.Name);
        CheckEq("G.compile_success", castResult.Success, true);
        if (castResult.Snapshot != null)
        {
            root = castResult.Snapshot.Root;
            InstallRoot(ally, root);
            await NextFrame();
            int aBefore = GetHealth(enemy), bBefore = GetHealth(b), cBefore = GetHealth(c);
            ally.ApplyTurnStartEffects();
            last = DriveTurn(ally);
            CheckEq("G.turn_completed", last, BtStatus.Success);
            CheckEq("G.near_casting_damaged", GetHealth(b) < bBefore, true);
            CheckEq("G.near_noncasting_untouched", GetHealth(enemy), aBefore);
            CheckEq("G.far_casting_untouched", GetHealth(c), cBefore);
        }
        // 스폰한 B/C 정리(다음 [F]는 단일 main enemy로 진행).
        FreeOpponent(container, b);
        FreeOpponent(container, c);
        await NextFrame();

        // ---- [F] 대상 없음(전멸) -> default_attack 불성립 -> terminal_wait(이동·피해 없음, matrix #22) ----
        // ApproachAllowed의 default_attack은 열린 격자에서 항상 경로가 있어, "접근/공격 불가"의 결정적 케이스는
        // 살아 있는 상대가 없는 경우다. AnyEnemy 조건이 Failure -> 행 불성립 -> terminal_wait가 턴을 소비한다.
        Park(enemy, new Vector2I(x0 + 3, y));
        Park(ally, new Vector2I(x0, y));
        Vector2I allyBeforeF = ally.TilePosition;
        SetHealth(enemy, 0);   // 사망 -> IsAlive=false -> GetLivingOpponents 제외
        await NextFrame();
        ally.ApplyTurnStartEffects();
        last = DriveTurn(ally);
        CheckEq("F.no_target_turn_completed", last, BtStatus.Success);
        CheckEq("F.no_target_did_not_move", ally.TilePosition, allyBeforeF);   // 접근 없음
        CheckEq("F.wait_row_selected", root.LastResult, TacticResult.ActionCompleted);   // terminal_wait 완료

        // teardown: 진행 중 이동 연출 정리 + Fx 큐 드레인(피해 숫자 tween leak 방지, BT-002 Step3.5 동형).
        adapter.CancelApproach(ally);
        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < 30; i++) { BattleFieldScene.BattleField?.FxPlayer?.Tick(0.1); await NextFrame(); }
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        }

        RemoveChild(battle);
        battle.QueueFree();
        await NextFrame();
        await NextFrame();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await NextFrame();
    }

    // 최소 유효 보드: 잠긴 두 system row만. default_attack이 실질 공격 행이다.
    private static TacticBoardDefinition MakeMinimalBoard()
    {
        var board = new TacticBoardDefinition { SchemaVersion = 1, BoardId = "board.min", DisplayName = "min" };
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "default_attack",
            Enabled = true,
            Locked = true,
            Source = TacticRowSource.Default,
            Conditions = new Array<TacticConditionDefinition> { new TacticAnyEnemyConditionDefinition() },
            Target = new TacticNearestEnemyTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "default_attack" },
            ApproachPolicy = TacticApproachPolicy.ApproachAllowed
        });
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "terminal_wait",
            Enabled = true,
            Locked = true,
            Source = TacticRowSource.Default,
            Conditions = new Array<TacticConditionDefinition>(),
            Target = null,
            Action = new TacticWaitActionDefinition()
        });
        return board;
    }

    // [enemy_casting -> nearest_enemy -> attack] player 행 + 잠긴 두 system row. 영창 적 집합 내 최근접을 친다.
    private static TacticBoardDefinition MakeCastingInterruptBoard()
    {
        var board = MakeMinimalBoard();   // [default_attack, terminal_wait]
        board.BoardId = "board.cast";
        board.Rows.Insert(0, new TacticRowDefinition
        {
            RowId = "r_interrupt",
            Enabled = true,
            Conditions = new Array<TacticConditionDefinition> { new TacticEnemyCastingConditionDefinition() },
            Target = new TacticNearestEnemyTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "default_attack" },
            ApproachPolicy = TacticApproachPolicy.ApproachAllowed
        });
        return board;
    }

    // 상대 하나를 스폰해 Opponent 부모에 붙인다(SceneFilePath 우선, 없으면 Duplicate). 등록/배치는 호출자가 한다.
    private static CharacterArticle SpawnOpponent(CharacterArticle template, Node parent)
    {
        CharacterArticle spawned;
        string path = template.SceneFilePath;
        spawned = !string.IsNullOrEmpty(path)
            ? GD.Load<PackedScene>(path).Instantiate<CharacterArticle>()
            : (CharacterArticle)template.Duplicate();
        parent.AddChild(spawned);
        return spawned;
    }

    private static void FreeOpponent(ArticlesContainer container, CharacterArticle opponent)
    {
        container.Articles["Opponent"].Remove(opponent);
        opponent.GetParent()?.RemoveChild(opponent);
        opponent.QueueFree();
    }

    // 상대를 영창 중 상태로 만든다(CharacterArticle.IsCasting의 진실은 CurrentTurnActionState).
    private static void MakeCasting(CharacterArticle target)
    {
        var definition = new SkillDefinition
        {
            Id = new StringName("bt003_cast"),
            Range = 1,
            WindupCost = 1,
            Effects = new Array<EffectBlock> { new DamageBlock { MinDamage = 1, MaxDamage = 1 } }
        };
        var skill = new TurnAction_Skill { Definition = definition };
        target.CurrentTurnActionState = new SkillState(skill, definition) { IsCasting = true };
    }

    // 컴파일된 root를 컨테이너에 배선한다(Step 4 explicit installer 이전의 테스트 배선).
    private static void InstallRoot(CharacterArticle ally, TacticPrioritySelector root)
    {
        var bt = ally.GetNode<BehaviorTree>("BehaviorTree");
        foreach (Node child in bt.GetChildren()) { bt.RemoveChild(child); child.QueueFree(); }
        bt.AddChild(root);
        bt.UpdateRequest();
    }

    // ---- 전장 harness(BT-002 Step3.5 동형) ------------------------------------------------------

    private async System.Threading.Tasks.Task<Node> MakeBattle()
    {
        var packed = GD.Load<PackedScene>(BattleScenePath);
        var battle = packed.Instantiate<BattleFieldScene>();
        var turnHelper = battle.GetNodeOrNull<TurnHelper>("TurnHelper");
        if (turnHelper != null) turnHelper.AutoStart = false;
        AddChild(battle);
        await NextFrame();
        return battle;
    }

    private async System.Threading.Tasks.Task NextFrame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private static (CharacterArticle ally, CharacterArticle enemy) FindCombatants()
    {
        var container = BattleFieldScene.BattleField?.Articles;
        if (container == null) return (null, null);
        CharacterArticle ally = container.Articles.TryGetValue("Ally", out var allies)
            ? allies.OfType<CharacterArticle>().FirstOrDefault() : null;
        CharacterArticle enemy = container.Articles.TryGetValue("Opponent", out var opponents)
            ? opponents.OfType<CharacterArticle>().FirstOrDefault() : null;
        return (ally, enemy);
    }

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

    private static void Park(ArticleBase article, Vector2I tile)
    {
        var tileMap = BattleFieldScene.BattleField.BattleFieldTileMap;
        article.TilePosition = tile;
        article.GlobalPosition = tileMap.ToGlobal(tileMap.MapToLocal(tile));
    }

    private static void SetMobility(ArticleBase article, int value)
    {
        if (article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Mobility), out var element)
            && element is Mobility mobility)
            mobility.Value = value;
    }

    private static int GetHealth(ArticleBase article)
        => article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Health), out var h) && h is Health health
            ? health.CurrentHealth : 0;

    private static void SetHealth(ArticleBase article, int value)
    {
        if (article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Health), out var h) && h is Health health)
            health.CurrentHealth = value;
    }

    private void CheckEq<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    private void Fail(string name) { _failures++; GD.Print($"  FAIL: {name}"); }
}
#endif
