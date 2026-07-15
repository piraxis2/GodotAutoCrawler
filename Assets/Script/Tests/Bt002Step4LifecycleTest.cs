#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.TurnAction.Common;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BT-002 Step 4: 수명 주기, 디버그 metadata, 구조 validation.
//
// 완료 조건 매핑:
//  A  RowId가 debug tick payload에서 식별되고, debug-off일 때는 아무것도 쌓이지 않는다(비용 계약).
//  B  잘못된 택틱 구조가 실행 전 validation으로 드러난다(런타임 fail-closed와 같은 규칙).
//  C  두 유닛(같은 구조)의 latch/문맥이 섞이지 않는다.
//  D  반복 실행에서 이전 턴 상태가 남지 않는다.
//  E  확정 대상이 free되면 바인딩이 그 대상을 반환하지 않고 다른 적으로 fallback하지도 않는다(Step 3.5 이월).
public partial class Bt002Step4LifecycleTest : Node
{
    private int _failures;
    private readonly List<TestTacticTarget> _targets = new();
    private readonly List<Node> _extraNodes = new();

    public override async void _Ready()
    {
        if (!(OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")) return;

        GD.Print("[BT-002 Step4] Running lifecycle/debug/validation tests...");
        try
        {
            TestRowIdInDebugTick();
            TestDebugOffCostsNothing();
            TestTacticStructureValidation();
            TestTwoUnitsIsolated();
            TestRepeatedTurnsLeaveNoState();
            await TestDeathAndFreedTargetLifecycle();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-002 Step4] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        if (_failures == 0)
        {
            GD.Print("[BT-002 Step4] ALL PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"[BT-002 Step4] FAILED: {_failures} assertion(s)");
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task NextFrame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    // ---- [A] 최종 debug tick payload에 RowId가 실린다(실제 EndDebugTick이 만드는 dictionary) --------
    private void TestRowIdInDebugTick()
    {
        GD.Print("[A] RowId debug payload");
        var tree = new BehaviorTree { Name = "DebugTree" };
        var sel = new TacticPrioritySelector { Name = "Root" };

        sel.AddChild(MakeGateRow("skip_me", pass: false));   // 불성립 행 — 보고되면 안 된다
        var runningRow = new TacticRow { Name = "Row_run", RowId = "chosen_row" };
        runningRow.AddChild(new TestStep4Gate { Name = "Gate", Pass = true });
        var act = new TestStep4Action { Name = "Act" };
        act.SetScript(TacticResult.Running, TacticResult.ActionCompleted);
        runningRow.AddChild(act);
        sel.AddChild(runningRow);

        tree.AddChild(sel);
        AddChild(tree);

        // headless에는 EngineDebugger가 붙지 않아 StartDebugTick이 버퍼를 만들지 않는다. 버퍼만 주입해
        // debug-on 경로를 재현하고, 실제로 나가는 payload(BuildTickPayload)를 검증한다.
        tree.DebugEnabled = true;

        // tick 1: 행이 선택되고 Running으로 latch된다.
        SetTickReports(tree, new Godot.Collections.Array());
        sel.Behave(0.016, null);
        var payload1 = tree.BuildTickPayload();

        CheckEq("A.payload_built", payload1 != null, true);
        CheckEq("A.payload_has_nodes", payload1.ContainsKey("nodes"), true);
        CheckEq("A.payload_has_row_id", payload1.ContainsKey("tactic_row_id"), true);
        CheckEq("A.row_id_is_selected_row", payload1["tactic_row_id"].AsString(), "chosen_row");
        CheckEq("A.row_result_running", payload1["tactic_row_result"].AsInt32(), (int)TacticResult.Running);

        // tick 2: latch된 행을 재개해도 같은 RowId가 실린다.
        SetTickReports(tree, new Godot.Collections.Array());
        sel.Behave(0.016, null);
        var payload2 = tree.BuildTickPayload();

        CheckEq("A.resume_same_row_id", payload2["tactic_row_id"].AsString(), "chosen_row");
        CheckEq("A.resume_result_completed", payload2["tactic_row_result"].AsInt32(), (int)TacticResult.ActionCompleted);

        Teardown(tree);
    }

    // ---- [A2] debug-off 비용 계약 + 수제 BT payload에는 택틱 키가 없다 ----------------------------
    private void TestDebugOffCostsNothing()
    {
        GD.Print("[A2] debug-off 비용 계약 / 수제 BT payload");

        // debug-off: 리포트 버퍼 자체를 만들지 않고 payload도 null이다.
        {
            var tree = new BehaviorTree { Name = "NoDebugTree" };
            var sel = new TacticPrioritySelector { Name = "Root" };
            sel.AddChild(MakeGateRow("row", pass: true));
            tree.AddChild(sel);
            AddChild(tree);

            tree.DebugEnabled = false;
            tree.StartDebugTick();
            sel.Behave(0.016, null);

            CheckEq("A2.no_tick_reports", TickReports(tree) == null, true);
            CheckEq("A2.no_payload", tree.BuildTickPayload() == null, true);

            Teardown(tree);
        }

        // 수제 BT(택틱 아님): payload에 택틱 키가 실리지 않아 기존 계약이 유지된다.
        {
            var tree = new BehaviorTree { Name = "PlainTree" };
            var sel = new BehaviorTree_Selector { Name = "PlainRoot" };
            sel.AddChild(new TestStep4Action { Name = "PlainAct" });
            tree.AddChild(sel);
            AddChild(tree);

            tree.DebugEnabled = true;
            SetTickReports(tree, new Godot.Collections.Array());
            sel.Behave(0.016, null);
            var payload = tree.BuildTickPayload();

            CheckEq("A2.plain_payload_built", payload != null, true);
            CheckEq("A2.plain_has_no_row_id", payload.ContainsKey("tactic_row_id"), false);
            CheckEq("A2.plain_has_no_row_result", payload.ContainsKey("tactic_row_result"), false);

            Teardown(tree);
        }
    }

    // ---- [B] 잘못된 택틱 구조는 실행 전 validation으로 드러난다 -----------------------------------
    private void TestTacticStructureValidation()
    {
        GD.Print("[B] 택틱 구조 validation");

        // B1: selector 자식이 택틱 행이 아님.
        {
            var tree = new BehaviorTree();
            var sel = new TacticPrioritySelector { Name = "Root" };
            sel.AddChild(new BehaviorTree_Selector { Name = "NotARow" });
            tree.AddChild(sel);

            var results = BehaviorTreeValidation.ValidateTree(tree);
            CheckEq("B1.selector_invalid_child",
                results.ContainsKey(sel) && results[sel].ErrorType == BtValidationErrorType.TacticSelectorInvalidChild, true);
            CheckEq("B1.is_error", results.ContainsKey(sel) && results[sel].IsError, true);
            tree.QueueFree();
        }

        // B1b: 행에 행동이 아예 없음.
        {
            var tree = new BehaviorTree();
            var sel = new TacticPrioritySelector { Name = "Root" };
            var row = new TacticRow { Name = "EmptyRow", RowId = "empty" };
            sel.AddChild(row);
            tree.AddChild(sel);

            var results = BehaviorTreeValidation.ValidateTree(tree);
            CheckEq("B1b.row_no_action",
                results.ContainsKey(row) && results[row].ErrorType == BtValidationErrorType.TacticRowNoAction, true);
            CheckEq("B1b.is_error", results.ContainsKey(row) && results[row].IsError, true);
            tree.QueueFree();
        }

        // B2: 행의 마지막 자식이 택틱 행동이 아님.
        {
            var tree = new BehaviorTree();
            var sel = new TacticPrioritySelector { Name = "Root" };
            var row = new TacticRow { Name = "Row", RowId = "bad" };
            row.AddChild(new BehaviorTree_Selector { Name = "NotAnAction" });
            sel.AddChild(row);
            tree.AddChild(sel);

            var results = BehaviorTreeValidation.ValidateTree(tree);
            CheckEq("B2.row_invalid_action",
                results.ContainsKey(row) && results[row].ErrorType == BtValidationErrorType.TacticRowInvalidAction, true);
            tree.QueueFree();
        }

        // B3: 타깃 셀렉터가 행동 바로 앞이 아님(셀렉터 뒤에 조건 -> 교집합 우회).
        {
            var tree = new BehaviorTree();
            var sel = new TacticPrioritySelector { Name = "Root" };
            var row = new TacticRow { Name = "Row", RowId = "misplaced" };
            row.AddChild(new TacticTargetSelector { Name = "Sel" });
            row.AddChild(new TacticConditionAlways { Name = "CondAfterSelector" });
            row.AddChild(new TacticWait { Name = "Wait" });
            sel.AddChild(row);
            tree.AddChild(sel);

            var results = BehaviorTreeValidation.ValidateTree(tree);
            CheckEq("B3.selector_misplaced",
                results.ContainsKey(row) && results[row].ErrorType == BtValidationErrorType.TacticRowSelectorMisplaced, true);
            tree.QueueFree();
        }

        // B4: 행동 시퀀스에 비택틱 자식.
        {
            var tree = new BehaviorTree();
            var sel = new TacticPrioritySelector { Name = "Root" };
            var row = new TacticRow { Name = "Row", RowId = "seq" };
            var seq = new TacticActionSequence { Name = "Seq" };
            seq.AddChild(new BehaviorTree_Selector { Name = "RawInSeq" });
            row.AddChild(seq);
            sel.AddChild(row);
            tree.AddChild(sel);

            var results = BehaviorTreeValidation.ValidateTree(tree);
            CheckEq("B4.sequence_invalid_child",
                results.ContainsKey(seq) && results[seq].ErrorType == BtValidationErrorType.TacticSequenceInvalidChild, true);
            tree.QueueFree();
        }

        // B5: 올바른 구조는 오류가 없다.
        {
            var tree = new BehaviorTree();
            var sel = new TacticPrioritySelector { Name = "Root" };
            var row = new TacticRow { Name = "Row", RowId = "ok" };
            row.AddChild(new TacticConditionAlways { Name = "Always" });
            row.AddChild(new TacticTargetSelector { Name = "Sel" });
            var seq = new TacticActionSequence { Name = "Seq" };
            seq.AddChild(new TacticApproach { Name = "Approach" });
            seq.AddChild(new TacticTurnAction { Name = "Melee", TurnAction = new TurnAction_Attack() });
            row.AddChild(seq);
            sel.AddChild(row);
            var waitRow = new TacticRow { Name = "WaitRow", RowId = "wait" };
            waitRow.AddChild(new TacticWait { Name = "Wait" });
            sel.AddChild(waitRow);
            tree.AddChild(sel);

            var results = BehaviorTreeValidation.ValidateTree(tree);
            CheckEq("B5.valid_board_no_errors", results.Count, 0);
            tree.QueueFree();
        }
    }

    // ---- [C] 같은 구조의 두 유닛이 latch/문맥을 공유하지 않는다 -----------------------------------
    private void TestTwoUnitsIsolated()
    {
        GD.Print("[C] 두 유닛 격리");
        var (selA, actA) = MakeRunningBoard("A");
        var (selB, actB) = MakeRunningBoard("B");
        AddChild(selA);
        AddChild(selB);

        // A만 Running으로 latch시킨다.
        CheckEq("C.A_running", selA.Behave(0.016, null), BtStatus.Running);
        CheckEq("C.B_untouched", actB.ExecuteCount, 0);

        // B를 독립적으로 완료시킨다 — A의 latch에 영향받지 않는다.
        actB.SetScript(TacticResult.ActionCompleted);
        CheckEq("C.B_completes", selB.Behave(0.016, null), BtStatus.Success);
        CheckEq("C.B_result", selB.LastResult, TacticResult.ActionCompleted);

        // A는 여전히 자기 latch를 유지해 재개한다.
        actA.SetScript(TacticResult.ActionCompleted);
        CheckEq("C.A_resumes", selA.Behave(0.016, null), BtStatus.Success);
        CheckEq("C.A_ran_twice", actA.ExecuteCount, 2);
        CheckEq("C.B_ran_once", actB.ExecuteCount, 1);

        Teardown(selA);
        Teardown(selB);
    }

    // ---- [D] 반복 실행: 이전 턴 latch/문맥이 남지 않는다 -----------------------------------------
    private void TestRepeatedTurnsLeaveNoState()
    {
        GD.Print("[D] 반복 턴 잔여 상태 없음");
        var (sel, act) = MakeRunningBoard("R");
        AddChild(sel);

        for (int turn = 0; turn < 3; turn++)
        {
            sel.ResetForNewTurn();                       // 실제 턴 경계
            CheckEq($"D.turn{turn}_reset", sel.LastResult, TacticResult.Ineligible);

            act.SetScript(TacticResult.Running, TacticResult.ActionCompleted);
            CheckEq($"D.turn{turn}_running", sel.Behave(0.016, null), BtStatus.Running);
            CheckEq($"D.turn{turn}_completes", sel.Behave(0.016, null), BtStatus.Success);
        }

        // 3턴 x (Running + Completed) = 6회 실행. 이전 턴 상태가 남았다면 횟수가 어긋난다.
        CheckEq("D.execute_count_exact", act.ExecuteCount, 6);

        Teardown(sel);
    }

    // ---- [E] 실제 사망(OnDead)으로 latch/문맥/바인딩/이동 tween이 정리된다 -------------------------
    //       [F] 두 전투 격리: 전투1에서 만든 상태가 전투2로 새지 않는다(Matrix #12).
    //       두 보드가 **같은 TurnAction 리소스를 공유**하게 해, 리소스에 남은 바인딩이 새는지까지 본다.
    private async System.Threading.Tasks.Task TestDeathAndFreedTargetLifecycle()
    {
        GD.Print("[E/F] 실제 사망·teardown·두 전투 격리");

        var sharedAttack = new TurnAction_Attack();   // 전투1·2가 공유하는 행동 리소스

        // ===== 전투 1: 접근 중 사망 =====
        var battle1 = await LoadBattle();
        var (ally1, enemy1) = FindCombatants();
        if (ally1 == null || enemy1 == null) { Fail("E.combatants_found"); return; }

        Rect2I region = BattleFieldScene.BattleField.BattleFieldTileMap.GetUsedRect();
        int y = region.Position.Y + region.Size.Y / 2;
        int x0 = region.Position.X + 1;

        var (root1, row1, action1) = BuildMeleeBoard(ally1, sharedAttack);
        await NextFrame();

        // 적을 멀리 둬서 이 턴은 "접근 중(Running)"에 머물게 한다.
        Park(enemy1, new Vector2I(x0 + 5, y));
        Park(ally1, new Vector2I(x0, y));
        ally1.ApplyTurnStartEffects();

        BtStatus st = ally1.TurnPlay(0.1);   // 접근 시작 -> Running, latch
        CheckEq("E.approach_running", st, BtStatus.Running);
        CheckEq("E.latched", root1.LastResult, TacticResult.Running);
        CheckEq("E.context_target_set", RowContext(row1)?.ConfirmedTarget != null, true);
        CheckEq("E.move_tween_alive", StepTween(ally1) != null, true);

        // 실제 사망: HP 0 -> ArticleBase.Dead() -> OnDead -> ResetTacticRuntime()
        Kill(ally1);
        CheckEq("E.ally_dead", ally1.IsAlive, false);

        CheckEq("E.latch_cleared_on_death", root1.LastResult, TacticResult.Ineligible);
        CheckEq("E.context_cleared_on_death", RowContext(row1)?.ConfirmedTarget == null, true);
        CheckEq("E.binding_cleared_on_death", sharedAttack.IsExplicitTargetBound, false);
        CheckEq("E.action_cleared_on_death", ally1.CurrentTurnAction == null, true);
        CheckEq("E.state_cleared_on_death", ally1.CurrentTurnActionState == null, true);
        CheckEq("E.move_tween_killed_on_death", StepTween(ally1) == null, true);

        // 바인딩된 대상이 free되면 fallback 없이 null(Step 3.5 이월).
        var probe = new BoundTargetProbe { Action = sharedAttack };
        AddChild(probe);
        sharedAttack.BindExplicitTarget(enemy1);
        CheckEq("E.bound_returns_target", ReferenceEquals(probe.Resolve(), enemy1), true);
        enemy1.QueueFree();
        await NextFrame();
        CheckEq("E.enemy_freed", GodotObject.IsInstanceValid(enemy1), false);
        CheckEq("E.still_bound_after_free", sharedAttack.IsExplicitTargetBound, true);
        CheckEq("E.freed_target_not_returned", probe.Resolve() == null, true);
        RemoveChild(probe);
        probe.QueueFree();

        // 전투 1 teardown.
        await DrainBattleFx();
        RemoveChild(battle1);
        battle1.QueueFree();
        await NextFrame();
        CheckEq("E.battle1_freed", GodotObject.IsInstanceValid(battle1), false);

        // tree exit에서 공유 리소스의 바인딩도 풀려 있어야 다음 전투로 새지 않는다.
        CheckEq("F.binding_not_leaked_after_teardown", sharedAttack.IsExplicitTargetBound, false);

        // ===== 전투 2: 완전히 새 전장에서 독립 실행 =====
        var battle2 = await LoadBattle();
        var (ally2, enemy2) = FindCombatants();
        if (ally2 == null || enemy2 == null) { Fail("F.combatants_found"); return; }

        CheckEq("F.new_units_are_distinct", ReferenceEquals(ally2, ally1), false);

        var (root2, row2, action2) = BuildMeleeBoard(ally2, sharedAttack);
        await NextFrame();

        // 새 보드는 초기 상태다(전투 1의 latch/문맥이 없다).
        CheckEq("F.fresh_latch", root2.LastResult, TacticResult.Ineligible);
        CheckEq("F.fresh_context", RowContext(row2)?.ConfirmedTarget == null, true);

        Park(enemy2, new Vector2I(x0 + 1, y));   // 근접 사거리
        Park(ally2, new Vector2I(x0, y));
        int hpBefore = GetHealth(enemy2);

        ally2.ApplyTurnStartEffects();
        BtStatus last = DriveTurn(ally2);

        CheckEq("F.battle2_turn_completed", last, BtStatus.Success);
        CheckEq("F.battle2_hit_its_own_enemy", GetHealth(enemy2) < hpBefore, true);
        // 공유 리소스가 전투 2의 적에 바인딩됐다(전투 1의 죽은 적이 아니다).
        CheckEq("F.bound_to_battle2_enemy",
            ReferenceEquals(RowContext(row2)?.ConfirmedTarget, enemy2), true);

        await DrainBattleFx();
        RemoveChild(battle2);
        battle2.QueueFree();
        await NextFrame();
        CheckEq("F.battle2_freed", GodotObject.IsInstanceValid(battle2), false);
    }

    // 전투 연출(`damage_floater.gd`의 SceneTree tween, ~0.5초)을 모두 띄우고 끝날 때까지 실제 시간을 준다.
    // FxPlayer를 먼저 비워야 마지막에 생성된 floater가 미완료로 남지 않는다(TurnHelper를 끄고 직접 구동하므로
    // 스케줄러가 하던 Tick을 테스트가 대신한다).
    private async System.Threading.Tasks.Task DrainBattleFx()
    {
        for (int i = 0; i < 10; i++)
        {
            BattleFieldScene.BattleField?.FxPlayer?.Tick(0.1);
            await NextFrame();
        }
        await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
    }

    // ---- 전장/보드 헬퍼 ---------------------------------------------------------------------------

    private async System.Threading.Tasks.Task<BattleFieldScene> LoadBattle()
    {
        var packed = GD.Load<PackedScene>("res://Assets/Scenes/Map/battle_field.tscn");
        var battle = packed.Instantiate<BattleFieldScene>();
        var turnHelper = battle.GetNodeOrNull<TurnHelper>("TurnHelper");
        if (turnHelper != null) turnHelper.AutoStart = false;
        AddChild(battle);
        await NextFrame();
        return battle;
    }

    private static (CharacterArticle ally, CharacterArticle enemy) FindCombatants()
    {
        var container = BattleFieldScene.BattleField?.Articles;
        if (container == null) return (null, null);
        CharacterArticle ally = container.Articles["Ally"].Count > 0
            ? container.Articles["Ally"][0] as CharacterArticle : null;
        CharacterArticle enemy = container.Articles["Opponent"].Count > 0
            ? container.Articles["Opponent"][0] as CharacterArticle : null;
        return (ally, enemy);
    }

    // [적이 있음 -> 셀렉터 -> 시퀀스[접근, 근접행동]], [대기]
    private static (TacticPrioritySelector root, TacticRow row, TacticTurnAction action) BuildMeleeBoard(
        CharacterArticle ally, TurnAction_Attack sharedAttack)
    {
        var bt = ally.GetNode<BehaviorTree>("BehaviorTree");
        foreach (Node child in bt.GetChildren())
        {
            bt.RemoveChild(child);
            child.QueueFree();
        }

        var root = new TacticPrioritySelector { Name = "TacticRoot" };
        var row = new TacticRow { Name = "RowAttack", RowId = "attack" };
        row.AddChild(new TacticConditionAnyEnemy { Name = "AnyEnemy" });
        row.AddChild(new TacticTargetSelector { Name = "Nearest", Policy = TacticApproachPolicy.ApproachAllowed });
        var seq = new TacticActionSequence { Name = "Seq" };
        seq.AddChild(new TacticApproach { Name = "Approach" });
        var action = new TacticTurnAction { Name = "Melee", TurnAction = sharedAttack };
        seq.AddChild(action);
        row.AddChild(seq);
        root.AddChild(row);

        var waitRow = new TacticRow { Name = "RowWait", RowId = "wait" };
        waitRow.AddChild(new TacticWait { Name = "Wait" });
        root.AddChild(waitRow);

        bt.AddChild(root);
        bt.UpdateRequest();
        return (root, row, action);
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

    // HP를 0으로 만들어 실제 사망 경로(Health -> ArticleBase.Dead() -> OnDead)를 태운다.
    private static void Kill(CharacterArticle article)
    {
        if (article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Health), out var element)
            && element is Health health)
        {
            health.CurrentHealth = 0;
        }
    }

    private static int GetHealth(ArticleBase article)
        => article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Health), out var h) && h is Health health
            ? health.CurrentHealth : 0;

    // 행의 유닛별 문맥(확정 대상 확인용).
    private static TacticRowContext RowContext(TacticRow row)
    {
        var field = typeof(TacticRow).GetField("_context",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (TacticRowContext)field.GetValue(row);
    }

    // 어댑터가 소유 중인 이동 tween(사망 시 정리되는지 확인용).
    private static Tween StepTween(CharacterArticle article)
    {
        var adapter = article.TacticAdapter;
        var field = typeof(CharacterTacticAdapter).GetField("_stepTween",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Tween)field.GetValue(adapter);
    }

    private void Fail(string name)
    {
        _failures++;
        GD.Print($"  FAIL: {name}");
    }

    // ---- 빌드 헬퍼 -------------------------------------------------------------------------------

    private static TacticRow MakeGateRow(string rowId, bool pass)
    {
        var row = new TacticRow { Name = $"Row_{rowId}", RowId = rowId };
        row.AddChild(new TestStep4Gate { Name = $"Gate_{rowId}", Pass = pass });
        row.AddChild(new TacticWait { Name = $"Wait_{rowId}" });
        return row;
    }

    // [조건(통과), 시퀀스[스크립트 행동]] 한 행짜리 보드.
    private static (TacticPrioritySelector sel, TestStep4Action act) MakeRunningBoard(string id)
    {
        var sel = new TacticPrioritySelector { Name = $"Sel_{id}" };
        var row = new TacticRow { Name = $"Row_{id}", RowId = id };
        row.AddChild(new TestStep4Gate { Name = $"Gate_{id}", Pass = true });
        var act = new TestStep4Action { Name = $"Act_{id}" };
        act.SetScript(TacticResult.Running);
        row.AddChild(act);
        sel.AddChild(row);
        return (sel, act);
    }

    private static void SetTickReports(BehaviorTree tree, Godot.Collections.Array reports)
    {
        var field = typeof(BehaviorTree).GetField("_tickReports",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.SetValue(tree, reports);
    }

    private static Godot.Collections.Array TickReports(BehaviorTree tree)
    {
        var field = typeof(BehaviorTree).GetField("_tickReports",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Godot.Collections.Array)field.GetValue(tree);
    }

    private static string RowId(BehaviorTree tree)
    {
        var field = typeof(BehaviorTree).GetField("_tickRowId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (string)field.GetValue(tree);
    }

    private static int RowResult(BehaviorTree tree)
    {
        var field = typeof(BehaviorTree).GetField("_tickRowResult",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (int)field.GetValue(tree);
    }

    private void Teardown(Node subtree)
    {
        RemoveChild(subtree);
        subtree.QueueFree();
        foreach (var t in _targets)
            if (GodotObject.IsInstanceValid(t)) t.Free();
        _targets.Clear();
        foreach (var n in _extraNodes)
            if (GodotObject.IsInstanceValid(n)) n.Free();
        _extraNodes.Clear();
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

// ---- test 대역 (제품 코드 아님, #if TOOLS) ------------------------------------------------------

public partial class TestStep4Gate : TacticCondition
{
    public bool Pass { get; set; } = true;

    protected override BtStatus PerformAction(double delta, Node owner)
        => Pass ? BtStatus.Success : BtStatus.Failure;
}

// TacticResult를 스크립트대로 내는 행동 대역(어댑터 없이 동작하도록 TacticExecuteAction을 쓰지 않는다).
public partial class TestStep4Action : BehaviorTree_Action, ITacticNode
{
    private readonly Queue<TacticResult> _script = new();
    private TacticResult _last = TacticResult.ActionCompleted;

    public int ExecuteCount { get; private set; }
    public TacticResult LastResult => _last;

    public void SetScript(params TacticResult[] results)
    {
        _script.Clear();
        foreach (var r in results) _script.Enqueue(r);
    }

    public void ResetForNewTurn() => _last = TacticResult.Ineligible;

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        ExecuteCount++;
        _last = _script.Count > 0 ? _script.Dequeue() : TacticResult.ActionCompleted;
        return _last.ToBtStatus();
    }
}

// TurnActionBase의 protected BoundTarget을 관찰하기 위한 얇은 probe.
public partial class BoundTargetProbe : BehaviorTree_Action
{
    public TurnAction_Attack Action { get; set; }

    public ArticleBase Resolve()
    {
        var prop = typeof(AutoCrawler.Assets.Script.TurnAction.TurnActionBase)
            .GetProperty("BoundTarget",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (ArticleBase)prop.GetValue(Action);
    }

    protected override BtStatus PerformAction(double delta, Node owner) => BtStatus.Success;
}
#endif
