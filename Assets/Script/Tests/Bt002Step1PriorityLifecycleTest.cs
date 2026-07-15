#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BT-002 Step 1: TacticPrioritySelector / TacticRow 상태 기계 headless 검증.
// test condition/action 대역만 쓰며 실제 대상 검색·이동·SkillSystem은 다루지 않는다(Step 1 제외 범위).
//
// 완료 조건 매핑:
//  A/B  첫 행 성립/불성립과 두 번째 행 fallback
//  C    Running 중 상위 조건이 바뀌어도 선택 행 유지(preempt 금지)
//  D    commit 후 취소는 다음 행 미실행
//  G    commit 전(Ineligible) 행동 실패는 다음 행으로 fallback
//  E    다음 턴에는 첫 행부터 재평가
//  F    두 tree instance 상태 격리
public partial class Bt002Step1PriorityLifecycleTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0)
            {
                GD.Print("[BT-002 Step1] ALL PASS");
                GetTree().Quit(0);
            }
            else
            {
                GD.Print($"[BT-002 Step1] FAILED: {failures} assertion(s)");
                GetTree().Quit(1);
            }
        }
    }

    public int RunTests()
    {
        GD.Print("[BT-002 Step1] Running priority/lifecycle tests...");
        try
        {
            TestFirstEligibleRowRuns();
            TestFallbackToSecondRow();
            TestRunningRowIsNotPreempted();
            TestCancelledAfterCommitEndsTurn();
            TestPreCommitIneligibleFallsThrough();
            TestNextTurnReevaluatesFromTop();
            TestInstancesAreIsolated();
            TestExplicitResetReevaluatesFromTop();
            TestSelectorSkipsNonTacticChild();
            TestRowRejectsNonTacticAction();
            TestRunningConditionFailsClosed();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-002 Step1] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        return _failures;
    }

    // ---- [A] 첫 행 성립 -> 첫 행 실행 --------------------------------------------------------------
    private void TestFirstEligibleRowRuns()
    {
        GD.Print("[A] 첫 성립 행 실행");
        var (r0, _, a0) = MakeRow("0", eligible: true, TacticResult.ActionCompleted);
        var (r1, _, a1) = MakeRow("1", eligible: true, TacticResult.ActionCompleted);
        var sel = MakeSelector(r0, r1);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, null);

        CheckEq("A.status_success", status, BtStatus.Success);
        CheckEq("A.result_completed", sel.LastResult, TacticResult.ActionCompleted);
        CheckEq("A.row0_ran", a0.ExecuteCount, 1);
        CheckEq("A.row1_untouched", a1.ExecuteCount, 0);

        Teardown(sel);
    }

    // ---- [B] 첫 행 불성립 -> 두 번째 행 실행 -------------------------------------------------------
    private void TestFallbackToSecondRow()
    {
        GD.Print("[B] 불성립 fallback");
        var (r0, _, a0) = MakeRow("0", eligible: false, TacticResult.ActionCompleted);
        var (r1, _, a1) = MakeRow("1", eligible: true, TacticResult.ActionCompleted);
        var sel = MakeSelector(r0, r1);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, null);

        CheckEq("B.status_success", status, BtStatus.Success);
        CheckEq("B.row0_gated_no_action", a0.ExecuteCount, 0);
        CheckEq("B.row1_ran", a1.ExecuteCount, 1);

        Teardown(sel);
    }

    // ---- [C] Running 중 상위 조건 변화 -> 선택 행 유지(preempt 금지) ------------------------------
    private void TestRunningRowIsNotPreempted()
    {
        GD.Print("[C] Running 행 고정");
        var (r0, c0, a0) = MakeRow("0", eligible: false, TacticResult.ActionCompleted);
        var (r1, _, a1) = MakeRow("1", eligible: true, TacticResult.Running, TacticResult.ActionCompleted);
        var sel = MakeSelector(r0, r1);
        AddChild(sel);

        BtStatus t1 = sel.Behave(0.016, null);
        CheckEq("C.t1_running", t1, BtStatus.Running);
        CheckEq("C.t1_result_running", sel.LastResult, TacticResult.Running);
        CheckEq("C.t1_row0_untouched", a0.ExecuteCount, 0);

        // 실행 중 상위 행 조건을 성립시켜도 preempt하면 안 된다.
        c0.Eligible = true;

        BtStatus t2 = sel.Behave(0.016, null);
        CheckEq("C.t2_success", t2, BtStatus.Success);
        CheckEq("C.t2_result_completed", sel.LastResult, TacticResult.ActionCompleted);
        CheckEq("C.t2_row0_still_untouched", a0.ExecuteCount, 0);
        CheckEq("C.row1_resumed_twice", a1.ExecuteCount, 2);

        Teardown(sel);
    }

    // ---- [D] commit 후 취소 -> 턴 종료, 다음 행 미실행 --------------------------------------------
    private void TestCancelledAfterCommitEndsTurn()
    {
        GD.Print("[D] commit 후 취소");
        var (r0, _, a0) = MakeRow("0", eligible: true, TacticResult.Running, TacticResult.CancelledAfterCommit);
        var (r1, _, a1) = MakeRow("1", eligible: true, TacticResult.ActionCompleted);
        var sel = MakeSelector(r0, r1);
        AddChild(sel);

        BtStatus t1 = sel.Behave(0.016, null);
        CheckEq("D.t1_running", t1, BtStatus.Running);

        BtStatus t2 = sel.Behave(0.016, null);
        CheckEq("D.t2_success", t2, BtStatus.Success);
        CheckEq("D.t2_result_cancelled", sel.LastResult, TacticResult.CancelledAfterCommit);
        CheckEq("D.row0_ran_twice", a0.ExecuteCount, 2);
        CheckEq("D.row1_never_ran", a1.ExecuteCount, 0);

        Teardown(sel);
    }

    // ---- [G] commit 전 Ineligible 행동 실패 -> 다음 행으로 fallback -------------------------------
    private void TestPreCommitIneligibleFallsThrough()
    {
        GD.Print("[G] commit 전 실패 fallback");
        // 조건은 통과하지만 행동이 시작 자체를 못 함(pre-commit) -> Ineligible -> 다음 행.
        var (r0, _, a0) = MakeRow("0", eligible: true, TacticResult.Ineligible);
        var (r1, _, a1) = MakeRow("1", eligible: true, TacticResult.ActionCompleted);
        var sel = MakeSelector(r0, r1);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, null);

        CheckEq("G.status_success", status, BtStatus.Success);
        CheckEq("G.row0_action_tried", a0.ExecuteCount, 1);
        CheckEq("G.row1_ran", a1.ExecuteCount, 1);
        CheckEq("G.result_completed", sel.LastResult, TacticResult.ActionCompleted);

        Teardown(sel);
    }

    // ---- [E] 다음 턴 -> 첫 행부터 재평가 ----------------------------------------------------------
    private void TestNextTurnReevaluatesFromTop()
    {
        GD.Print("[E] 다음 턴 재평가");
        var (r0, c0, a0) = MakeRow("0", eligible: true, TacticResult.ActionCompleted, TacticResult.ActionCompleted);
        var (r1, _, a1) = MakeRow("1", eligible: true, TacticResult.ActionCompleted);
        var sel = MakeSelector(r0, r1);
        AddChild(sel);

        // 턴 1: 첫 행이 성립·완료.
        BtStatus turn1 = sel.Behave(0.016, null);
        CheckEq("E.turn1_success", turn1, BtStatus.Success);
        CheckEq("E.turn1_row0_ran", a0.ExecuteCount, 1);
        CheckEq("E.turn1_row1_untouched", a1.ExecuteCount, 0);

        // 다음 턴 전에 첫 행을 불성립으로 바꾼다.
        c0.Eligible = false;

        // 턴 2: latch 없이 첫 행부터 재평가 -> 첫 행 불성립 -> 두 번째 행.
        BtStatus turn2 = sel.Behave(0.016, null);
        CheckEq("E.turn2_success", turn2, BtStatus.Success);
        CheckEq("E.turn2_row0_gated", a0.ExecuteCount, 1); // 턴1의 1회에서 증가 없음
        CheckEq("E.turn2_row1_ran", a1.ExecuteCount, 1);
        CheckEq("E.turn2_result_completed", sel.LastResult, TacticResult.ActionCompleted);

        Teardown(sel);
    }

    // ---- [F] 두 tree instance 상태 격리 -----------------------------------------------------------
    private void TestInstancesAreIsolated()
    {
        GD.Print("[F] 인스턴스 격리");
        // A: 첫 행 불성립, 둘째 행이 Running으로 latch된다.
        var (a_r0, _, a_a0) = MakeRow("A0", eligible: false, TacticResult.ActionCompleted);
        var (a_r1, _, a_a1) = MakeRow("A1", eligible: true, TacticResult.Running, TacticResult.ActionCompleted);
        var selA = MakeSelector(a_r0, a_r1);

        // B: 첫 행이 즉시 완료된다(latch 없음).
        var (b_r0, b_c0, b_a0) = MakeRow("B0", eligible: true, TacticResult.ActionCompleted);
        var (b_r1, _, b_a1) = MakeRow("B1", eligible: true, TacticResult.ActionCompleted);
        var selB = MakeSelector(b_r0, b_r1);

        AddChild(selA);
        AddChild(selB);

        // A는 Running으로 latch, B는 즉시 완료. 서로의 latch/상태에 영향 없음.
        CheckEq("F.A_t1_running", selA.Behave(0.016, null), BtStatus.Running);
        CheckEq("F.B_t1_success", selB.Behave(0.016, null), BtStatus.Success);
        CheckEq("F.A_result_running", selA.LastResult, TacticResult.Running);
        CheckEq("F.B_result_completed", selB.LastResult, TacticResult.ActionCompleted);
        CheckEq("F.B_picked_row0", b_a0.ExecuteCount, 1);
        CheckEq("F.B_row1_untouched", b_a1.ExecuteCount, 0);

        // A의 latch가 진행되는 동안 B를 새 턴으로 다시 돌려도 B는 자신의 상태로만 평가한다.
        b_c0.Eligible = false; // B는 이제 첫 행 불성립
        CheckEq("F.B_t2_success", selB.Behave(0.016, null), BtStatus.Success);
        CheckEq("F.B_t2_row1_ran", b_a1.ExecuteCount, 1); // B는 자기 latch가 없어 첫 행부터 재평가

        // A는 여전히 자기 latch를 유지해 둘째 행을 재개·완료한다.
        CheckEq("F.A_t2_success", selA.Behave(0.016, null), BtStatus.Success);
        CheckEq("F.A_row1_resumed_twice", a_a1.ExecuteCount, 2);
        CheckEq("F.A_row0_never_ran", a_a0.ExecuteCount, 0);

        Teardown(selA);
        Teardown(selB);
    }

    // ---- [H] 명시적 ResetForNewTurn -> Running 이후에도 첫 행부터 재평가 --------------------------
    // 멀티턴 action이 CurrentTurnAction으로 BT tick을 우회하다 외부에서 종료되면, 다음 자기 턴에도 base status가
    // Running이라 OnInit이 호출되지 않는다. 실제 턴 시작 배선(Step 3)은 ResetForNewTurn()을 명시 호출한다.
    // 그 seam이 OnInit과 무관하게 latch를 풀고 첫 행부터 재평가하는지 고정한다.
    private void TestExplicitResetReevaluatesFromTop()
    {
        GD.Print("[H] 명시적 reset 후 재평가");
        var (r0, c0, a0) = MakeRow("0", eligible: true, TacticResult.Running, TacticResult.Running, TacticResult.ActionCompleted);
        var (r1, _, a1) = MakeRow("1", eligible: true, TacticResult.ActionCompleted);
        var sel = MakeSelector(r0, r1);
        AddChild(sel);

        // Tick1: 첫 행이 Running으로 latch된다(base status는 이후 Running으로 남는다).
        CheckEq("H.t1_running", sel.Behave(0.016, null), BtStatus.Running);
        CheckEq("H.t1_row0_ran_once", a0.ExecuteCount, 1);

        // 새 자기 턴이 시작됐다고 명시적으로 reset. 다음 턴엔 첫 행을 불성립으로 바꾼다.
        c0.Eligible = false;
        sel.ResetForNewTurn();

        // Tick2: base status가 Running이라 OnInit은 호출되지 않지만, 명시 reset으로 latch가 풀려 첫 행부터
        // 재평가한다. 첫 행은 불성립 -> 두 번째 행 실행. 스테일 latch였다면 a0가 재개돼 ExecuteCount가 2가 된다.
        CheckEq("H.t2_success", sel.Behave(0.016, null), BtStatus.Success);
        CheckEq("H.t2_row0_not_resumed", a0.ExecuteCount, 1);
        CheckEq("H.t2_row1_ran", a1.ExecuteCount, 1);

        Teardown(sel);
    }

    // ---- [I] Selector는 비택틱 자식을 실행하지 않는다(fail-closed) ---------------------------------
    private void TestSelectorSkipsNonTacticChild()
    {
        GD.Print("[I] 비택틱 자식 skip (오류 로그 예상)");
        var rawRow = new TestRawAction { Name = "RawRow", Outcome = BtStatus.Success };
        var (r1, _, a1) = MakeRow("1", eligible: true, TacticResult.ActionCompleted);
        var sel = new TacticPrioritySelector { Name = "SelI" };
        sel.AddChild(rawRow);   // index 0: 비택틱 자식 -> 실행 금지
        sel.AddChild(r1);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, null);

        CheckEq("I.raw_child_not_executed", rawRow.ExecuteCount, 0);
        CheckEq("I.valid_row_ran", a1.ExecuteCount, 1);
        CheckEq("I.status_success", status, BtStatus.Success);

        Teardown(sel);
    }

    // ---- [J] Row는 비택틱 행동을 실행하지 않는다(fail-closed) --------------------------------------
    private void TestRowRejectsNonTacticAction()
    {
        GD.Print("[J] 비택틱 행동 거부 (오류 로그 예상)");
        var row = new TacticRow { Name = "RowJ", RowId = "J" };
        var cond = new TestTacticCondition { Name = "CondJ", Eligible = true };
        var rawAction = new TestRawAction { Name = "RawActJ", Outcome = BtStatus.Success };
        row.AddChild(cond);
        row.AddChild(rawAction);   // 마지막 자식이 비택틱 -> 잘못된 구조
        var sel = MakeSelector(row);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, null);

        CheckEq("J.raw_action_not_executed", rawAction.ExecuteCount, 0);
        CheckEq("J.status_failure", status, BtStatus.Failure);

        Teardown(sel);
    }

    // ---- [K] Running 조건은 fail-closed되어 행동을 실행하지 않는다 ---------------------------------
    private void TestRunningConditionFailsClosed()
    {
        GD.Print("[K] Running 조건 fail-closed (오류 로그 예상)");
        var row = new TacticRow { Name = "RowK", RowId = "K" };
        var runningCond = new TestRawAction { Name = "RunCondK", Outcome = BtStatus.Running }; // gate가 Running(잘못된 구조)
        var action = new TestTacticAction { Name = "ActK" };
        action.SetScript(TacticResult.ActionCompleted);
        row.AddChild(runningCond);   // 조건(마지막 아님)
        row.AddChild(action);        // 행동(마지막, 유효 택틱)
        var sel = MakeSelector(row);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, null);

        CheckEq("K.running_cond_evaluated", runningCond.ExecuteCount, 1);
        CheckEq("K.action_not_executed", action.ExecuteCount, 0);
        CheckEq("K.status_failure", status, BtStatus.Failure);

        Teardown(sel);
    }

    // ---- 빌드 헬퍼 -------------------------------------------------------------------------------

    // 행 하나를 조립한다. 자식 순서 [조건, 행동] -> TacticRow는 마지막 자식을 행동으로 본다.
    private static (TacticRow row, TestTacticCondition cond, TestTacticAction action) MakeRow(
        string id, bool eligible, params TacticResult[] script)
    {
        var row = new TacticRow { Name = $"Row_{id}", RowId = id };
        var cond = new TestTacticCondition { Name = $"Cond_{id}", Eligible = eligible };
        var action = new TestTacticAction { Name = $"Act_{id}" };
        action.SetScript(script);
        row.AddChild(cond);
        row.AddChild(action);
        return (row, cond, action);
    }

    private static TacticPrioritySelector MakeSelector(params TacticRow[] rows)
    {
        var sel = new TacticPrioritySelector { Name = "TacticSelector" };
        foreach (var row in rows) sel.AddChild(row);
        return sel;
    }

    // subtree를 SceneTree에 넣어 각 노드의 _Ready(-> OnTreeChanged)로 composite 자식 목록을 채운다.
    private void Teardown(Node subtree)
    {
        RemoveChild(subtree);
        subtree.QueueFree();
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

// 성립/불성립을 직접 제어하는 조건 게이트. Eligible=false면 행을 불성립시킨다(후보를 만들지 않는 gate).
public partial class TestTacticCondition : BehaviorTree_Action
{
    public bool Eligible { get; set; } = true;

    protected override BtStatus PerformAction(double delta, Node owner)
        => Eligible ? BtStatus.Success : BtStatus.Failure;
}

// TacticResult 시퀀스를 재생하는 행동 대역. 큐가 비면 마지막 기본값(ActionCompleted)을 반환한다.
// ExecuteCount로 실제 실행 횟수를 관찰해 fallback/latch/격리를 단언한다.
public partial class TestTacticAction : BehaviorTree_Action, ITacticNode
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

    public void ResetForNewTurn()
    {
        // 대역은 스크립트/카운트를 유지한다(테스트가 턴 경계 동작을 관찰하기 위함). 계약상 LastResult만 초기화.
        _last = TacticResult.Ineligible;
    }

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        ExecuteCount++;
        _last = _script.Count > 0 ? _script.Dequeue() : TacticResult.ActionCompleted;
        return _last.ToBtStatus();
    }
}

// 비택틱(ITacticNode 아님) BT 노드 대역. fail-closed 검증에서 "raw 노드는 호출되지 않음"을 관찰하고,
// gate 자리에 두면 Running 조건 같은 잘못된 구조도 재현한다.
public partial class TestRawAction : BehaviorTree_Action
{
    public int ExecuteCount { get; private set; }
    public BtStatus Outcome { get; set; } = BtStatus.Success;

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        ExecuteCount++;
        return Outcome;
    }
}
#endif
