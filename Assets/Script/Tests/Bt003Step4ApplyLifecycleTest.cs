#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Compile;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Common;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

// BT-003 Step 4: compile snapshot의 원자 apply 경계와 owner lifecycle을 실제 전장에 고정한다.
// BattleSession/UI는 범위 밖이다. 이 테스트는 prepare 상태의 CharacterArticle에만 직접 적용한다.
public partial class Bt003Step4ApplyLifecycleTest : Node
{
    private const string BattleScenePath = "res://Assets/Scenes/Map/battle_field.tscn";
    private int _failures;

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BT-003 Step4] Running apply/lifecycle tests...");
        try { await RunTests(); }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-003 Step4] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0) { GD.Print("[BT-003 Step4] ALL PASS"); GetTree().Quit(0); }
        else { GD.Print($"[BT-003 Step4] FAILED: {_failures} assertion(s)"); GetTree().Quit(1); }
    }

    private async System.Threading.Tasks.Task RunTests()
    {
        var battle = await LoadBattle();
        var (ally, enemy) = FindCombatants();
        if (ally == null || enemy == null) { Fail("fixture.combatants_found"); return; }

        var tree = ally.BehaviorTree;
        BehaviorTree_Node authoredRoot = tree.Root;
        CheckEq("A.authored_root_present", GodotObject.IsInstanceValid(authoredRoot), true);

        // [A] installer 자체는 parented root를 거부하고 기존 authored root를 보존한다.
        var parentedCandidate = new TacticPrioritySelector();
        AddChild(parentedCandidate);
        CheckEq("A.parented_root_rejected", tree.InstallRoot(parentedCandidate), false);
        CheckEq("A.rejected_keeps_authored_root", ReferenceEquals(tree.Root, authoredRoot), true);
        RemoveChild(parentedCandidate);
        parentedCandidate.Free();

        var catalog = CreateCatalog("rev-1");
        var resolver = new TacticActionCatalogResolver(catalog);

        // [B] 첫 적용: root cache/Tree와 apply metadata가 즉시 준비되어 다음 tick을 기다릴 필요가 없다.
        TacticCompileResult first = Compile(ally, resolver, catalog.CatalogRevision, "board.a");
        CheckEq("B.compile_success", first.Success, true);
        if (first.Snapshot == null) { Fail("B.snapshot_present"); return; }

        int structureUpdates = 0;
        tree.OnUpdateTree += _ => structureUpdates++;
        CheckEq("B.apply_status", ally.TryApplyCompiledTacticBoard(first), TacticBoardApplyStatus.Applied);
        CheckEq("B.root_installed_sync", ReferenceEquals(tree.Root, first.Snapshot.Root), true);
        CheckEq("B.root_tree_wired_sync", ReferenceEquals(first.Snapshot.Root.Tree, tree), true);
        CheckEq("B.structure_event_sync_once", structureUpdates, 1);
        CheckEq("B.last_good_signature", ally.IsCurrentTacticBoardInputApplied(first.Snapshot.CompileInputSignature), true);
        CheckEq("B.current_input_not_stale", ally.RequiresTacticBoardRecompile(first.Snapshot.CompileInputSignature), false);
        CheckEq("B.authored_root_freed", GodotObject.IsInstanceValid(authoredRoot), false);

        // 새 root가 첫 tick부터 쓸 수 있는지 실제 근접 공격으로 검증한다(지연 UpdateRequest 의존 금지).
        Rect2I region = BattleFieldScene.BattleField.BattleFieldTileMap.GetUsedRect();
        int y = region.Position.Y + region.Size.Y / 2;
        int x0 = region.Position.X + 1;
        Park(ally, new Vector2I(x0, y));
        Park(enemy, new Vector2I(x0 + 1, y));
        int hpBeforeFirstTick = GetHealth(enemy);
        ally.ApplyTurnStartEffects();
        CheckEq("B.first_tick_completed", DriveTurn(ally), BtStatus.Success);
        CheckEq("B.first_tick_attacked", GetHealth(enemy) < hpBeforeFirstTick, true);

        // [C] A -> B 교체: 기존 latch/action binding/root가 새 snapshot에 남지 않는다.
        TurnActionBase firstAction = first.Snapshot.Actions.Single();
        firstAction.BindExplicitTarget(enemy);
        TacticCompileResult second = Compile(ally, resolver, catalog.CatalogRevision, "board.b");
        CheckEq("C.compile_success", second.Success, true);
        if (second.Snapshot == null) { Fail("C.snapshot_present"); return; }
        CheckEq("C.apply_status", ally.TryApplyCompiledTacticBoard(second), TacticBoardApplyStatus.Applied);
        CheckEq("C.new_root_current", ReferenceEquals(tree.Root, second.Snapshot.Root), true);
        CheckEq("C.old_root_freed", GodotObject.IsInstanceValid(first.Snapshot.Root), false);
        CheckEq("C.old_binding_cleared", firstAction.IsExplicitTargetBound, false);
        CheckEq("C.old_signature_stale", ally.RequiresTacticBoardRecompile(first.Snapshot.CompileInputSignature), true);
        CheckEq("C.new_signature_current", ally.IsCurrentTacticBoardInputApplied(second.Snapshot.CompileInputSignature), true);

        // [D] 실패/중복/다른 유닛 snapshot/전투 gate는 모두 last-good root와 metadata를 보존한다.
        CheckEq("D.invalid_compile_rejected",
            ally.TryApplyCompiledTacticBoard(null), TacticBoardApplyStatus.CompilationFailed);
        CheckEq("D.invalid_keeps_root", ReferenceEquals(tree.Root, second.Snapshot.Root), true);
        CheckEq("D.duplicate_snapshot_rejected",
            ally.TryApplyCompiledTacticBoard(second), TacticBoardApplyStatus.SnapshotParented);
        CheckEq("D.duplicate_keeps_root", ReferenceEquals(tree.Root, second.Snapshot.Root), true);

        TacticCompileResult foreign = TacticBoardCompiler.Compile(
            MakeBoard("board.foreign"), resolver, catalog.CatalogRevision, "not_this_unit");
        CheckEq("D.foreign_compile_success", foreign.Success, true);
        CheckEq("D.foreign_unit_rejected",
            ally.TryApplyCompiledTacticBoard(foreign), TacticBoardApplyStatus.UnitMismatch);
        CheckEq("D.foreign_keeps_root", ReferenceEquals(tree.Root, second.Snapshot.Root), true);
        foreign.Snapshot?.FreeRoot();

        TacticCompileResult sealedCandidate = Compile(ally, resolver, catalog.CatalogRevision, "board.sealed");
        ally.SealTacticBoardForBattle();
        CheckEq("D.sealed_rejected",
            ally.TryApplyCompiledTacticBoard(sealedCandidate), TacticBoardApplyStatus.PreparationClosed);
        CheckEq("D.sealed_keeps_root", ReferenceEquals(tree.Root, second.Snapshot.Root), true);
        sealedCandidate.Snapshot?.FreeRoot();

        // [E] 같은 catalog를 두 unit이 풀어도 root/action instance는 분리된다.
        TacticCompileResult enemyResult = Compile(enemy, resolver, catalog.CatalogRevision, "board.enemy");
        CheckEq("E.enemy_compile_success", enemyResult.Success, true);
        if (enemyResult.Snapshot == null) { Fail("E.enemy_snapshot_present"); return; }
        CheckEq("E.enemy_apply_status", enemy.TryApplyCompiledTacticBoard(enemyResult), TacticBoardApplyStatus.Applied);
        CheckEq("E.roots_isolated", ReferenceEquals(second.Snapshot.Root, enemyResult.Snapshot.Root), false);
        CheckEq("E.actions_isolated", ReferenceEquals(second.Snapshot.Actions.Single(), enemyResult.Snapshot.Actions.Single()), false);
        enemyResult.Snapshot.Actions.Single().BindExplicitTarget(ally);
        CheckEq("E.binding_isolated", second.Snapshot.Actions.Single().IsExplicitTargetBound, false);

        // [F] 다시 prepare를 열어 A -> B -> A를 catalog만으로 재컴파일·적용한 뒤 owner 사망을 발생시키면 deferred teardown이 root/action/state를
        // 한 번에 제거한다. Health.Dead 콜스택에서 tree를 동기 수정하지 않는 계약도 frame 뒤에 확인한다.
        ally.BeginTacticBoardPreparation();
        TacticCompileResult third = Compile(ally, resolver, catalog.CatalogRevision, "board.a");
        CheckEq("F.reopened_apply", ally.TryApplyCompiledTacticBoard(third), TacticBoardApplyStatus.Applied);
        CheckEq("F.recompiled_a_freed_b_root", GodotObject.IsInstanceValid(second.Snapshot.Root), false);
        if (third.Snapshot == null) { Fail("F.snapshot_present"); return; }
        TurnActionBase thirdAction = third.Snapshot.Actions.Single();
        thirdAction.BindExplicitTarget(enemy);
        CheckEq("F.binding_before_death", thirdAction.IsExplicitTargetBound, true);
        SetHealth(ally, 0);
        CheckEq("F.owner_dead", ally.IsAlive, false);
        await NextFrame();
        await NextFrame();
        CheckEq("F.apply_state_cleared", ally.TacticBoardApply.HasAppliedSnapshot, false);
        CheckEq("F.preparation_closed", ally.TacticBoardApply.IsPreparationOpen, false);
        CheckEq("F.death_root_freed", GodotObject.IsInstanceValid(third.Snapshot.Root), false);
        CheckEq("F.death_binding_cleared", thirdAction.IsExplicitTargetBound, false);
        CheckEq("F.tree_no_root", tree.Root == null, true);

        await DrainBattleFx();
        RemoveChild(battle);
        battle.QueueFree();
        await NextFrame();
        await NextFrame();
        CheckEq("F.battle_freed", GodotObject.IsInstanceValid(battle), false);

        // [G] tree exit도 death와 독립적으로 snapshot action binding/state를 즉시 잊는다.
        var battle2 = await LoadBattle();
        var (ally2, enemy2) = FindCombatants();
        if (ally2 == null || enemy2 == null) { Fail("G.combatants_found"); return; }
        var catalog2 = CreateCatalog("rev-2");
        var resolver2 = new TacticActionCatalogResolver(catalog2);
        TacticCompileResult exitResult = Compile(ally2, resolver2, catalog2.CatalogRevision, "board.exit");
        CheckEq("G.compile_success", exitResult.Success, true);
        CheckEq("G.apply_status", ally2.TryApplyCompiledTacticBoard(exitResult), TacticBoardApplyStatus.Applied);
        if (exitResult.Snapshot == null) { Fail("G.snapshot_present"); return; }
        TurnActionBase exitAction = exitResult.Snapshot.Actions.Single();
        exitAction.BindExplicitTarget(enemy2);
        CheckEq("G.binding_before_exit", exitAction.IsExplicitTargetBound, true);
        RemoveChild(battle2);
        battle2.QueueFree();
        await NextFrame();
        await NextFrame();
        CheckEq("G.tree_exit_state_cleared", ally2.TacticBoardApply.HasAppliedSnapshot, false);
        CheckEq("G.tree_exit_binding_cleared", exitAction.IsExplicitTargetBound, false);
        CheckEq("G.tree_exit_root_freed", GodotObject.IsInstanceValid(exitResult.Snapshot.Root), false);
        CheckEq("G.battle2_freed", GodotObject.IsInstanceValid(battle2), false);
    }

    private static TacticActionCatalog CreateCatalog(string revision)
        => new()
        {
            CatalogRevision = revision,
            Entries = new Array<TacticActionCatalogEntry>
            {
                new() { ActionId = "default_attack", Template = new TurnAction_Attack() }
            }
        };

    private static TacticCompileResult Compile(
        CharacterArticle owner, ITacticActionResolver resolver, string revision, string boardId)
        => TacticBoardCompiler.Compile(MakeBoard(boardId), resolver, revision, owner.Name);

    // 최소 유효 보드. default_attack/terminal_wait는 draft에 직렬화되는 잠긴 system row다.
    private static TacticBoardDefinition MakeBoard(string boardId)
    {
        var board = new TacticBoardDefinition { SchemaVersion = 1, BoardId = boardId, DisplayName = boardId };
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

    private async System.Threading.Tasks.Task<BattleFieldScene> LoadBattle()
    {
        var battle = GD.Load<PackedScene>(BattleScenePath).Instantiate<BattleFieldScene>();
        var helper = battle.GetNodeOrNull<TurnHelper>("TurnHelper");
        if (helper != null) helper.AutoStart = false;
        AddChild(battle);
        await NextFrame();
        return battle;
    }

    private async System.Threading.Tasks.Task NextFrame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task DrainBattleFx()
    {
        for (int i = 0; i < 10; i++)
        {
            BattleFieldScene.BattleField?.FxPlayer?.Tick(0.1);
            await NextFrame();
        }
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
    }

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
        for (int i = 0; i < 200; i++)
        {
            BattleFieldScene.BattleField?.FxPlayer?.Tick(0.1);
            last = ally.TurnPlay(0.1);
            if (last != BtStatus.Running) break;
        }
        return last;
    }

    private static void Park(ArticleBase article, Vector2I tile)
    {
        var map = BattleFieldScene.BattleField.BattleFieldTileMap;
        article.TilePosition = tile;
        article.GlobalPosition = map.ToGlobal(map.MapToLocal(tile));
    }

    private static int GetHealth(ArticleBase article)
        => article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Health), out var element)
           && element is Health health ? health.CurrentHealth : 0;

    private static void SetHealth(ArticleBase article, int value)
    {
        if (article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Health), out var element)
            && element is Health health)
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
