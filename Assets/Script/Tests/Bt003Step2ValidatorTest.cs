#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Validation;
using AutoCrawler.Assets.Script.TurnAction.Common;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Tests;

// BT-003 Step 2: TacticBoardValidator의 pure/read-only 검증과 구조화 진단 headless 검증.
// Node 생성/compile은 범위 밖이다. draft/resolver/template 불변, 결정론적 진단 순서를 고정한다.
public partial class Bt003Step2ValidatorTest : Node
{
    private int _failures;

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0) { GD.Print("[BT-003 Step2] ALL PASS"); GetTree().Quit(0); }
            else { GD.Print($"[BT-003 Step2] FAILED: {failures} assertion(s)"); GetTree().Quit(1); }
        }
    }

    public int RunTests()
    {
        GD.Print("[BT-003 Step2] Running validator/diagnostics tests...");
        try
        {
            TestValidBoardPasses();
            TestNullBoard();
            TestUnsupportedSchemaVersion();
            TestEmptyBoardId();
            TestDuplicateRowId();
            TestTooManyConditions();
            TestUnknownConditionType();
            TestActionMissing();
            TestActionIdEmpty();
            TestCatalogUnknownDuplicateNullTemplate();
            TestResolverMissing();
            TestTargetRequiredAndNotAllowed();
            TestMissingTargetContext();
            TestSystemRowPresenceAndOrder();
            TestSystemRowMalformed();
            TestSystemRowCorruptionVariants();
            TestMaxRowsBoundary();
            TestSameFieldCodeTieBreak();
            TestResultDefensiveCopy();
            TestWarningsAllowSuccess();
            TestDeterministicOrderingAndBoardBeforeRow();
            TestValidatorDoesNotMutateInputs();
            TestTwoResolversDiffer();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[BT-003 Step2] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }
        return _failures;
    }

    // ---- [A] 유효 보드 ---------------------------------------------------------------------------
    private void TestValidBoardPasses()
    {
        GD.Print("[A] 유효 보드");
        var result = TacticBoardValidator.Validate(MakeValidBoard(), MakeResolver());
        CheckEq("A.is_valid", result.IsValid, true);
        CheckEq("A.no_diagnostics", result.Diagnostics.Count, 0);
    }

    // ---- [B] null board -------------------------------------------------------------------------
    private void TestNullBoard()
    {
        GD.Print("[B] null board");
        var result = TacticBoardValidator.Validate(null, MakeResolver());
        CheckEq("B.invalid", result.IsValid, false);
        CheckEq("B.board_null", Has(result, TacticDiagnosticCodes.BoardNull), true);
    }

    // ---- [C] unsupported schema version ---------------------------------------------------------
    private void TestUnsupportedSchemaVersion()
    {
        GD.Print("[C] future version");
        var board = MakeValidBoard();
        board.SchemaVersion = 2;
        var result = TacticBoardValidator.Validate(board, MakeResolver());
        CheckEq("C.invalid", result.IsValid, false);
        CheckEq("C.unsupported_version", Has(result, TacticDiagnosticCodes.UnsupportedSchemaVersion), true);
    }

    // ---- [D] empty BoardId ----------------------------------------------------------------------
    private void TestEmptyBoardId()
    {
        GD.Print("[D] empty BoardId");
        var board = MakeValidBoard();
        board.BoardId = "";
        var result = TacticBoardValidator.Validate(board, MakeResolver());
        CheckEq("D.board_id_empty", Has(result, TacticDiagnosticCodes.BoardIdEmpty), true);
    }

    // ---- [E] duplicate RowId --------------------------------------------------------------------
    private void TestDuplicateRowId()
    {
        GD.Print("[E] duplicate RowId");
        var board = MakeValidBoard();
        board.Rows[0].RowId = "r_nearest";   // r_nearest와 충돌
        var result = TacticBoardValidator.Validate(board, MakeResolver());
        CheckEq("E.invalid", result.IsValid, false);
        CheckEq("E.row_id_duplicate", Has(result, TacticDiagnosticCodes.RowIdDuplicate), true);
    }

    // ---- [F] too many conditions ----------------------------------------------------------------
    private void TestTooManyConditions()
    {
        GD.Print("[F] 조건 3개");
        var board = MakeValidBoard();
        board.Rows[0].Conditions = new Array<TacticConditionDefinition>
        {
            new TacticAlwaysConditionDefinition(),
            new TacticAnyEnemyConditionDefinition(),
            new TacticEnemyCastingConditionDefinition()
        };
        var result = TacticBoardValidator.Validate(board, MakeResolver());
        CheckEq("F.too_many_conditions", Has(result, TacticDiagnosticCodes.TooManyConditions), true);
    }

    // ---- [G] unknown condition type -------------------------------------------------------------
    private void TestUnknownConditionType()
    {
        GD.Print("[G] unknown condition type");
        var board = MakeValidBoard();
        board.Rows[0].Conditions = new Array<TacticConditionDefinition> { new TestUnknownConditionDefinition() };
        var result = TacticBoardValidator.Validate(board, MakeResolver());
        CheckEq("G.condition_unknown_type", Has(result, TacticDiagnosticCodes.ConditionUnknownType), true);
    }

    // ---- [H] action missing ---------------------------------------------------------------------
    private void TestActionMissing()
    {
        GD.Print("[H] action 누락");
        var board = MakeValidBoard();
        board.Rows[1].Action = null;
        board.Rows[1].Target = null;   // 대상만 남기면 target_not_allowed 등 혼선 방지
        var result = TacticBoardValidator.Validate(board, MakeResolver());
        CheckEq("H.action_missing", Has(result, TacticDiagnosticCodes.ActionMissing), true);
    }

    // ---- [I] catalog action empty ActionId ------------------------------------------------------
    private void TestActionIdEmpty()
    {
        GD.Print("[I] ActionId 빈 값");
        var board = MakeValidBoard();
        (board.Rows[1].Action as TacticCatalogActionDefinition).ActionId = "";
        var result = TacticBoardValidator.Validate(board, MakeResolver());
        CheckEq("I.action_id_empty", Has(result, TacticDiagnosticCodes.ActionIdEmpty), true);
    }

    // ---- [J] catalog 해석: unknown/duplicate/null template --------------------------------------
    private void TestCatalogUnknownDuplicateNullTemplate()
    {
        GD.Print("[J] catalog 해석 오류");

        // unknown
        var b1 = MakeValidBoard();
        (b1.Rows[1].Action as TacticCatalogActionDefinition).ActionId = "nonexistent";
        var r1 = TacticBoardValidator.Validate(b1, MakeResolver());
        CheckEq("J.action_unknown", Has(r1, TacticDiagnosticCodes.ActionUnknown), true);

        // duplicate: catalog에 default_attack이 둘
        var dupCatalog = MakeCatalog();
        dupCatalog.Entries.Add(new TacticActionCatalogEntry { ActionId = "default_attack", Template = new TurnAction_Attack() });
        var r2 = TacticBoardValidator.Validate(MakeValidBoard(), new TacticActionCatalogResolver(dupCatalog));
        CheckEq("J.action_duplicate", Has(r2, TacticDiagnosticCodes.ActionDuplicate), true);

        // null template
        var nullCatalog = MakeCatalog();
        nullCatalog.Entries.Add(new TacticActionCatalogEntry { ActionId = "null_tmpl", Template = null });
        var b3 = MakeValidBoard();
        (b3.Rows[1].Action as TacticCatalogActionDefinition).ActionId = "null_tmpl";
        var r3 = TacticBoardValidator.Validate(b3, new TacticActionCatalogResolver(nullCatalog));
        CheckEq("J.action_null_template", Has(r3, TacticDiagnosticCodes.ActionNullTemplate), true);
    }

    // ---- [K] resolver 없음 ----------------------------------------------------------------------
    private void TestResolverMissing()
    {
        GD.Print("[K] resolver 없음");
        var result = TacticBoardValidator.Validate(MakeValidBoard(), null);
        CheckEq("K.resolver_missing", Has(result, TacticDiagnosticCodes.ResolverMissing), true);
        CheckEq("K.invalid", result.IsValid, false);
    }

    // ---- [L] target_required / target_not_allowed -----------------------------------------------
    private void TestTargetRequiredAndNotAllowed()
    {
        GD.Print("[L] 대상 필요/불허");

        // catalog_action인데 대상 없음
        var b1 = MakeValidBoard();
        b1.Rows[1].Target = null;
        var r1 = TacticBoardValidator.Validate(b1, MakeResolver());
        CheckEq("L.target_required", Has(r1, TacticDiagnosticCodes.TargetRequired), true);

        // wait인데 대상 있음: 새 player row(wait + target)를 위에 추가
        var b2 = MakeValidBoard();
        var waitRow = new TacticRowDefinition
        {
            RowId = "r_wait_bad",
            Enabled = true,
            Conditions = new Array<TacticConditionDefinition> { new TacticAlwaysConditionDefinition() },
            Target = new TacticNearestEnemyTargetDefinition(),
            Action = new TacticWaitActionDefinition()
        };
        b2.Rows.Insert(0, waitRow);
        var r2 = TacticBoardValidator.Validate(b2, MakeResolver());
        CheckEq("L.target_not_allowed", Has(r2, TacticDiagnosticCodes.TargetNotAllowed), true);
    }

    // ---- [M] missing_target_context -------------------------------------------------------------
    private void TestMissingTargetContext()
    {
        GD.Print("[M] context_target 후보 공급 없음");

        // context_target인데 조건이 always뿐(후보 미공급) → Error
        var bad = MakeValidBoard();
        bad.Rows[0].Conditions = new Array<TacticConditionDefinition> { new TacticAlwaysConditionDefinition() };
        // Rows[0]는 context_target 대상, sonic_blow
        var rBad = TacticBoardValidator.Validate(bad, MakeResolver());
        CheckEq("M.missing_target_context", Has(rBad, TacticDiagnosticCodes.MissingTargetContext), true);

        // enemy_casting이 있으면 OK(유효 보드가 이미 그 경로) → 진단 없음
        var rOk = TacticBoardValidator.Validate(MakeValidBoard(), MakeResolver());
        CheckEq("M.context_ok_no_error", Has(rOk, TacticDiagnosticCodes.MissingTargetContext), false);
    }

    // ---- [N] system row 존재/순서 ---------------------------------------------------------------
    private void TestSystemRowPresenceAndOrder()
    {
        GD.Print("[N] system row 존재/순서");

        var noDefault = MakeValidBoard();
        noDefault.Rows.RemoveAt(2);   // default_attack 제거
        var rNoDefault = TacticBoardValidator.Validate(noDefault, MakeResolver());
        CheckEq("N.default_attack_missing", Has(rNoDefault, TacticDiagnosticCodes.DefaultAttackMissing), true);

        var noWait = MakeValidBoard();
        noWait.Rows.RemoveAt(3);      // terminal_wait 제거
        var rNoWait = TacticBoardValidator.Validate(noWait, MakeResolver());
        CheckEq("N.terminal_wait_missing", Has(rNoWait, TacticDiagnosticCodes.TerminalWaitMissing), true);

        var notLast = MakeValidBoard();
        notLast.Rows.Add(new TacticRowDefinition
        {
            RowId = "r_after",
            Enabled = true,
            Conditions = new Array<TacticConditionDefinition> { new TacticAlwaysConditionDefinition() },
            Target = new TacticNearestEnemyTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "default_attack" }
        });
        var rNotLast = TacticBoardValidator.Validate(notLast, MakeResolver());
        CheckEq("N.terminal_wait_not_last", Has(rNotLast, TacticDiagnosticCodes.TerminalWaitNotLast), true);
        CheckEq("N.player_row_below_system", Has(rNotLast, TacticDiagnosticCodes.PlayerRowBelowSystem), true);

        var dupDefault = MakeValidBoard();
        dupDefault.Rows.Insert(2, MakeDefaultAttackRow());
        var rDupDefault = TacticBoardValidator.Validate(dupDefault, MakeResolver());
        CheckEq("N.default_attack_duplicate", Has(rDupDefault, TacticDiagnosticCodes.DefaultAttackDuplicate), true);

        // 두 system row를 모두 제거해도 복구 가능한 missing 진단을 함께 내야 한다(int.MaxValue+1 overflow 회귀).
        var noSystem = MakeValidBoard();
        noSystem.Rows.RemoveAt(3);   // terminal_wait
        noSystem.Rows.RemoveAt(2);   // default_attack
        var rNoSystem = TacticBoardValidator.Validate(noSystem, MakeResolver());
        CheckEq("N.both_default_missing", Has(rNoSystem, TacticDiagnosticCodes.DefaultAttackMissing), true);
        CheckEq("N.both_wait_missing", Has(rNoSystem, TacticDiagnosticCodes.TerminalWaitMissing), true);
        CheckEq("N.both_no_internal_error", Has(rNoSystem, TacticDiagnosticCodes.ValidatorInternalError), false);
        CheckEq("N.both_invalid", rNoSystem.IsValid, false);
    }

    // ---- [O] system row 내용 손상 ---------------------------------------------------------------
    private void TestSystemRowMalformed()
    {
        GD.Print("[O] system row 손상");

        var badDefault = MakeValidBoard();
        badDefault.Rows[2].ApproachPolicy = TacticApproachPolicy.Immediate;   // 계약 위반
        var rBadDefault = TacticBoardValidator.Validate(badDefault, MakeResolver());
        CheckEq("O.default_attack_malformed", Has(rBadDefault, TacticDiagnosticCodes.DefaultAttackMalformed), true);

        var badWait = MakeValidBoard();
        badWait.Rows[3].Target = new TacticNearestEnemyTargetDefinition();    // 대기에 대상
        var rBadWait = TacticBoardValidator.Validate(badWait, MakeResolver());
        CheckEq("O.terminal_wait_malformed", Has(rBadWait, TacticDiagnosticCodes.TerminalWaitMalformed), true);
    }

    // ---- [O2] system row 손상 변형(리뷰 P1) -----------------------------------------------------
    private void TestSystemRowCorruptionVariants()
    {
        GD.Print("[O2] system row 손상 변형");

        // default_attack Enabled=false → 하한 소실
        var b1 = MakeValidBoard();
        b1.Rows[2].Enabled = false;
        var r1 = TacticBoardValidator.Validate(b1, MakeResolver());
        CheckEq("O2.default_disabled_malformed", Has(r1, TacticDiagnosticCodes.DefaultAttackMalformed), true);
        CheckEq("O2.default_disabled_invalid", r1.IsValid, false);

        // terminal_wait Enabled=false
        var b2 = MakeValidBoard();
        b2.Rows[3].Enabled = false;
        var r2 = TacticBoardValidator.Validate(b2, MakeResolver());
        CheckEq("O2.wait_disabled_malformed", Has(r2, TacticDiagnosticCodes.TerminalWaitMalformed), true);

        // default_attack 조건에 enemy_casting 추가 → 기본 공격 대상 제한(정확히 [any_enemy]가 아님)
        var b3 = MakeValidBoard();
        b3.Rows[2].Conditions = new Array<TacticConditionDefinition>
        {
            new TacticAnyEnemyConditionDefinition(),
            new TacticEnemyCastingConditionDefinition()
        };
        var r3 = TacticBoardValidator.Validate(b3, MakeResolver());
        CheckEq("O2.default_extra_condition_malformed", Has(r3, TacticDiagnosticCodes.DefaultAttackMalformed), true);

        // Source=Player, Locked=true 행을 system row 아래에 삽입 → player_row_below_system 우회 불가
        var b4 = MakeValidBoard();
        b4.Rows.Insert(3, new TacticRowDefinition
        {
            RowId = "r_sneaky",
            Enabled = true,
            Locked = true,                     // Locked로 위장
            Source = TacticRowSource.Player,
            Conditions = new Array<TacticConditionDefinition> { new TacticAlwaysConditionDefinition() },
            Target = new TacticNearestEnemyTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "default_attack" }
        });
        var r4 = TacticBoardValidator.Validate(b4, MakeResolver());
        CheckEq("O2.locked_player_below_system", Has(r4, TacticDiagnosticCodes.PlayerRowBelowSystem), true);
        CheckEq("O2.locked_player_invalid", r4.IsValid, false);
    }

    // ---- [O3] 최대 행 수(리뷰 P1, 기획 H-5 = 8) -------------------------------------------------
    private void TestMaxRowsBoundary()
    {
        GD.Print("[O3] 최대 8행");

        var eight = MakeBoardWithExtraRows(4);   // 4 base + 4 extra = 8
        CheckEq("O3.eight_rows_count", eight.Rows.Count, 8);
        var rEight = TacticBoardValidator.Validate(eight, MakeResolver());
        CheckEq("O3.eight_no_too_many", Has(rEight, TacticDiagnosticCodes.TooManyRows), false);

        var nine = MakeBoardWithExtraRows(5);    // 4 base + 5 extra = 9
        CheckEq("O3.nine_rows_count", nine.Rows.Count, 9);
        var rNine = TacticBoardValidator.Validate(nine, MakeResolver());
        CheckEq("O3.nine_too_many", Has(rNine, TacticDiagnosticCodes.TooManyRows), true);
        CheckEq("O3.nine_invalid", rNine.IsValid, false);
    }

    // ---- [O4] 같은 field/code 진단 tie-break(리뷰 P2) -------------------------------------------
    private void TestSameFieldCodeTieBreak()
    {
        GD.Print("[O4] tie-break 결정론");
        var board = MakeValidBoard();
        board.Rows[1].Conditions = new Array<TacticConditionDefinition> { null, null };   // condition_null 2건

        var fieldPaths = new List<string>();
        var r = TacticBoardValidator.Validate(board, MakeResolver());
        foreach (var d in r.Diagnostics)
            if (d.Code.ToString() == TacticDiagnosticCodes.ConditionNull.ToString())
                fieldPaths.Add(d.FieldPath);

        CheckEq("O4.two_condition_null", fieldPaths.Count, 2);
        CheckEq("O4.order_conditions0_first", fieldPaths.Count == 2 && fieldPaths[0] == "Conditions[0]", true);
        CheckEq("O4.order_conditions1_second", fieldPaths.Count == 2 && fieldPaths[1] == "Conditions[1]", true);
    }

    // ---- [O5] 결과 타입 방어 복사(리뷰 P2) ------------------------------------------------------
    private void TestResultDefensiveCopy()
    {
        GD.Print("[O5] 결과 방어 복사");
        var list = new List<TacticDiagnostic>
        {
            new(TacticDiagnosticSeverity.Error, "x")
        };
        var result = new TacticValidationResult(false, list);
        list.Add(new TacticDiagnostic(TacticDiagnosticSeverity.Error, "y"));   // 생성 후 원본 목록 변형
        CheckEq("O5.result_unaffected", result.Diagnostics.Count, 1);
    }

    // ---- [P] 경고는 성공을 막지 않는다 ----------------------------------------------------------
    private void TestWarningsAllowSuccess()
    {
        GD.Print("[P] warning-only 보드");

        // 중복 활성 행: r_nearest를 그대로 복제해 위에 추가.
        var dup = MakeValidBoard();
        dup.Rows.Insert(1, MakeNearestAttackRow("r_dup"));  // r_nearest와 동일 signature
        // r_nearest도 같은 signature라 두 번째가 중복으로 잡힌다.
        dup.Rows.Insert(2, MakeNearestAttackRow("r_dup2"));
        var rDup = TacticBoardValidator.Validate(dup, MakeResolver());
        CheckEq("P.duplicate_active_row", Has(rDup, TacticDiagnosticCodes.DuplicateActiveRow), true);
        CheckEq("P.duplicate_still_valid", rDup.IsValid, true);

        // 무조건 wait 상위 행 → 아래 가림 경고.
        var shadow = MakeValidBoard();
        shadow.Rows.Insert(0, new TacticRowDefinition
        {
            RowId = "r_wait_top",
            Enabled = true,
            Conditions = new Array<TacticConditionDefinition>(),
            Target = null,
            Action = new TacticWaitActionDefinition()
        });
        var rShadow = TacticBoardValidator.Validate(shadow, MakeResolver());
        CheckEq("P.unreachable_after_unconditional", Has(rShadow, TacticDiagnosticCodes.UnreachableAfterUnconditional), true);
        CheckEq("P.shadow_still_valid", rShadow.IsValid, true);
    }

    // ---- [Q] 결정론 순서 + board-level 우선 -----------------------------------------------------
    private void TestDeterministicOrderingAndBoardBeforeRow()
    {
        GD.Print("[Q] 결정론 순서");
        var board = MakeValidBoard();
        board.BoardId = "";                                   // board-level error
        board.Rows[1].Conditions = new Array<TacticConditionDefinition> { new TestUnknownConditionDefinition() }; // row error

        var r1 = TacticBoardValidator.Validate(board, MakeResolver());
        var r2 = TacticBoardValidator.Validate(board, MakeResolver());
        string codes1 = string.Join(",", r1.Diagnostics.Select(d => d.Code.ToString()));
        string codes2 = string.Join(",", r2.Diagnostics.Select(d => d.Code.ToString()));
        CheckEq("Q.deterministic", codes1, codes2);

        // board-level(board_id_empty)이 row-level(condition_unknown_type)보다 먼저.
        int boardIdx = IndexOf(r1, TacticDiagnosticCodes.BoardIdEmpty);
        int rowIdx = IndexOf(r1, TacticDiagnosticCodes.ConditionUnknownType);
        CheckEq("Q.board_before_row", boardIdx >= 0 && rowIdx >= 0 && boardIdx < rowIdx, true);
    }

    // ---- [R] validator는 입력을 변형하지 않는다 ------------------------------------------------
    private void TestValidatorDoesNotMutateInputs()
    {
        GD.Print("[R] 입력 불변");
        var board = MakeValidBoard();
        int rowsBefore = board.Rows.Count;
        int condsBefore = board.Rows[0].Conditions.Count;
        string idBefore = board.Rows[0].RowId.ToString();

        TacticBoardValidator.Validate(board, MakeResolver());

        CheckEq("R.rows_unchanged", board.Rows.Count, rowsBefore);
        CheckEq("R.conds_unchanged", board.Rows[0].Conditions.Count, condsBefore);
        CheckEq("R.rowid_unchanged", board.Rows[0].RowId.ToString(), idBefore);

        // 반복 호출 결과가 동일(반환 컬렉션 변형 무영향).
        var a = TacticBoardValidator.Validate(board, MakeResolver());
        var b = TacticBoardValidator.Validate(board, MakeResolver());
        CheckEq("R.repeat_same_count", a.Diagnostics.Count, b.Diagnostics.Count);
    }

    // ---- [S] 두 유닛 resolver 차이 --------------------------------------------------------------
    private void TestTwoResolversDiffer()
    {
        GD.Print("[S] resolver 차이");
        var board = MakeValidBoard();
        var full = TacticBoardValidator.Validate(board, MakeResolver());
        CheckEq("S.full_valid", full.IsValid, true);

        // 빈 catalog resolver → catalog_action이 해석되지 않는다.
        var empty = TacticBoardValidator.Validate(board, new TacticActionCatalogResolver(new TacticActionCatalog()));
        CheckEq("S.empty_unknown", Has(empty, TacticDiagnosticCodes.ActionUnknown), true);
        CheckEq("S.empty_invalid", empty.IsValid, false);
    }

    // ---- 빌드 헬퍼 -------------------------------------------------------------------------------

    private static TacticBoardDefinition MakeValidBoard()
    {
        var board = new TacticBoardDefinition { SchemaVersion = 1, BoardId = "board.pc", DisplayName = "보드" };
        // r_cast: [enemy_casting] -> context_target -> sonic_blow, Immediate
        board.Rows.Add(new TacticRowDefinition
        {
            RowId = "r_cast",
            Enabled = true,
            Conditions = new Array<TacticConditionDefinition> { new TacticEnemyCastingConditionDefinition() },
            Target = new TacticContextTargetDefinition(),
            Action = new TacticCatalogActionDefinition { ActionId = "sonic_blow" },
            ApproachPolicy = TacticApproachPolicy.Immediate
        });
        // r_nearest: [always] -> nearest_enemy -> default_attack, ApproachAllowed
        board.Rows.Add(MakeNearestAttackRow("r_nearest"));
        // 잠긴 system rows
        board.Rows.Add(MakeDefaultAttackRow());
        board.Rows.Add(MakeTerminalWaitRow());
        return board;
    }

    // base 4행에 nearest-attack player 행을 extra개 system row 앞에 삽입한 보드(행 수 경계 테스트용).
    private static TacticBoardDefinition MakeBoardWithExtraRows(int extra)
    {
        var board = MakeValidBoard();
        for (int i = 0; i < extra; i++)
            board.Rows.Insert(2, MakeNearestAttackRow($"r_extra_{i}"));   // system row(idx 2) 앞에 삽입
        return board;
    }

    private static TacticRowDefinition MakeNearestAttackRow(string rowId) => new()
    {
        RowId = rowId,
        Enabled = true,
        Conditions = new Array<TacticConditionDefinition> { new TacticAlwaysConditionDefinition() },
        Target = new TacticNearestEnemyTargetDefinition(),
        Action = new TacticCatalogActionDefinition { ActionId = "default_attack" },
        ApproachPolicy = TacticApproachPolicy.ApproachAllowed
    };

    private static TacticRowDefinition MakeDefaultAttackRow() => new()
    {
        RowId = "default_attack",
        Enabled = true,
        Locked = true,
        Source = TacticRowSource.Default,
        Conditions = new Array<TacticConditionDefinition> { new TacticAnyEnemyConditionDefinition() },
        Target = new TacticNearestEnemyTargetDefinition(),
        Action = new TacticCatalogActionDefinition { ActionId = "default_attack" },
        ApproachPolicy = TacticApproachPolicy.ApproachAllowed
    };

    private static TacticRowDefinition MakeTerminalWaitRow() => new()
    {
        RowId = "terminal_wait",
        Enabled = true,
        Locked = true,
        Source = TacticRowSource.Default,
        Conditions = new Array<TacticConditionDefinition>(),
        Target = null,
        Action = new TacticWaitActionDefinition()
    };

    private static TacticActionCatalog MakeCatalog()
    {
        return new TacticActionCatalog
        {
            CatalogRevision = "rev-1",
            Entries = new Array<TacticActionCatalogEntry>
            {
                new() { ActionId = "sonic_blow", Template = new TurnAction_Attack() },
                new() { ActionId = "default_attack", Template = new TurnAction_Attack() }
            }
        };
    }

    private static ITacticActionResolver MakeResolver() => new TacticActionCatalogResolver(MakeCatalog());

    // ---- 단언 헬퍼 -------------------------------------------------------------------------------

    private static bool Has(TacticValidationResult result, StringName code)
        => result.Diagnostics.Any(d => d.Code.ToString() == code.ToString());

    private static int IndexOf(TacticValidationResult result, StringName code)
    {
        for (int i = 0; i < result.Diagnostics.Count; i++)
            if (result.Diagnostics[i].Code.ToString() == code.ToString()) return i;
        return -1;
    }

    private void CheckEq<T>(string name, T actual, T expected)
    {
        if (EqualityComparer<T>.Default.Equals(actual, expected)) GD.Print($"  PASS: {name}");
        else { _failures++; GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}"); }
    }
}

// unknown TypeId를 내는 test 대역(제품 코드 아님). 직렬화하지 않으므로 [GlobalClass] 불필요.
public partial class TestUnknownConditionDefinition : TacticConditionDefinition
{
    public override StringName TypeId => "bogus";
}
#endif
