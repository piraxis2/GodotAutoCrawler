#if TOOLS
using System;
using System.Collections.Generic;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BT-002 Step 3: 접근 정책·행동·대기. 실제 이동/사거리는 승인된 test 어댑터 대역으로 통합 검증한다(Task 승인).
//
// 완료 조건 매핑:
//  S    Immediate 먼 대상 -> 접근 없이 다음 행(wait), 성립 검사는 이동/지불 없음(mutation-free)
//  T    Immediate 이동 후 사거리 내 -> 같은 대상 같은 턴 행동, 실제 지불 1회
//  U    ApproachAllowed 미도달 -> 접근으로 턴 종료, 다음 행 미실행
//  V    이동 Running 중 상위 행 성립해도 preempt 없음(latch)
//  V2   새 턴 reset이 진행 중 접근을 취소(이동 tween 누수 방지 계약)
public partial class Bt002Step3ApproachActionTest : Node
{
    private int _failures;
    private readonly List<TestTacticTarget> _targets = new();
    private readonly List<Node> _extraNodes = new();

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0)
            {
                GD.Print("[BT-002 Step3] ALL PASS");
                GetTree().Quit(0);
            }
            else
            {
                GD.Print($"[BT-002 Step3] FAILED: {failures} assertion(s)");
                GetTree().Quit(1);
            }
        }
    }

    public int RunTests()
    {
        GD.Print("[BT-002 Step3] Running approach/action/wait tests...");
        try
        {
            TestImmediateTooFarSkipsToWait();
            TestImmediateReachMovesThenActs();
            TestApproachAllowedConsumesTurn();
            TestApproachRunningNotPreempted();
            TestResetCancelsApproach();
            TestPreCommitActionFailureFallsThrough();
            TestRunningActionKeepsTurn();
            TestCancelledAfterCommitEndsTurn();
            TestSequenceRejectsNonTacticChild();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-002 Step3] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        return _failures;
    }

    // ---- [S] Immediate 먼 대상 -> 접근 없이 다음 행(wait) + 성립 검사 mutation 없음 --------------
    private void TestImmediateTooFarSkipsToWait()
    {
        GD.Print("[S] Immediate 먼 대상 -> wait");
        var adapter = new TestTacticActionAdapter();
        var owner = MakeOwner(adapter, new Vector2I(0, 0));

        var far = MakeTarget(new Vector2I(9, 0));
        adapter.HasPath.Add(far);   // 경로는 있으나 Immediate는 이번 턴 행동 불가 -> 불성립

        var cond = new TestTargetCondition { Name = "CondS" };
        cond.SetCandidates(far);
        var (attackRow, _, _) = MakeAttackRow("S", TacticApproachPolicy.Immediate, cond, adapter);
        var waitRow = MakeWaitRow("Swait");
        var sel = MakeSelector(attackRow, waitRow);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, owner);

        CheckEq("S.status_success", status, BtStatus.Success);
        CheckEq("S.result_wait_completed", sel.LastResult, TacticResult.ActionCompleted);
        CheckEq("S.no_approach", adapter.ApproachCalls, 0);              // 먼 영창 적에게 접근하지 않음
        CheckEq("S.no_attack_payment", adapter.ExecuteAttackCalls, 0);  // 성립 검사에서 비용 지불 없음
        CheckEq("S.feasibility_checked", adapter.FeasibilityCalls > 0, true);

        Teardown(sel, owner);
    }

    // ---- [T] Immediate 이동 후 사거리 내 -> 같은 대상 같은 턴 행동, 지불 1회 -------------------------
    private void TestImmediateReachMovesThenActs()
    {
        GD.Print("[T] Immediate 이동 후 행동");
        var adapter = new TestTacticActionAdapter();
        var owner = MakeOwner(adapter, new Vector2I(0, 0));

        var t = MakeTarget(new Vector2I(2, 0));
        adapter.ReachThisTurn.Add(t);            // 이번 턴 이동(Mobility+1)으로 사거리 진입 가능
        adapter.ApproachScript.Enqueue(ApproachStep.Arrived);

        var cond = new TestTargetCondition { Name = "CondT" };
        cond.SetCandidates(t);
        var (attackRow, _, _) = MakeAttackRow("T", TacticApproachPolicy.Immediate, cond, adapter);
        var sel = MakeSelector(attackRow);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, owner);

        CheckEq("T.status_success", status, BtStatus.Success);
        CheckEq("T.result_completed", sel.LastResult, TacticResult.ActionCompleted);
        CheckEq("T.moved_once", adapter.ApproachCalls, 1);
        CheckEq("T.attacked_once", adapter.ExecuteAttackCalls, 1);   // 실제 실행에서 한 번만 지불
        CheckEq("T.no_cancel", adapter.CancelCalls, 0);

        Teardown(sel, owner);
    }

    // ---- [U] ApproachAllowed 미도달 -> 접근으로 턴 종료, 다음 행 미실행 ----------------------------
    private void TestApproachAllowedConsumesTurn()
    {
        GD.Print("[U] ApproachAllowed 접근 소비");
        var adapter = new TestTacticActionAdapter();
        var owner = MakeOwner(adapter, new Vector2I(0, 0));

        var t = MakeTarget(new Vector2I(6, 0));
        adapter.HasPath.Add(t);                  // 경로는 있으나 이번 턴 도달 불가
        adapter.ApproachScript.Enqueue(ApproachStep.Exhausted);

        var cond = new TestTargetCondition { Name = "CondU" };
        cond.SetCandidates(t);
        var (attackRow, _, _) = MakeAttackRow("U", TacticApproachPolicy.ApproachAllowed, cond, adapter);
        var waitRow = MakeWaitRow("Uwait");
        var sel = MakeSelector(attackRow, waitRow);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, owner);

        CheckEq("U.status_success", status, BtStatus.Success);
        CheckEq("U.result_approach_consumed", sel.LastResult, TacticResult.ApproachConsumed);
        CheckEq("U.moved_once", adapter.ApproachCalls, 1);
        CheckEq("U.no_attack", adapter.ExecuteAttackCalls, 0);   // 접근만, 행동 없음
        // waitRow(다음 행)가 실행됐다면 LastResult가 ActionCompleted였을 것 -> 미실행 확인.

        Teardown(sel, owner);
    }

    // ---- [V] 이동 Running 중 상위 행 성립 -> preempt 없음(latch) -----------------------------------
    private void TestApproachRunningNotPreempted()
    {
        GD.Print("[V] 접근 Running 고정");
        var adapter = new TestTacticActionAdapter();
        var owner = MakeOwner(adapter, new Vector2I(0, 0));

        var t = MakeTarget(new Vector2I(5, 0));
        adapter.HasPath.Add(t);
        adapter.ApproachScript.Enqueue(ApproachStep.Moving);
        adapter.ApproachScript.Enqueue(ApproachStep.Moving);
        adapter.ApproachScript.Enqueue(ApproachStep.Arrived);

        // row0: 처음엔 불성립인 gate + wait. 실행 중 성립시켜도 preempt되면 안 된다.
        var gate0 = new TestGateCondition { Name = "Gate0", Pass = false };
        var row0 = new TacticRow { Name = "Row0V", RowId = "0V" };
        row0.AddChild(gate0);
        row0.AddChild(new TacticWait { Name = "Wait0V" });

        var cond = new TestTargetCondition { Name = "CondV" };
        cond.SetCandidates(t);
        var (row1, _, _) = MakeAttackRow("1V", TacticApproachPolicy.ApproachAllowed, cond, adapter);

        var sel = MakeSelector(row0, row1);
        AddChild(sel);

        // Tick1: row0 불성립 -> row1 접근 Moving -> Running, latch.
        CheckEq("V.t1_running", sel.Behave(0.016, owner), BtStatus.Running);
        CheckEq("V.t1_gate0_evaluated", gate0.EvaluateCount, 1);
        CheckEq("V.t1_moving", adapter.ApproachCalls, 1);

        // 상위 행을 성립시킨다.
        gate0.Pass = true;

        // Tick2: latch된 row1을 재개 -> row0은 재평가하지 않는다(preempt 없음).
        CheckEq("V.t2_running", sel.Behave(0.016, owner), BtStatus.Running);
        CheckEq("V.t2_gate0_not_reevaluated", gate0.EvaluateCount, 1);
        CheckEq("V.t2_moving_again", adapter.ApproachCalls, 2);

        // Tick3: 도달 -> 같은 턴 행동 완료.
        CheckEq("V.t3_success", sel.Behave(0.016, owner), BtStatus.Success);
        CheckEq("V.t3_gate0_never_preempted", gate0.EvaluateCount, 1);
        CheckEq("V.t3_attacked_once", adapter.ExecuteAttackCalls, 1);

        Teardown(sel, owner);
    }

    // ---- [V2] 새 턴 reset -> 진행 중 접근 취소(tween 누수 방지 계약) ------------------------------
    private void TestResetCancelsApproach()
    {
        GD.Print("[V2] reset이 접근 취소");
        var adapter = new TestTacticActionAdapter();
        var owner = MakeOwner(adapter, new Vector2I(0, 0));

        var t = MakeTarget(new Vector2I(5, 0));
        adapter.HasPath.Add(t);
        adapter.ApproachScript.Enqueue(ApproachStep.Moving);   // 계속 이동 중

        var cond = new TestTargetCondition { Name = "CondV2" };
        cond.SetCandidates(t);
        var (row, _, _) = MakeAttackRow("V2", TacticApproachPolicy.ApproachAllowed, cond, adapter);
        var sel = MakeSelector(row);
        AddChild(sel);

        // Tick1: 접근 Moving -> Running(commit, 이동 중).
        CheckEq("V2.t1_running", sel.Behave(0.016, owner), BtStatus.Running);
        CheckEq("V2.no_cancel_yet", adapter.CancelCalls, 0);

        // 새 자기 턴 시작을 모사: 명시 reset -> 진행 중 접근이 취소돼야 한다.
        sel.ResetForNewTurn();
        CheckEq("V2.cancel_called", adapter.CancelCalls, 1);

        Teardown(sel, owner);
    }

    // ---- [Z1] 행동의 commit 전 실패(지불/검증) -> 다음 행으로 fallback ----------------------------
    private void TestPreCommitActionFailureFallsThrough()
    {
        GD.Print("[Z1] 행동 commit 전 실패 fallback");
        var adapter = new TestTacticActionAdapter();
        var owner = MakeOwner(adapter, new Vector2I(0, 0));

        var t = MakeTarget(new Vector2I(1, 0));
        adapter.InRange.Add(t);                                   // 사거리 안 -> 접근 없이 행동 시도
        adapter.ExecuteScript.Enqueue(TacticActionOutcome.Ineligible);   // 실제 행동이 지불/검증 실패

        var cond = new TestTargetCondition { Name = "CondZ1" };
        cond.SetCandidates(t);
        var (attackRow, _, _) = MakeAttackRow("Z1", TacticApproachPolicy.ApproachAllowed, cond, adapter);
        var waitRow = MakeWaitRow("Z1wait");
        var sel = MakeSelector(attackRow, waitRow);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, owner);

        CheckEq("Z1.status_success", status, BtStatus.Success);
        CheckEq("Z1.tried_once", adapter.ExecuteAttackCalls, 1);
        CheckEq("Z1.no_move", adapter.ApproachCalls, 0);
        CheckEq("Z1.fell_through_to_wait", sel.LastResult, TacticResult.ActionCompleted);   // 다음 행(wait) 실행

        Teardown(sel, owner);
    }

    // ---- [Z2] 멀티턴 Running 행동 -> selector가 턴을 넘기지 않고 유지 ------------------------------
    private void TestRunningActionKeepsTurn()
    {
        GD.Print("[Z2] Running 행동 턴 유지");
        var adapter = new TestTacticActionAdapter();
        var owner = MakeOwner(adapter, new Vector2I(0, 0));

        var t = MakeTarget(new Vector2I(1, 0));
        adapter.InRange.Add(t);
        adapter.ExecuteScript.Enqueue(TacticActionOutcome.Running);     // 멀티턴 영창 진행
        adapter.ExecuteScript.Enqueue(TacticActionOutcome.Running);
        adapter.ExecuteScript.Enqueue(TacticActionOutcome.Completed);

        var cond = new TestTargetCondition { Name = "CondZ2" };
        cond.SetCandidates(t);
        var (attackRow, _, _) = MakeAttackRow("Z2", TacticApproachPolicy.ApproachAllowed, cond, adapter);
        var sel = MakeSelector(attackRow);
        AddChild(sel);

        CheckEq("Z2.t1_running", sel.Behave(0.016, owner), BtStatus.Running);   // 턴 유지
        CheckEq("Z2.t2_running", sel.Behave(0.016, owner), BtStatus.Running);
        CheckEq("Z2.t3_success", sel.Behave(0.016, owner), BtStatus.Success);
        CheckEq("Z2.result_completed", sel.LastResult, TacticResult.ActionCompleted);
        CheckEq("Z2.executed_thrice", adapter.ExecuteAttackCalls, 3);

        Teardown(sel, owner);
    }

    // ---- [Z4] commit 후 대상 무효 -> CancelledAfterCommit으로 턴 종료, 다음 행 미실행 -------------
    // Minimum Verification Matrix #9. commit(Running) -> 확정 대상 free -> 다음 tick 취소로 턴 종료.
    private void TestCancelledAfterCommitEndsTurn()
    {
        GD.Print("[Z4] commit 후 대상 무효");
        var adapter = new TestTacticActionAdapter();
        var owner = MakeOwner(adapter, new Vector2I(0, 0));

        var t = MakeTarget(new Vector2I(1, 0));
        adapter.InRange.Add(t);                                        // 사거리 안 -> 이동 없이 행동
        adapter.ExecuteScript.Enqueue(TacticActionOutcome.Running);    // 멀티턴 행동 시작 = commit

        var cond = new TestTargetCondition { Name = "CondZ4" };
        cond.SetCandidates(t);
        var (attackRow, _, _) = MakeAttackRow("Z4", TacticApproachPolicy.ApproachAllowed, cond, adapter);
        var waitRow = MakeWaitRow("Z4wait");
        var sel = MakeSelector(attackRow, waitRow);
        AddChild(sel);

        // Tick1: 행동이 Running으로 commit되고 행이 latch된다.
        CheckEq("Z4.t1_running", sel.Behave(0.016, owner), BtStatus.Running);
        CheckEq("Z4.t1_committed_once", adapter.ExecuteAttackCalls, 1);

        // commit 이후 확정 대상이 무효화된다(사망/free).
        t.Free();
        CheckEq("Z4.target_freed", GodotObject.IsInstanceValid(t), false);

        // Tick2: 취소로 턴을 종료한다. 다음 행(wait)은 실행하지 않는다(R6).
        CheckEq("Z4.t2_success", sel.Behave(0.016, owner), BtStatus.Success);
        CheckEq("Z4.result_cancelled_after_commit", sel.LastResult, TacticResult.CancelledAfterCommit);
        CheckEq("Z4.no_second_execute", adapter.ExecuteAttackCalls, 1);   // 무효 대상에 재실행/재지불 없음
        // wait 행이 실행됐다면 LastResult가 ActionCompleted였을 것 -> 미실행 확인.

        Teardown(sel, owner);
    }

    // ---- [Z3] 시퀀스의 비택틱 자식 -> 실행 전 fail-closed (오류 로그 예상) ------------------------
    private void TestSequenceRejectsNonTacticChild()
    {
        GD.Print("[Z3] 시퀀스 비택틱 자식 fail-closed (오류 로그 예상)");
        var adapter = new TestTacticActionAdapter();
        var owner = MakeOwner(adapter, new Vector2I(0, 0));

        var t = MakeTarget(new Vector2I(1, 0));
        adapter.InRange.Add(t);   // 셀렉터가 대상을 확정하도록

        var cond = new TestTargetCondition { Name = "CondZ3" };
        cond.SetCandidates(t);

        var row = new TacticRow { Name = "RowZ3", RowId = "Z3" };
        row.AddChild(cond);
        row.AddChild(new TacticTargetSelector { Name = "SelZ3", Policy = TacticApproachPolicy.ApproachAllowed });
        var seq = new TacticActionSequence { Name = "SeqZ3" };
        var raw = new TestRawAction { Name = "RawZ3", Outcome = BtStatus.Success };   // 비택틱 자식
        seq.AddChild(raw);
        row.AddChild(seq);

        var sel = MakeSelector(row);
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, owner);

        CheckEq("Z3.status_failure", status, BtStatus.Failure);
        CheckEq("Z3.raw_not_executed", raw.ExecuteCount, 0);

        Teardown(sel, owner);
    }

    // ---- 빌드 헬퍼 -------------------------------------------------------------------------------

    // 공격 행: [조건, 셀렉터(정책), 시퀀스[접근, 행동]]. 행동 대역이 사거리 spec을 컨텍스트에 공급한다(§10).
    private (TacticRow row, TacticApproach approach, TestScriptedAction execute) MakeAttackRow(
        string id, TacticApproachPolicy policy, TacticCondition condition, TestTacticActionAdapter adapter)
    {
        var row = new TacticRow { Name = $"Row_{id}", RowId = id };
        row.AddChild(condition);
        row.AddChild(new TacticTargetSelector { Name = $"Sel_{id}", Policy = policy });

        var seq = new TacticActionSequence { Name = $"Seq_{id}" };
        var approach = new TacticApproach { Name = $"App_{id}" };
        var execute = new TestScriptedAction { Name = $"Exec_{id}", Adapter = adapter };
        seq.AddChild(approach);
        seq.AddChild(execute);
        row.AddChild(seq);
        return (row, approach, execute);
    }

    private TacticRow MakeWaitRow(string id)
    {
        var row = new TacticRow { Name = $"Row_{id}", RowId = id };
        row.AddChild(new TacticWait { Name = $"Wait_{id}" });
        return row;
    }

    private static TacticPrioritySelector MakeSelector(params TacticRow[] rows)
    {
        var sel = new TacticPrioritySelector { Name = "TacticSelector" };
        foreach (var row in rows) sel.AddChild(row);
        return sel;
    }

    private TestOwner MakeOwner(TestTacticActionAdapter adapter, Vector2I pos)
    {
        var owner = new TestOwner { Name = "Owner", TilePosition = pos, TacticAdapter = adapter };
        _extraNodes.Add(owner);
        return owner;
    }

    private TestTacticTarget MakeTarget(Vector2I pos)
    {
        var t = new TestTacticTarget { TilePosition = pos };
        _targets.Add(t);
        return t;
    }

    private void Teardown(Node subtree, Node owner)
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

// owner 대역: 위치(정규 순서 원점) + 전투 어댑터를 제공한다.
public partial class TestOwner : Node, ITacticTarget, ITacticActionAdapterProvider
{
    public Vector2I TilePosition { get; set; }
    public ITacticActionAdapter TacticAdapter { get; set; }
}

// 전장 어댑터 대역. feasibility는 집합 조회(무변형)로 답하고, 접근은 스크립트대로 진행하며 호출 수를 기록한다.
// Arrived 시 대상을 사거리 집합에 넣어 "이동으로 사거리 진입"을 모사한다.
public class TestTacticActionAdapter : ITacticActionAdapter
{
    public readonly HashSet<GodotObject> InRange = new();
    public readonly HashSet<GodotObject> ReachThisTurn = new();
    public readonly HashSet<GodotObject> HasPath = new();
    public readonly List<GodotObject> Opponents = new();
    public readonly Queue<ApproachStep> ApproachScript = new();
    public ApproachStep DefaultApproachStep = ApproachStep.Arrived;

    public int FeasibilityCalls;
    public int ApproachCalls;
    public int CancelCalls;

    // 행동 실행 호출 수는 행동 대역(TestScriptedAction)이 이 카운터에 기록한다.
    public int ExecuteAttackCalls;
    public readonly Queue<TacticActionOutcome> ExecuteScript = new();
    public TacticActionOutcome DefaultOutcome = TacticActionOutcome.Completed;

    public IReadOnlyList<GodotObject> GetLivingOpponents(Node owner) => Opponents;

    public bool IsInRange(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets)
    {
        FeasibilityCalls++;
        return InRange.Contains(target);
    }

    public bool CanReachAndActThisTurn(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets)
    {
        FeasibilityCalls++;
        return ReachThisTurn.Contains(target);
    }

    public bool HasApproachPath(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets)
    {
        FeasibilityCalls++;
        return HasPath.Contains(target);
    }

    public ApproachStep Approach(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets, double delta)
    {
        ApproachCalls++;
        ApproachStep step = ApproachScript.Count > 0 ? ApproachScript.Dequeue() : DefaultApproachStep;
        if (step == ApproachStep.Arrived) InRange.Add(target);   // 이동으로 사거리 진입.
        return step;
    }

    public void CancelApproach(Node owner) => CancelCalls++;

    public void ResetTurn(Node owner) { }
}

// 행동 대역: 스크립트된 결과를 내며 사거리 spec을 공급한다(§10). 어댑터의 카운터/스크립트를 공유해
// 기존 시나리오 단언(ExecuteAttackCalls 등)을 그대로 유지한다.
public partial class TestScriptedAction : TacticExecuteAction
{
    public TestTacticActionAdapter Adapter { get; set; }

    // 근접 사거리 대역(실제 판정은 어댑터 대역의 집합이 하므로 값 자체는 중요하지 않다).
    public override IReadOnlyList<Vector2I> AttackRangePositions { get; } = new[] { Vector2I.Zero };

    public override bool CanStart(Node owner) => true;

    protected override TacticActionOutcome ExecuteAction(double delta, Node owner, GodotObject target)
    {
        Adapter.ExecuteAttackCalls++;
        return Adapter.ExecuteScript.Count > 0 ? Adapter.ExecuteScript.Dequeue() : Adapter.DefaultOutcome;
    }
}
#endif
