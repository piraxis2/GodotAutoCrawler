using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Validation;

// 택틱보드 draft를 변경 없이 검사해 구조화된 진단을 내는 pure/read-only validator(BT-003 Step 2, ADR-024 §4/§5/
// §11/§12, 기획 H-7). draft·resolver·catalog·template을 변형하지 않으며, Node 생성/compile은 하지 않는다(Step 3).
//
// 진단 순서는 board-level -> row order -> field order -> code로 안정화한다(ADR-024 §4). Dictionary/Resource
// 순회 순서에 의존하지 않도록, 수집한 진단을 (rowIndex, fieldRank, code) 키로 정렬한 뒤 반환한다.
public static class TacticBoardValidator
{
    // v1 어휘 registry. compiler가 아는 안정 TypeId만 통과시킨다(ADR-024 §2). 모르는 TypeId는 unknown_type Error.
    private static readonly HashSet<string> KnownConditions = new() { "always", "any_enemy", "enemy_casting" };
    // 후보를 공급하는 조건(ADR-024 §11). always는 gate라 후보를 만들지 않는다.
    private static readonly HashSet<string> TargetBearingConditions = new() { "any_enemy", "enemy_casting" };
    private static readonly HashSet<string> KnownTargets = new() { "nearest_enemy", "context_target" };
    private static readonly HashSet<string> KnownActions = new() { "wait", "catalog_action" };

    private const string DefaultAttackRowId = "default_attack";
    private const string TerminalWaitRowId = "terminal_wait";

    // v1 한계.
    private const int MaxRows = 8;               // 기획 H-5 "최대 8행(기본 행 포함)". 확장(숙련/장신구 +행)은 후속.
    private const int MaxConditionsPerRow = 2;   // 기획 H-8 "TacticCondition ×0~2".

    // 정렬용 필드 rank(같은 scope 안의 순서).
    private const int FieldBoardSchema = 0;
    private const int FieldBoardId = 1;
    private const int FieldBoardRows = 2;
    private const int FieldBoardDefaultAttack = 10;
    private const int FieldBoardTerminalWait = 11;

    private const int FieldRowId = 0;
    private const int FieldRowConditions = 1;
    private const int FieldRowTarget = 2;
    private const int FieldRowAction = 3;
    private const int FieldRowCompat = 4;
    private const int FieldRowStructure = 6;
    private const int FieldRowWarning = 7;

    private const int BoardScope = -1;

    private readonly struct Entry
    {
        public readonly int Row;
        public readonly int Field;
        public readonly TacticDiagnostic Diagnostic;

        public Entry(int row, int field, TacticDiagnostic diagnostic)
        {
            Row = row;
            Field = field;
            Diagnostic = diagnostic;
        }
    }

    public static TacticValidationResult Validate(TacticBoardDefinition board, ITacticActionResolver resolver)
    {
        var work = new List<Entry>();
        try
        {
            if (board == null)
            {
                Add(work, BoardScope, FieldBoardSchema, Error(TacticDiagnosticCodes.BoardNull));
                return Finish(work);
            }

            ValidateBoardScalars(board, work);

            IReadOnlyList<TacticRowDefinition> rows =
                (IReadOnlyList<TacticRowDefinition>)board.Rows ?? Array.Empty<TacticRowDefinition>();

            if (rows.Count > MaxRows)
            {
                Add(work, BoardScope, FieldBoardRows,
                    Error(TacticDiagnosticCodes.TooManyRows, args: new[] { rows.Count.ToString(), MaxRows.ToString() }));
            }

            var seenRowIds = new HashSet<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                ValidateRow(i, rows[i], resolver, seenRowIds, work);
            }

            ValidateSystemRows(rows, work);
            ValidateWarnings(rows, work);

            return Finish(work);
        }
        catch (Exception ex)
        {
            // validator 예외는 격리한다(ADR-024 §5). 예외로 전투 준비 흐름을 죽이지 않고 Error 진단으로 변환한다.
            var isolated = new List<Entry>
            {
                new(BoardScope, FieldBoardSchema,
                    Error(TacticDiagnosticCodes.ValidatorInternalError, developerDetail: ex.ToString()))
            };
            return Finish(isolated);
        }
    }

    private static void ValidateBoardScalars(TacticBoardDefinition board, List<Entry> work)
    {
        if (board.SchemaVersion != 1)
        {
            Add(work, BoardScope, FieldBoardSchema, Error(TacticDiagnosticCodes.UnsupportedSchemaVersion,
                args: new[] { board.SchemaVersion.ToString() }));
        }

        if (string.IsNullOrEmpty(board.BoardId.ToString()))
        {
            Add(work, BoardScope, FieldBoardId, Error(TacticDiagnosticCodes.BoardIdEmpty));
        }
    }

    private static void ValidateRow(int index, TacticRowDefinition row, ITacticActionResolver resolver,
        HashSet<string> seenRowIds, List<Entry> work)
    {
        if (row == null)
        {
            Add(work, index, FieldRowId, Error(TacticDiagnosticCodes.RowNull));
            return;
        }

        StringName rowId = row.RowId;
        string rowIdText = rowId.ToString();

        // identity
        if (string.IsNullOrEmpty(rowIdText))
        {
            Add(work, index, FieldRowId, Error(TacticDiagnosticCodes.RowIdEmpty, rowId));
        }
        else if (!seenRowIds.Add(rowIdText))
        {
            Add(work, index, FieldRowId, Error(TacticDiagnosticCodes.RowIdDuplicate, rowId, args: new[] { rowIdText }));
        }

        // conditions
        IReadOnlyList<TacticConditionDefinition> conditions =
            (IReadOnlyList<TacticConditionDefinition>)row.Conditions ?? Array.Empty<TacticConditionDefinition>();
        if (conditions.Count > MaxConditionsPerRow)
        {
            Add(work, index, FieldRowConditions, Error(TacticDiagnosticCodes.TooManyConditions, rowId,
                fieldPath: "Conditions", args: new[] { conditions.Count.ToString(), MaxConditionsPerRow.ToString() }));
        }
        for (int c = 0; c < conditions.Count; c++)
        {
            TacticConditionDefinition cond = conditions[c];
            if (cond == null)
            {
                Add(work, index, FieldRowConditions,
                    Error(TacticDiagnosticCodes.ConditionNull, rowId, fieldPath: $"Conditions[{c}]"));
            }
            else if (!KnownConditions.Contains(cond.TypeId.ToString()))
            {
                Add(work, index, FieldRowConditions, Error(TacticDiagnosticCodes.ConditionUnknownType, rowId,
                    fieldPath: $"Conditions[{c}]", args: new[] { cond.TypeId.ToString() }));
            }
        }

        // target type
        if (row.Target != null && !KnownTargets.Contains(row.Target.TypeId.ToString()))
        {
            Add(work, index, FieldRowTarget, Error(TacticDiagnosticCodes.TargetUnknownType, rowId,
                fieldPath: "Target", args: new[] { row.Target.TypeId.ToString() }));
        }

        // action + catalog 해석
        string actionType = ValidateAction(index, row, resolver, work);

        // target/action 호환
        ValidateTargetActionCompatibility(index, row, actionType, conditions, work);
    }

    // 행동 검사. 알 수 있으면 action TypeId 문자열을 반환한다(호환 검사에 씀). 없거나 unknown이면 null.
    private static string ValidateAction(int index, TacticRowDefinition row, ITacticActionResolver resolver,
        List<Entry> work)
    {
        StringName rowId = row.RowId;
        if (row.Action == null)
        {
            Add(work, index, FieldRowAction, Error(TacticDiagnosticCodes.ActionMissing, rowId, fieldPath: "Action"));
            return null;
        }

        string actionType = row.Action.TypeId.ToString();
        if (!KnownActions.Contains(actionType))
        {
            Add(work, index, FieldRowAction, Error(TacticDiagnosticCodes.ActionUnknownType, rowId,
                fieldPath: "Action", args: new[] { actionType }));
            return null;
        }

        if (actionType == "catalog_action")
        {
            var catalogAction = row.Action as TacticCatalogActionDefinition;
            string actionId = catalogAction?.ActionId.ToString() ?? "";
            if (string.IsNullOrEmpty(actionId))
            {
                Add(work, index, FieldRowAction,
                    Error(TacticDiagnosticCodes.ActionIdEmpty, rowId, fieldPath: "Action.ActionId"));
            }
            else if (resolver == null)
            {
                Add(work, index, FieldRowAction, Error(TacticDiagnosticCodes.ResolverMissing, rowId,
                    fieldPath: "Action.ActionId", args: new[] { actionId }));
            }
            else
            {
                switch (resolver.Resolve(catalogAction.ActionId))
                {
                    case TacticActionResolution.Unknown:
                        Add(work, index, FieldRowAction, Error(TacticDiagnosticCodes.ActionUnknown, rowId,
                            fieldPath: "Action.ActionId", args: new[] { actionId }));
                        break;
                    case TacticActionResolution.Duplicate:
                        Add(work, index, FieldRowAction, Error(TacticDiagnosticCodes.ActionDuplicate, rowId,
                            fieldPath: "Action.ActionId", args: new[] { actionId }));
                        break;
                    case TacticActionResolution.NullTemplate:
                        Add(work, index, FieldRowAction, Error(TacticDiagnosticCodes.ActionNullTemplate, rowId,
                            fieldPath: "Action.ActionId", args: new[] { actionId }));
                        break;
                }
            }
        }

        return actionType;
    }

    private static void ValidateTargetActionCompatibility(int index, TacticRowDefinition row, string actionType,
        IReadOnlyList<TacticConditionDefinition> conditions, List<Entry> work)
    {
        if (actionType == null) return;   // 행동 자체가 없거나 unknown이면 호환 검사 생략(이미 Error).
        StringName rowId = row.RowId;

        bool hasTarget = row.Target != null;

        // 무대상 행동(wait)에 대상 셀렉터가 붙으면 잘못된 구조.
        if (actionType == "wait" && hasTarget)
        {
            Add(work, index, FieldRowCompat,
                Error(TacticDiagnosticCodes.TargetNotAllowed, rowId, fieldPath: "Target"));
        }

        // 대상이 필요한 행동(catalog_action)에 대상 셀렉터가 없으면 불성립.
        if (actionType == "catalog_action" && !hasTarget)
        {
            Add(work, index, FieldRowCompat,
                Error(TacticDiagnosticCodes.TargetRequired, rowId, fieldPath: "Target"));
        }

        // ContextTarget은 후보 공급 조건(any_enemy/enemy_casting)이 하나 이상 있어야 한다(ADR-024 §11).
        // NearestEnemy는 compiler가 AnyEnemy를 자동 공급하므로 draft에 요구하지 않는다.
        if (hasTarget && row.Target.TypeId.ToString() == "context_target")
        {
            bool hasSupplier = conditions.Any(c => c != null && TargetBearingConditions.Contains(c.TypeId.ToString()));
            if (!hasSupplier)
            {
                Add(work, index, FieldRowCompat,
                    Error(TacticDiagnosticCodes.MissingTargetContext, rowId, fieldPath: "Target"));
            }
        }
    }

    // 잠긴 기본 system row 계약(ADR-024 §11, 기획 H-7-2). default_attack/terminal_wait의 존재·중복·위치·내용을
    // 검사한다. compiler가 몰래 합성하지 않으므로 draft에 없거나 손상되면 blocking Error다.
    private static void ValidateSystemRows(IReadOnlyList<TacticRowDefinition> rows, List<Entry> work)
    {
        var defaultAttackIdx = new List<int>();
        var terminalWaitIdx = new List<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            string id = rows[i]?.RowId.ToString() ?? "";
            if (id == DefaultAttackRowId) defaultAttackIdx.Add(i);
            else if (id == TerminalWaitRowId) terminalWaitIdx.Add(i);
        }

        // 존재/중복
        if (defaultAttackIdx.Count == 0)
            Add(work, BoardScope, FieldBoardDefaultAttack, Error(TacticDiagnosticCodes.DefaultAttackMissing));
        else if (defaultAttackIdx.Count > 1)
            Add(work, BoardScope, FieldBoardDefaultAttack, Error(TacticDiagnosticCodes.DefaultAttackDuplicate));

        if (terminalWaitIdx.Count == 0)
            Add(work, BoardScope, FieldBoardTerminalWait, Error(TacticDiagnosticCodes.TerminalWaitMissing));
        else if (terminalWaitIdx.Count > 1)
            Add(work, BoardScope, FieldBoardTerminalWait, Error(TacticDiagnosticCodes.TerminalWaitDuplicate));

        // terminal_wait는 마지막 행이어야 한다.
        if (terminalWaitIdx.Count == 1 && terminalWaitIdx[0] != rows.Count - 1)
            Add(work, BoardScope, FieldBoardTerminalWait, Error(TacticDiagnosticCodes.TerminalWaitNotLast));

        // 첫 system row 아래에는 두 system row 외에 어떤 행도 있으면 안 된다(ADR-024 §11, 기획 H-3-3 안전 하한).
        // Locked/Source로 위장한 행도 막기 위해 RowId identity로만 예외를 둔다(Locked=true, Source=Player 우회 차단).
        // 두 system row가 모두 없으면(=int.MaxValue) 아래 검사를 건너뛴다 — `+1` 정수 overflow 방지(missing 진단이
        // validator_internal_error로 덮이지 않도록).
        var systemIndices = defaultAttackIdx.Concat(terminalWaitIdx).ToList();
        if (systemIndices.Count > 0)
        {
            int firstSystemIdx = systemIndices.Min();
            for (int i = firstSystemIdx + 1; i < rows.Count; i++)
            {
                TacticRowDefinition row = rows[i];
                if (row == null) continue;
                string id = row.RowId.ToString();
                if (id == DefaultAttackRowId || id == TerminalWaitRowId) continue;   // 두 system row만 허용
                Add(work, i, FieldRowStructure, Error(TacticDiagnosticCodes.PlayerRowBelowSystem, row.RowId));
            }
        }

        // 내용 계약
        if (defaultAttackIdx.Count == 1) ValidateDefaultAttackContent(defaultAttackIdx[0], rows[defaultAttackIdx[0]], work);
        if (terminalWaitIdx.Count == 1) ValidateTerminalWaitContent(terminalWaitIdx[0], rows[terminalWaitIdx[0]], work);
    }

    // default_attack = [any_enemy] -> nearest_enemy(ApproachAllowed) -> catalog_action("default_attack"),
    // Enabled/Locked/Default. 조건은 정확히 [any_enemy] 하나여야 한다 — enemy_casting 등을 추가하면 기본 공격이
    // 특정 적으로 제한돼 "어떤 상황에서도 아무것도 안 함 방지" 하한이 깨진다(기획 H-3-3).
    private static void ValidateDefaultAttackContent(int index, TacticRowDefinition row, List<Entry> work)
    {
        StringName rowId = row.RowId;
        void Malformed(string field) =>
            Add(work, index, FieldRowStructure, Error(TacticDiagnosticCodes.DefaultAttackMalformed, rowId, fieldPath: field));

        if (!row.Enabled) Malformed("Enabled");
        if (!row.Locked) Malformed("Locked");
        if (row.Source != TacticRowSource.Default) Malformed("Source");

        bool condsExactlyAnyEnemy = row.Conditions != null && row.Conditions.Count == 1 &&
            row.Conditions[0] != null && row.Conditions[0].TypeId.ToString() == "any_enemy";
        if (!condsExactlyAnyEnemy) Malformed("Conditions");

        if (row.Target == null || row.Target.TypeId.ToString() != "nearest_enemy") Malformed("Target");
        if (row.ApproachPolicy != TacticApproachPolicy.ApproachAllowed) Malformed("ApproachPolicy");

        string actionId = (row.Action as TacticCatalogActionDefinition)?.ActionId.ToString();
        if (row.Action?.TypeId.ToString() != "catalog_action" || actionId != DefaultAttackRowId) Malformed("Action");
    }

    // terminal_wait = 무조건 wait, 대상/조건 없음, Enabled/Locked/Default. Enabled=false면 하한이 사라진다.
    private static void ValidateTerminalWaitContent(int index, TacticRowDefinition row, List<Entry> work)
    {
        StringName rowId = row.RowId;
        void Malformed(string field) =>
            Add(work, index, FieldRowStructure, Error(TacticDiagnosticCodes.TerminalWaitMalformed, rowId, fieldPath: field));

        if (!row.Enabled) Malformed("Enabled");
        if (!row.Locked) Malformed("Locked");
        if (row.Source != TacticRowSource.Default) Malformed("Source");
        if (row.Action?.TypeId.ToString() != "wait") Malformed("Action");
        if (row.Target != null) Malformed("Target");
        if (row.Conditions != null && row.Conditions.Count > 0) Malformed("Conditions");
    }

    // 정적으로 확실히 증명 가능한 warning만 낸다(ADR-024 Warning Policy, 기획 H-7-3).
    private static void ValidateWarnings(IReadOnlyList<TacticRowDefinition> rows, List<Entry> work)
    {
        var seenSignatures = new HashSet<string>();
        int firstUnconditionalIdx = -1;

        for (int i = 0; i < rows.Count; i++)
        {
            TacticRowDefinition row = rows[i];
            if (row == null || !row.Enabled) continue;

            // 완전히 동일한 활성 행 중복.
            string signature = RowSignature(row);
            if (!seenSignatures.Add(signature))
            {
                Add(work, i, FieldRowWarning, Warning(TacticDiagnosticCodes.DuplicateActiveRow, row.RowId));
            }

            // 무조건 성립하는 상위 행이 아래 행을 확실히 가림. 증명 가능한 경우만: (조건 없음 또는 전부 always) +
            // 무대상 wait(항상 완료). catalog_action은 대상/사거리 실패 가능성이 있어 가림을 단정하지 않는다.
            if (firstUnconditionalIdx >= 0 && i > firstUnconditionalIdx)
            {
                Add(work, i, FieldRowWarning,
                    Warning(TacticDiagnosticCodes.UnreachableAfterUnconditional, row.RowId));
            }
            else if (firstUnconditionalIdx < 0 && IsUnconditionalSuccess(row))
            {
                firstUnconditionalIdx = i;
            }
        }
    }

    private static bool IsUnconditionalSuccess(TacticRowDefinition row)
    {
        bool conditionsAlwaysTrue = row.Conditions == null || row.Conditions.Count == 0 ||
            row.Conditions.All(c => c != null && c.TypeId.ToString() == "always");
        bool targetlessWait = row.Action?.TypeId.ToString() == "wait";
        return conditionsAlwaysTrue && targetlessWait;
    }

    private static string RowSignature(TacticRowDefinition row)
    {
        IEnumerable<string> condTypes = (row.Conditions ?? new())
            .Where(c => c != null)
            .Select(c => c.TypeId.ToString())
            .OrderBy(s => s, StringComparer.Ordinal);
        string conds = string.Join(",", condTypes);
        string target = row.Target?.TypeId.ToString() ?? "<none>";
        string action = row.Action?.TypeId.ToString() ?? "<none>";
        string actionId = (row.Action as TacticCatalogActionDefinition)?.ActionId.ToString() ?? "";
        return $"{conds}|{target}|{action}:{actionId}|{(int)row.ApproachPolicy}";
    }

    // --- helpers ---

    private static void Add(List<Entry> work, int row, int field, TacticDiagnostic diagnostic)
        => work.Add(new Entry(row, field, diagnostic));

    private static TacticDiagnostic Error(StringName code, StringName rowId = null, string fieldPath = null,
        string[] args = null, string developerDetail = null)
        => new(TacticDiagnosticSeverity.Error, code, rowId, fieldPath, args, developerDetail);

    private static TacticDiagnostic Warning(StringName code, StringName rowId = null, string fieldPath = null,
        string[] args = null)
        => new(TacticDiagnosticSeverity.Warning, code, rowId, fieldPath, args);

    private static TacticValidationResult Finish(List<Entry> work)
    {
        // board-level -> row order -> field order -> code -> FieldPath -> args(ordinal). Dictionary/Resource 순회
        // 순서 비의존(ADR-024 §4). FieldPath 동률 키가 같은 code의 Conditions[0]/Conditions[1] 순서를 고정한다.
        work.Sort((a, b) =>
        {
            int r = a.Row.CompareTo(b.Row);
            if (r != 0) return r;
            int f = a.Field.CompareTo(b.Field);
            if (f != 0) return f;
            int c = string.CompareOrdinal(a.Diagnostic.Code.ToString(), b.Diagnostic.Code.ToString());
            if (c != 0) return c;
            int p = string.CompareOrdinal(a.Diagnostic.FieldPath ?? "", b.Diagnostic.FieldPath ?? "");
            if (p != 0) return p;
            return string.CompareOrdinal(
                string.Join(",", a.Diagnostic.MessageArgs), string.Join(",", b.Diagnostic.MessageArgs));
        });

        var diagnostics = work.Select(e => e.Diagnostic).ToList();
        bool isValid = diagnostics.All(d => d.Severity != TacticDiagnosticSeverity.Error);
        return new TacticValidationResult(isValid, diagnostics.AsReadOnly());
    }
}
