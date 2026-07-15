using System;
using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Validation;

// 진단 심각도(ADR-024 §4). Error 하나라도 있으면 snapshot 생성·적용 불가, Warning은 적용 허용, Info는 편집 도움.
public enum TacticDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

// 구조화된 진단 한 건(ADR-024 §4). UI는 Code/RowId/FieldPath로 행·필드를 가리키고 MessageKey/args로 현지화한다.
// 내부 예외 문자열을 플레이어 메시지로 직접 쓰지 않는다(DeveloperDetail은 개발 로그용). 불변 값이다 — 생성 후
// 필드가 바뀌지 않으므로 호출자가 다음 validation에 영향을 줄 수 없다.
public sealed class TacticDiagnostic
{
    public TacticDiagnosticSeverity Severity { get; }
    public StringName Code { get; }
    // board-level 진단이면 빈 StringName.
    public StringName RowId { get; }
    // 해당 없으면 null.
    public string FieldPath { get; }
    public StringName MessageKey { get; }
    public IReadOnlyList<string> MessageArgs { get; }
    public string DeveloperDetail { get; }

    public TacticDiagnostic(
        TacticDiagnosticSeverity severity,
        StringName code,
        StringName rowId = null,
        string fieldPath = null,
        IReadOnlyList<string> messageArgs = null,
        string developerDetail = null)
    {
        Severity = severity;
        Code = code ?? new StringName();
        RowId = rowId ?? new StringName();
        FieldPath = fieldPath;
        // v1: 로컬라이즈 키 = 안정 code. 현지화 renderer가 code -> 문구로 매핑한다(후속).
        MessageKey = Code;
        // 방어 복사: 호출자가 넘긴 배열을 이후에 바꿔도 진단은 불변이다.
        MessageArgs = messageArgs != null
            ? Array.AsReadOnly(new List<string>(messageArgs).ToArray())
            : Array.AsReadOnly(Array.Empty<string>());
        DeveloperDetail = developerDetail;
    }

    public override string ToString()
    {
        string row = RowId.ToString().Length > 0 ? $"@{RowId}" : "";
        string field = FieldPath != null ? $".{FieldPath}" : "";
        return $"{Severity}:{Code}{row}{field}";
    }
}

// validation 결과(ADR-024 §4). Step 2는 snapshot을 만들지 않으므로 진단 목록 + IsValid만 담는다. compile
// result(Snapshot 포함)는 Step 3의 TacticCompileResult가 이 진단 타입을 재사용한다.
public sealed class TacticValidationResult
{
    // Error 심각도 진단이 하나도 없으면 true. Warning/Info만 있으면 여전히 유효하다.
    public bool IsValid { get; }
    public IReadOnlyList<TacticDiagnostic> Diagnostics { get; }

    public bool HasErrors => !IsValid;

    public TacticValidationResult(bool isValid, IReadOnlyList<TacticDiagnostic> diagnostics)
    {
        IsValid = isValid;
        // 방어 복사: 생성자에 넘어온 목록을 호출자가 이후에 바꿔도 결과는 불변이다(public 불변 계약).
        // TacticDiagnostic 자체가 불변이라 얕은 복사로 충분하다.
        Diagnostics = diagnostics != null
            ? Array.AsReadOnly(new List<TacticDiagnostic>(diagnostics).ToArray())
            : Array.AsReadOnly(Array.Empty<TacticDiagnostic>());
    }
}
