#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Compile;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Validation;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Common;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

// BT-003 Step 3: TacticBoardCompiler의 결정론 구조 생성·유닛별 격리·signature·2차 검사·실패 정리를 headless로
// 검증한다(실제 전장 parity는 bt003_step3_compile_battle_test). Node 구조는 detached라 TreeChildren이 아니라
// 네이티브 GetChildren()으로 검사한다.
public partial class Bt003Step3CompilerTest : Node
{
    private int _failures;
    private readonly List<CompiledBoardSnapshot> _toFree = new();

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0) { GD.Print("[BT-003 Step3] ALL PASS"); GetTree().Quit(0); }
            else { GD.Print($"[BT-003 Step3] FAILED: {failures} assertion(s)"); GetTree().Quit(1); }
        }
    }

    public int RunTests()
    {
        GD.Print("[BT-003 Step3] Running compiler tests...");
        try
        {
            TestCanonicalStructureMapping();
            TestNearestEnemySupplierInjection();
            TestEnemyCastingNearestNoSupplier();
            TestDisabledRowsSkipped();
            TestTwoUnitsIsolated();
            TestDeterministicSignature();
            TestInvalidBoardNotCompiled();
            TestActionCreateFailureCleanedUp();
            TestResolverThrowsIsolated();
            TestSnapshotDefensiveCopy();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-003 Step3] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        foreach (var s in _toFree) s.FreeRoot();
        _toFree.Clear();
        // 남은 Duplicate 인스턴스(RefCounted Resource)를 회수해 종료 시 leak 노이즈를 줄인다.
        // 종료 시 "ObjectDB instances leaked"는 clean --import에도 나오는 벤치 노이즈다(프로젝트 관례).
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return _failures;
    }

    // ---- [A] 정규 구조 매핑 ----------------------------------------------------------------------
    private void TestCanonicalStructureMapping()
    {
        GD.Print("[A] 정규 구조 매핑");
        var result = Track(TacticBoardCompiler.Compile(MakeValidBoard(), MakeResolver(), "rev-1", "unitA"));
        CheckEq("A.success", result.Success, true);
        if (result.Snapshot == null) { Fail("A.snapshot_null"); return; }

        TacticPrioritySelector root = result.Snapshot.Root;
        CheckEq("A.root_is_priority_selector", root != null, true);
        CheckEq("A.row_count", root.GetChildCount(), 4);
        CheckEq("A.rowids_order",
            string.Join(",", result.Snapshot.RowIds.Select(r => r.ToString())),
            "r_cast,r_nearest,default_attack,terminal_wait");
        CheckEq("A.metadata_board_id", result.Snapshot.SourceBoardId.ToString(), "board.pc");
        CheckEq("A.metadata_unit_id", result.Snapshot.UnitId.ToString(), "unitA");

        // r_cast: [EnemyCasting, Selector(Immediate), ActionSeq[Approach, Action]]
        Node rCast = root.GetChild(0);
        CheckEq("A.rcast_cond_enemy_casting", rCast.GetChild(0) is TacticConditionEnemyCasting, true);
        var castSelector = rCast.GetChild(1) as TacticTargetSelector;
        CheckEq("A.rcast_selector", castSelector != null, true);
        CheckEq("A.rcast_policy_immediate", castSelector?.Policy, TacticApproachPolicy.Immediate);
        Node castSeq = rCast.GetChild(2);
        CheckEq("A.rcast_action_is_sequence", castSeq is TacticActionSequence, true);
        CheckEq("A.rcast_seq_approach", castSeq.GetChild(0) is TacticApproach, true);
        var castAction = castSeq.GetChild(1) as TacticTurnAction;
        CheckEq("A.rcast_seq_turnaction", castAction != null, true);
        CheckEq("A.rcast_turnaction_instance", castAction?.TurnAction != null, true);

        // default_attack: draft에 any_enemy가 있어 SupplyAnyEnemy를 추가하지 않는다.
        Node rDefault = root.GetChild(2);
        CheckEq("A.default_first_is_any_enemy", rDefault.GetChild(0) is TacticConditionAnyEnemy, true);
        CheckEq("A.default_first_not_supply", rDefault.GetChild(0).Name.ToString(), "Cond0");
        CheckEq("A.default_child_count", rDefault.GetChildCount(), 3);   // [AnyEnemy, Selector, ActionSeq]

        // terminal_wait: [Wait] 하나만.
        Node rWait = root.GetChild(3);
        CheckEq("A.wait_child_count", rWait.GetChildCount(), 1);
        CheckEq("A.wait_is_tactic_wait", rWait.GetChild(0) is TacticWait, true);
    }

    // ---- [B] NearestEnemy 후보 공급 노드 주입(§11) ----------------------------------------------
    private void TestNearestEnemySupplierInjection()
    {
        GD.Print("[B] NearestEnemy 후보 공급 주입");
        var result = Track(TacticBoardCompiler.Compile(MakeValidBoard(), MakeResolver(), "rev-1"));
        if (result.Snapshot == null) { Fail("B.snapshot_null"); return; }

        // r_nearest: [always] -> nearest. target-bearing 조건이 없으므로 SupplyAnyEnemy가 먼저 온다.
        Node rNearest = result.Snapshot.Root.GetChild(1);
        var supply = rNearest.GetChild(0) as TacticConditionAnyEnemy;
        CheckEq("B.supply_injected", supply != null, true);
        CheckEq("B.supply_named", rNearest.GetChild(0).Name.ToString(), "SupplyAnyEnemy");
        CheckEq("B.draft_condition_after_supply", rNearest.GetChild(1) is TacticConditionAlways, true);
    }

    // ---- [B2] EnemyCasting + NearestEnemy: 후보 공급 조건이 이미 있으면 SupplyAnyEnemy 없음(§11) -----
    private void TestEnemyCastingNearestNoSupplier()
    {
        GD.Print("[B2] EnemyCasting+NearestEnemy 구조");
        var board = MakeValidBoard();
        // r_cast를 [enemy_casting] -> nearest_enemy로 바꾼다(target-bearing 조건 존재).
        board.Rows[0].Conditions = new Array<TacticConditionDefinition> { new TacticEnemyCastingConditionDefinition() };
        board.Rows[0].Target = new TacticNearestEnemyTargetDefinition();
        var result = Track(TacticBoardCompiler.Compile(board, MakeResolver(), "rev-1"));
        if (result.Snapshot == null) { Fail("B2.snapshot_null"); return; }

        Node row = result.Snapshot.Root.GetChild(0);
        // 구조 = [EnemyCasting, Selector, ActionSeq] — SupplyAnyEnemy 주입 없음.
        CheckEq("B2.first_is_enemy_casting", row.GetChild(0) is TacticConditionEnemyCasting, true);
        CheckEq("B2.first_not_supply", row.GetChild(0).Name.ToString(), "Cond0");
        CheckEq("B2.child_count_no_supply", row.GetChildCount(), 3);
        CheckEq("B2.no_supply_any_enemy",
            row.GetChildren().Any(c => c.Name.ToString() == "SupplyAnyEnemy"), false);
    }

    // ---- [B3] 비활성 행은 컴파일되지 않고 기본 행으로 내려간다(P1) -------------------------------
    private void TestDisabledRowsSkipped()
    {
        GD.Print("[B3] 비활성 행 skip");
        var board = MakeValidBoard();
        // 상위에 비활성 wait 행을 넣는다. 활성이면 항상 성립해 아래를 가리지만, 비활성이라 생성되지 않아야 한다.
        board.Rows.Insert(0, new TacticRowDefinition
        {
            RowId = "r_disabled_top",
            Enabled = false,
            Conditions = new Array<TacticConditionDefinition>(),
            Target = null,
            Action = new TacticWaitActionDefinition()
        });
        // r_nearest도 비활성으로.
        board.Rows.First(r => r.RowId.ToString() == "r_nearest").Enabled = false;

        var result = Track(TacticBoardCompiler.Compile(board, MakeResolver(), "rev-1"));
        if (result.Snapshot == null) { Fail("B3.snapshot_null"); return; }

        // 생성 행 = r_cast, default_attack, terminal_wait (비활성 2개 제외).
        CheckEq("B3.generated_row_count", result.Snapshot.Root.GetChildCount(), 3);
        CheckEq("B3.rowids_exclude_disabled",
            string.Join(",", result.Snapshot.RowIds.Select(r => r.ToString())),
            "r_cast,default_attack,terminal_wait");
        CheckEq("B3.no_disabled_node",
            result.Snapshot.Root.GetChildren().Any(c => c.Name.ToString() == "Row_r_disabled_top"), false);
    }

    // ---- [C] 두 유닛 격리 -----------------------------------------------------------------------
    private void TestTwoUnitsIsolated()
    {
        GD.Print("[C] 두 유닛 격리");
        var catalog = MakeCatalog();
        var resolver = new TacticActionCatalogResolver(catalog);
        var board = MakeValidBoard();

        var r1 = Track(TacticBoardCompiler.Compile(board, resolver, "rev-1", "unitA"));
        var r2 = Track(TacticBoardCompiler.Compile(board, resolver, "rev-1", "unitB"));
        if (r1.Snapshot == null || r2.Snapshot == null) { Fail("C.snapshot_null"); return; }

        CheckEq("C.distinct_root", ReferenceEquals(r1.Snapshot.Root, r2.Snapshot.Root), false);

        TurnActionBase a1 = r1.Snapshot.Actions[0];
        TurnActionBase a2 = r2.Snapshot.Actions[0];
        CheckEq("C.distinct_action_instance", ReferenceEquals(a1, a2), false);

        // 한 유닛의 explicit target 바인딩이 다른 유닛/템플릿에 보이지 않는다(런타임 상태 격리).
        a1.BindExplicitTarget(null);
        CheckEq("C.unit1_bound", a1.IsExplicitTargetBound, true);
        CheckEq("C.unit2_not_bound", a2.IsExplicitTargetBound, false);
        CheckEq("C.template_not_bound", catalog.Entries[0].Template.IsExplicitTargetBound, false);
    }

    // ---- [D] 결정론 signature --------------------------------------------------------------------
    private void TestDeterministicSignature()
    {
        GD.Print("[D] 결정론 signature");
        var s1 = Track(TacticBoardCompiler.Compile(MakeValidBoard(), MakeResolver(), "rev-1")).Snapshot;
        var s2 = Track(TacticBoardCompiler.Compile(MakeValidBoard(), MakeResolver(), "rev-1")).Snapshot;
        if (s1 == null || s2 == null) { Fail("D.snapshot_null"); return; }

        CheckEq("D.same_source_signature", s1.SourceSignature, s2.SourceSignature);
        CheckEq("D.same_compile_input_signature", s1.CompileInputSignature, s2.CompileInputSignature);

        // catalog revision만 바뀌면 SourceSignature는 같고 CompileInputSignature는 달라진다(§7/§9).
        var s3 = Track(TacticBoardCompiler.Compile(MakeValidBoard(), MakeResolver(), "rev-2")).Snapshot;
        CheckEq("D.revision_same_source", s1.SourceSignature, s3.SourceSignature);
        CheckEq("D.revision_diff_compile_input", s1.CompileInputSignature != s3.CompileInputSignature, true);

        // board가 바뀌면 SourceSignature가 달라진다.
        var changed = MakeValidBoard();
        changed.Rows[0].RowId = "r_cast_renamed";
        var s4 = Track(TacticBoardCompiler.Compile(changed, MakeResolver(), "rev-1")).Snapshot;
        CheckEq("D.board_change_diff_source", s1.SourceSignature != s4.SourceSignature, true);
    }

    // ---- [E] 유효하지 않은 보드는 compile하지 않는다 -------------------------------------------
    private void TestInvalidBoardNotCompiled()
    {
        GD.Print("[E] invalid 보드 미컴파일");
        var board = MakeValidBoard();
        board.Rows.RemoveAt(2);   // default_attack 제거 -> validation Error
        var result = TacticBoardCompiler.Compile(board, MakeResolver(), "rev-1");
        CheckEq("E.not_success", result.Success, false);
        CheckEq("E.no_snapshot", result.Snapshot == null, true);
        CheckEq("E.has_default_missing",
            result.Diagnostics.Any(d => d.Code.ToString() == TacticDiagnosticCodes.DefaultAttackMissing.ToString()), true);
    }

    // ---- [F] 행동 인스턴스 생성 실패는 정리 + Error ---------------------------------------------
    private void TestActionCreateFailureCleanedUp()
    {
        GD.Print("[F] 인스턴스 생성 실패");
        var result = TacticBoardCompiler.Compile(MakeValidBoard(), new NullCreatingResolver(), "rev-1");
        CheckEq("F.not_success", result.Success, false);
        CheckEq("F.no_snapshot", result.Snapshot == null, true);
        CheckEq("F.action_create_failed",
            result.Diagnostics.Any(d => d.Code.ToString() == TacticDiagnosticCodes.ActionCreateFailed.ToString()), true);
    }

    // ---- [G] resolver 예외 격리 ------------------------------------------------------------------
    private void TestResolverThrowsIsolated()
    {
        GD.Print("[G] resolver 예외 격리");
        var result = TacticBoardCompiler.Compile(MakeValidBoard(), new ThrowingResolver(), "rev-1");
        CheckEq("G.not_success", result.Success, false);
        CheckEq("G.no_snapshot", result.Snapshot == null, true);
        CheckEq("G.compiler_internal_error",
            result.Diagnostics.Any(d => d.Code.ToString() == TacticDiagnosticCodes.CompilerInternalError.ToString()), true);
    }

    // ---- [H] snapshot 결과 컬렉션 방어 복사(P4) ------------------------------------------------
    private void TestSnapshotDefensiveCopy()
    {
        GD.Print("[H] snapshot 방어 복사");
        var rowIds = new List<StringName> { "a", "b" };
        var actions = new List<TurnActionBase> { new TurnAction_Attack() };
        var snapshot = new CompiledBoardSnapshot("board", 1, "unit", rowIds, "sig", "csig", null, actions);

        rowIds.Add("c");                       // 생성 후 원본 목록 변형
        actions.Add(new TurnAction_Attack());
        CheckEq("H.rowids_unaffected", snapshot.RowIds.Count, 2);
        CheckEq("H.actions_unaffected", snapshot.Actions.Count, 1);
    }

    // ---- 빌드 헬퍼 -------------------------------------------------------------------------------

    private TacticCompileResult Track(TacticCompileResult result)
    {
        if (result.Snapshot != null) _toFree.Add(result.Snapshot);
        return result;
    }

    private static TacticBoardDefinition MakeValidBoard()
    {
        var board = new TacticBoardDefinition { SchemaVersion = 1, BoardId = "board.pc", DisplayName = "보드" };
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "r_cast",
            Enabled = true,
            Conditions = new Array<TacticConditionDefinition> { new TacticEnemyCastingConditionDefinition() },
            Target = new TacticContextTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "sonic_blow" },
            ApproachPolicy = TacticApproachPolicy.Immediate
        });
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "r_nearest",
            Enabled = true,
            Conditions = new Array<TacticConditionDefinition> { new TacticAlwaysConditionDefinition() },
            Target = new TacticNearestEnemyTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "default_attack" },
            ApproachPolicy = TacticApproachPolicy.ApproachAllowed
        });
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

    private static TacticActionCatalog MakeCatalog() => new()
    {
        CatalogRevision = "rev-1",
        Entries = new Array<TacticActionCatalogEntry>
        {
            new() { ActionId = "sonic_blow", Template = new TurnAction_Attack() },
            new() { ActionId = "default_attack", Template = new TurnAction_Attack() }
        }
    };

    private static ITacticActionResolver MakeResolver() => new TacticActionCatalogResolver(MakeCatalog());

    private void CheckEq<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }

    private void Fail(string name) { _failures++; GD.Print($"  FAIL: {name}"); }
}

// 해석은 Ok지만 인스턴스 생성이 실패하는 resolver(정리 경로 검증).
public sealed class NullCreatingResolver : ITacticActionResolver
{
    public TacticActionResolution Resolve(StringName actionId) => TacticActionResolution.Ok;
    public TurnActionBase CreateInstance(StringName actionId) => null;
}

// CreateInstance에서 예외를 던지는 resolver(compiler 예외 격리 검증).
public sealed class ThrowingResolver : ITacticActionResolver
{
    public TacticActionResolution Resolve(StringName actionId) => TacticActionResolution.Ok;
    public TurnActionBase CreateInstance(StringName actionId) => throw new InvalidOperationException("boom");
}
#endif
