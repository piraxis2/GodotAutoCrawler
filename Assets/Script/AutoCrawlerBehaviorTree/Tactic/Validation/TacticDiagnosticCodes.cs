using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Validation;

// 안정 진단 code(ADR-024 §4, 기획 H-7). UI 라우팅·로컬라이즈·테스트가 문자열이 아니라 이 상수를 참조해
// code가 바뀌면 한 곳만 고친다. 값(문자열)은 저장/로그/테스트에 남으므로 rename하지 않는다.
public static class TacticDiagnosticCodes
{
    // --- board-level ---
    public static readonly StringName BoardNull = "board_null";
    public static readonly StringName UnsupportedSchemaVersion = "unsupported_schema_version";
    public static readonly StringName BoardIdEmpty = "board_id_empty";
    public static readonly StringName TooManyRows = "too_many_rows";

    // --- row identity ---
    public static readonly StringName RowNull = "row_null";
    public static readonly StringName RowIdEmpty = "row_id_empty";
    public static readonly StringName RowIdDuplicate = "row_id_duplicate";

    // --- conditions ---
    public static readonly StringName TooManyConditions = "too_many_conditions";
    public static readonly StringName ConditionNull = "condition_null";
    public static readonly StringName ConditionUnknownType = "condition_unknown_type";

    // --- target ---
    public static readonly StringName TargetUnknownType = "target_unknown_type";

    // --- action / catalog 해석(§12) ---
    public static readonly StringName ActionMissing = "action_missing";
    public static readonly StringName ActionUnknownType = "action_unknown_type";
    public static readonly StringName ActionIdEmpty = "action_id_empty";
    public static readonly StringName ResolverMissing = "resolver_missing";
    public static readonly StringName ActionUnknown = "action_unknown";
    public static readonly StringName ActionDuplicate = "action_duplicate";
    public static readonly StringName ActionNullTemplate = "action_null_template";

    // --- target/action 호환(§11) ---
    public static readonly StringName TargetRequired = "target_required";
    public static readonly StringName TargetNotAllowed = "target_not_allowed";
    public static readonly StringName MissingTargetContext = "missing_target_context";

    // --- 잠긴 기본 system row(§11, 기획 H-7-2) ---
    public static readonly StringName DefaultAttackMissing = "default_attack_missing";
    public static readonly StringName DefaultAttackDuplicate = "default_attack_duplicate";
    public static readonly StringName DefaultAttackMalformed = "default_attack_malformed";
    public static readonly StringName TerminalWaitMissing = "terminal_wait_missing";
    public static readonly StringName TerminalWaitDuplicate = "terminal_wait_duplicate";
    public static readonly StringName TerminalWaitNotLast = "terminal_wait_not_last";
    public static readonly StringName TerminalWaitMalformed = "terminal_wait_malformed";
    public static readonly StringName PlayerRowBelowSystem = "player_row_below_system";

    // --- warning(기획 H-7-3, ADR-024 Warning Policy) ---
    public static readonly StringName DuplicateActiveRow = "duplicate_active_row";
    public static readonly StringName UnreachableAfterUnconditional = "unreachable_after_unconditional";

    // --- validator 자체 예외 격리 ---
    public static readonly StringName ValidatorInternalError = "validator_internal_error";

    // --- compile(Step 3, ADR-024 §5) ---
    public static readonly StringName GeneratedTreeInvalid = "generated_tree_invalid";
    public static readonly StringName ActionCreateFailed = "action_create_failed";
    public static readonly StringName CompilerInternalError = "compiler_internal_error";
}
