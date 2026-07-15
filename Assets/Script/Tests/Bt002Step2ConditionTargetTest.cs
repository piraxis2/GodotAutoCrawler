#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using Godot;

namespace AutoCrawler.Assets.Script.Tests;

// BT-002 Step 2: 조건 후보 교집합과 확정 대상 공유(TacticRowContext/TacticCondition/TacticTargetSelector).
// test condition/target 대역만 쓰며 실제 이동·비용 지불·H-4 어휘는 다루지 않는다(Step 2 제외 범위).
//
// 완료 조건 매핑:
//  L/M  두 대상 조건 교집합 — 같은 유닛이 모두 만족할 때만 성립(빈 교집합은 불성립)
//  N    gate 조건은 후보를 바꾸지 않는다
//  O    여러 후보는 거리→Y→X(ADR-017)로 같은 대상을 선택
//  P    조건·selector·action이 같은 identity를 보고 freed 후보를 남기지 않는다
public partial class Bt002Step2ConditionTargetTest : Node
{
    private int _failures;
    private readonly List<TestTacticTarget> _targets = new();

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0)
            {
                GD.Print("[BT-002 Step2] ALL PASS");
                GetTree().Quit(0);
            }
            else
            {
                GD.Print($"[BT-002 Step2] FAILED: {failures} assertion(s)");
                GetTree().Quit(1);
            }
        }
    }

    public int RunTests()
    {
        GD.Print("[BT-002 Step2] Running condition/target context tests...");
        try
        {
            TestIntersectionCommonUnit();
            TestEmptyIntersectionIneligible();
            TestGateDoesNotChangeCandidates();
            TestTieBreakSelectsCanonical();
            TestFreedCandidateSkippedAndIdentityShared();
            TestSelectorAfterConditionBypassBlocked();
            TestOwnerWithoutContextFailsClosed();
            TestContextDropsCandidateFreedAfterStorage();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-002 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        return _failures;
    }

    // ---- [L] 두 대상 조건 교집합 -> 공통 유닛만 확정 ----------------------------------------------
    private void TestIntersectionCommonUnit()
    {
        GD.Print("[L] 교집합 공통 유닛");
        var u1 = MakeTarget(new Vector2I(5, 0));
        var u2 = MakeTarget(new Vector2I(1, 0));
        var u3 = MakeTarget(new Vector2I(9, 0));

        var condA = new TestTargetCondition { Name = "CondA" };
        condA.SetCandidates(u1, u2);
        var condB = new TestTargetCondition { Name = "CondB" };
        condB.SetCandidates(u2, u3);

        var (row, action) = MakeTargetRow("L", new TacticTargetSelector { Name = "SelL" }, condA, condB);
        var sel = MakeSelector(row);
        var owner = MakeTarget(new Vector2I(0, 0));
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, owner);

        CheckEq("L.status_success", status, BtStatus.Success);
        CheckEq("L.confirmed_common_u2", ReferenceEquals(action.SeenTarget, u2), true);
        CheckEq("L.action_ran", action.ExecuteCount, 1);

        Teardown(sel);
    }

    // ---- [M] 빈 교집합 -> 불성립 -----------------------------------------------------------------
    private void TestEmptyIntersectionIneligible()
    {
        GD.Print("[M] 빈 교집합 불성립");
        var u1 = MakeTarget(new Vector2I(1, 0));
        var u2 = MakeTarget(new Vector2I(2, 0));

        var condA = new TestTargetCondition { Name = "CondA" };
        condA.SetCandidates(u1);
        var condB = new TestTargetCondition { Name = "CondB" };
        condB.SetCandidates(u2);   // 공통 유닛 없음

        var (row, action) = MakeTargetRow("M", new TacticTargetSelector { Name = "SelM" }, condA, condB);
        var sel = MakeSelector(row);
        var owner = MakeTarget(new Vector2I(0, 0));
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, owner);

        CheckEq("M.status_failure", status, BtStatus.Failure);
        CheckEq("M.action_not_run", action.ExecuteCount, 0);

        Teardown(sel);
    }

    // ---- [N] gate 조건은 후보를 바꾸지 않는다 ----------------------------------------------------
    private void TestGateDoesNotChangeCandidates()
    {
        GD.Print("[N] gate 후보 불변");

        // N1: gate(true) + target 조건 -> 확정 대상은 target 조건 후보에서만 나온다(gate가 후보 추가 안 함).
        {
            var near = MakeTarget(new Vector2I(1, 0));
            var far = MakeTarget(new Vector2I(5, 0));
            var gate = new TestGateCondition { Name = "Gate", Pass = true };
            var cond = new TestTargetCondition { Name = "Cond" };
            cond.SetCandidates(far, near);

            var (row, action) = MakeTargetRow("N1", new TacticTargetSelector { Name = "SelN1" }, gate, cond);
            var sel = MakeSelector(row);
            var owner = MakeTarget(new Vector2I(0, 0));
            AddChild(sel);

            CheckEq("N1.status_success", sel.Behave(0.016, owner), BtStatus.Success);
            CheckEq("N1.confirmed_from_cond_near", ReferenceEquals(action.SeenTarget, near), true);

            Teardown(sel);
        }

        // N2: gate-only(대상 조건 없음) -> 후보가 없으므로 셀렉터가 불성립. gate는 후보를 만들지 않음을 증명.
        {
            var gate = new TestGateCondition { Name = "GateOnly", Pass = true };
            var (row, action) = MakeTargetRow("N2", new TacticTargetSelector { Name = "SelN2" }, gate);
            var sel = MakeSelector(row);
            var owner = MakeTarget(new Vector2I(0, 0));
            AddChild(sel);

            CheckEq("N2.status_failure", sel.Behave(0.016, owner), BtStatus.Failure);
            CheckEq("N2.action_not_run", action.ExecuteCount, 0);

            Teardown(sel);
        }

        // N3: gate(false) -> 즉시 불성립, 이후 대상 조건/셀렉터/행동 미실행(후보 수집 자체를 안 함).
        {
            var u1 = MakeTarget(new Vector2I(1, 0));
            var gate = new TestGateCondition { Name = "GateFalse", Pass = false };
            var cond = new TestTargetCondition { Name = "CondAfterGate" };
            cond.SetCandidates(u1);

            var (row, action) = MakeTargetRow("N3", new TacticTargetSelector { Name = "SelN3" }, gate, cond);
            var sel = MakeSelector(row);
            var owner = MakeTarget(new Vector2I(0, 0));
            AddChild(sel);

            CheckEq("N3.status_failure", sel.Behave(0.016, owner), BtStatus.Failure);
            CheckEq("N3.cond_not_evaluated", cond.EvaluateCount, 0);
            CheckEq("N3.action_not_run", action.ExecuteCount, 0);

            Teardown(sel);
        }
    }

    // ---- [O] 여러 후보 -> 거리→Y→X 정규 순서로 같은 대상 선택 ------------------------------------
    private void TestTieBreakSelectsCanonical()
    {
        GD.Print("[O] ADR-017 tie-break");

        // O1: 서로 다른 거리 -> 최근접 선택.
        {
            var a = MakeTarget(new Vector2I(3, 0));
            var b = MakeTarget(new Vector2I(1, 0));
            var c = MakeTarget(new Vector2I(2, 0));
            var cond = new TestTargetCondition { Name = "CondDist" };
            cond.SetCandidates(a, b, c);   // 수집 순서와 무관해야 함

            var (row, action) = MakeTargetRow("O1", new TacticTargetSelector { Name = "SelO1" }, cond);
            var sel = MakeSelector(row);
            var owner = MakeTarget(new Vector2I(0, 0));
            AddChild(sel);

            sel.Behave(0.016, owner);
            CheckEq("O1.nearest_b", ReferenceEquals(action.SeenTarget, b), true);

            Teardown(sel);
        }

        // O2: 거리 동점 -> Y가 작은 후보. (2,1)·(2,-1)·(1,2) 모두 Manhattan 3, Y 최소 = -1.
        {
            var yPos = MakeTarget(new Vector2I(2, 1));
            var yNeg = MakeTarget(new Vector2I(2, -1));
            var other = MakeTarget(new Vector2I(1, 2));
            var cond = new TestTargetCondition { Name = "CondY" };
            cond.SetCandidates(yPos, yNeg, other);

            var (row, action) = MakeTargetRow("O2", new TacticTargetSelector { Name = "SelO2" }, cond);
            var sel = MakeSelector(row);
            var owner = MakeTarget(new Vector2I(0, 0));
            AddChild(sel);

            sel.Behave(0.016, owner);
            CheckEq("O2.smallest_y", ReferenceEquals(action.SeenTarget, yNeg), true);

            Teardown(sel);
        }

        // O3: 거리·Y 동점 -> X가 작은 후보. (1,2)·(-1,2) 모두 Manhattan 3, Y 2, X 최소 = -1.
        {
            var xPos = MakeTarget(new Vector2I(1, 2));
            var xNeg = MakeTarget(new Vector2I(-1, 2));
            var cond = new TestTargetCondition { Name = "CondX" };
            cond.SetCandidates(xPos, xNeg);

            var (row, action) = MakeTargetRow("O3", new TacticTargetSelector { Name = "SelO3" }, cond);
            var sel = MakeSelector(row);
            var owner = MakeTarget(new Vector2I(0, 0));
            AddChild(sel);

            sel.Behave(0.016, owner);
            CheckEq("O3.smallest_x", ReferenceEquals(action.SeenTarget, xNeg), true);

            Teardown(sel);
        }
    }

    // ---- [P] freed 후보 제외 + 조건·selector·action identity 공유 --------------------------------
    private void TestFreedCandidateSkippedAndIdentityShared()
    {
        GD.Print("[P] freed 제외 + identity 공유");
        var near = MakeTarget(new Vector2I(1, 0));   // 가장 가까움: freed되면 선택되면 안 됨
        var far = MakeTarget(new Vector2I(4, 0));

        var cond = new TestTargetCondition { Name = "CondP" };
        cond.SetCandidates(near, far);

        var (row, action) = MakeTargetRow("P", new TacticTargetSelector { Name = "SelP" }, cond);
        var sel = MakeSelector(row);
        var owner = MakeTarget(new Vector2I(0, 0));
        AddChild(sel);

        // 평가 직전 최근접 후보를 free한다. 셀렉터는 freed를 건너뛰고 far를 확정해야 한다.
        near.Free();
        CheckEq("P.near_freed", GodotObject.IsInstanceValid(near), false);

        BtStatus status = sel.Behave(0.016, owner);

        CheckEq("P.status_success", status, BtStatus.Success);
        CheckEq("P.confirmed_far_not_freed", ReferenceEquals(action.SeenTarget, far), true);
        // action이 본 대상은 셀렉터가 확정한 identity와 동일해야 한다(같은 컨텍스트 공유).
        CheckEq("P.action_sees_valid_target", action.SeenTarget != null && GodotObject.IsInstanceValid(action.SeenTarget), true);

        Teardown(sel);
    }

    // ---- [R1] 셀렉터 뒤 대상 조건은 교집합 우회 -> fail-closed (오류 로그 예상) --------------------
    // [조건 A, 셀렉터, 조건 B, 행동]. 셀렉터가 마지막 pre-action 자식이 아니므로 행을 실행하지 않는다.
    private void TestSelectorAfterConditionBypassBlocked()
    {
        GD.Print("[R1] 셀렉터 뒤 조건 fail-closed (오류 로그 예상)");
        var near = MakeTarget(new Vector2I(1, 0));
        var far = MakeTarget(new Vector2I(5, 0));

        var condA = new TestTargetCondition { Name = "CondA" };
        condA.SetCandidates(near, far);
        var condB = new TestTargetCondition { Name = "CondB" };
        condB.SetCandidates(far);

        // MakeTargetRow는 [조건들..., 셀렉터, 행동]을 만든다. 여기선 셀렉터가 조건 뒤에 오도록 직접 조립한다.
        var row = new TacticRow { Name = "RowR1", RowId = "R1" };
        row.AddChild(condA);
        row.AddChild(new TacticTargetSelector { Name = "SelR1" });
        row.AddChild(condB);   // 셀렉터 뒤의 대상 조건 -> 잘못된 구조
        var action = new TestContextAction { Name = "ActR1" };
        row.AddChild(action);

        var sel = MakeSelector(row);
        var owner = MakeTarget(new Vector2I(0, 0));
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, owner);

        CheckEq("R1.status_failure", status, BtStatus.Failure);
        CheckEq("R1.action_not_run", action.ExecuteCount, 0);
        CheckEq("R1.no_target_confirmed", action.SeenTarget, (GodotObject)null);

        Teardown(sel);
    }

    // ---- [R2] owner가 ITacticTarget이 아니면 fail-closed ------------------------------------------
    private void TestOwnerWithoutContextFailsClosed()
    {
        GD.Print("[R2] owner 원점 없음 fail-closed (오류 로그 예상)");
        var u1 = MakeTarget(new Vector2I(1, 0));
        var cond = new TestTargetCondition { Name = "CondR2" };
        cond.SetCandidates(u1);

        var (row, action) = MakeTargetRow("R2", new TacticTargetSelector { Name = "SelR2" }, cond);
        var sel = MakeSelector(row);
        var plainOwner = new Node { Name = "PlainOwner" };   // ITacticTarget 아님
        AddChild(sel);

        BtStatus status = sel.Behave(0.016, plainOwner);

        CheckEq("R2.status_failure", status, BtStatus.Failure);
        CheckEq("R2.action_not_run", action.ExecuteCount, 0);
        CheckEq("R2.no_target_confirmed", action.SeenTarget, (GodotObject)null);

        plainOwner.Free();
        Teardown(sel);
    }

    // ---- [Q] 컨텍스트 보관 후 free된 후보는 LiveCandidates에서 제거된다 ---------------------------
    private void TestContextDropsCandidateFreedAfterStorage()
    {
        GD.Print("[Q] 보관 후 free 무효화");

        // Q1: 컨텍스트 직접 검증 — 후보를 넣은 뒤 free하면 LiveCandidates가 제거하고, 확정 대상도 무효가 된다.
        {
            var a = MakeTarget(new Vector2I(1, 0));
            var b = MakeTarget(new Vector2I(2, 0));
            var ctx = new TacticRowContext();
            ctx.ContributeCandidates(new GodotObject[] { a, b });
            CheckEq("Q1.two_live", ctx.LiveCandidates.Count, 2);

            a.Free();
            CheckEq("Q1.freed_dropped", ctx.LiveCandidates.Count, 1);
            CheckEq("Q1.remaining_is_b", ctx.LiveCandidates.Count == 1 && ReferenceEquals(ctx.LiveCandidates[0], b), true);

            ctx.ConfirmTarget(b);
            CheckEq("Q1.confirmed_valid", ctx.HasValidConfirmedTarget, true);
            b.Free();
            CheckEq("Q1.confirmed_invalid_after_free", ctx.HasValidConfirmedTarget, false);
        }

        // Q2: 행 경로 — 조건이 후보를 넣은 뒤, 셀렉터 앞의 gate가 최근접 후보를 free한다.
        //     셀렉터는 free된 후보를 건너뛰고 다음 후보를 확정해야 한다.
        {
            var near = MakeTarget(new Vector2I(1, 0));   // 조건이 후보로 넣지만 이후 free됨
            var far = MakeTarget(new Vector2I(3, 0));
            var cond = new TestTargetCondition { Name = "CondQ2" };
            cond.SetCandidates(near, far);
            var freeing = new TestFreeingGate { Name = "FreeGateQ2", TargetToFree = near };

            var (row, action) = MakeTargetRow("Q2", new TacticTargetSelector { Name = "SelQ2" }, cond, freeing);
            var sel = MakeSelector(row);
            var owner = MakeTarget(new Vector2I(0, 0));
            AddChild(sel);

            BtStatus status = sel.Behave(0.016, owner);

            CheckEq("Q2.near_freed", GodotObject.IsInstanceValid(near), false);
            CheckEq("Q2.status_success", status, BtStatus.Success);
            CheckEq("Q2.confirmed_far", ReferenceEquals(action.SeenTarget, far), true);

            Teardown(sel);
        }
    }

    // ---- 빌드 헬퍼 -------------------------------------------------------------------------------

    // 대상 조건/셀렉터/행동으로 한 행을 조립한다. 자식 순서 [조건들..., 셀렉터, 행동].
    private static (TacticRow row, TestContextAction action) MakeTargetRow(
        string id, TacticTargetSelector selector, params TacticCondition[] conditions)
    {
        var row = new TacticRow { Name = $"Row_{id}", RowId = id };
        foreach (var cond in conditions) row.AddChild(cond);
        row.AddChild(selector);
        var action = new TestContextAction { Name = $"Act_{id}" };
        row.AddChild(action);
        return (row, action);
    }

    private static TacticPrioritySelector MakeSelector(params TacticRow[] rows)
    {
        var sel = new TacticPrioritySelector { Name = "TacticSelector" };
        foreach (var row in rows) sel.AddChild(row);
        return sel;
    }

    private TestTacticTarget MakeTarget(Vector2I pos)
    {
        var t = new TestTacticTarget { TilePosition = pos };
        _targets.Add(t);
        return t;
    }

    private void Teardown(Node subtree)
    {
        RemoveChild(subtree);
        subtree.QueueFree();
        foreach (var t in _targets)
        {
            if (GodotObject.IsInstanceValid(t)) t.Free();
        }
        _targets.Clear();
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

// 위치를 갖는 대상 대역. ITacticTarget으로 정규 순서에 참여하고, GodotObject라 freed 검사에 쓰인다.
public partial class TestTacticTarget : Node, ITacticTarget
{
    public Vector2I TilePosition { get; set; }
}

// targetless gate 조건 대역. Pass에 따라 Success/Failure만 반환하고 후보를 만들지 않는다.
public partial class TestGateCondition : TacticCondition
{
    public bool Pass { get; set; } = true;
    public int EvaluateCount { get; private set; }

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        EvaluateCount++;
        return Pass ? BtStatus.Success : BtStatus.Failure;
    }
}

// target-bearing 조건 대역. 살아 있는 후보가 있으면 컨텍스트에 기여하고 Success, 없으면 Failure(조건 거짓).
public partial class TestTargetCondition : TacticCondition
{
    private readonly List<GodotObject> _candidates = new();
    public int EvaluateCount { get; private set; }

    public void SetCandidates(params GodotObject[] candidates)
    {
        _candidates.Clear();
        _candidates.AddRange(candidates);
    }

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        EvaluateCount++;
        var live = _candidates.Where(GodotObject.IsInstanceValid).ToList();
        if (live.Count == 0) return BtStatus.Failure;
        Context.ContributeCandidates(live);
        return BtStatus.Success;
    }
}

// 셀렉터 앞에서 특정 후보를 free하는 gate 대역. 컨텍스트에 보관된 후보가 이후 free되는 경로를 재현한다.
public partial class TestFreeingGate : TacticCondition
{
    public GodotObject TargetToFree { get; set; }

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        if (TargetToFree != null && GodotObject.IsInstanceValid(TargetToFree)) TargetToFree.Free();
        return BtStatus.Success;
    }
}

// 확정 대상을 관찰하는 행동 대역. 컨텍스트의 ConfirmedTarget을 기록해 identity 공유를 단언한다.
public partial class TestContextAction : BehaviorTree_Action, ITacticNode, ITacticContextBound
{
    private TacticRowContext _context;
    private TacticResult _last = TacticResult.ActionCompleted;

    public int ExecuteCount { get; private set; }
    public GodotObject SeenTarget { get; private set; }
    public TacticResult LastResult => _last;

    public void BindContext(TacticRowContext context) => _context = context;

    public void ResetForNewTurn()
    {
        _last = TacticResult.Ineligible;
        SeenTarget = null;
    }

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        ExecuteCount++;
        SeenTarget = _context?.ConfirmedTarget;
        _last = TacticResult.ActionCompleted;
        return _last.ToBtStatus();
    }
}
#endif
