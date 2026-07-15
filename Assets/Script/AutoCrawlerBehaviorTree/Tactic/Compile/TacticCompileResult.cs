using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Validation;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Compile;

// compile 결과(BT-003 Step 3, ADR-024 §4). validation과 compile 진단을 공용 TacticDiagnostic으로 담고,
// Success일 때만 Snapshot을 공개한다. Error가 하나라도 있으면 Success=false, Snapshot=null이다.
public sealed class TacticCompileResult
{
    public bool Success { get; }
    public IReadOnlyList<TacticDiagnostic> Diagnostics { get; }
    // Success일 때만 non-null.
    public CompiledBoardSnapshot Snapshot { get; }

    public TacticCompileResult(bool success, IReadOnlyList<TacticDiagnostic> diagnostics, CompiledBoardSnapshot snapshot)
    {
        Success = success;
        // 방어 복사(public 불변 계약). TacticDiagnostic 자체가 불변이라 얕은 복사로 충분하다.
        Diagnostics = diagnostics != null
            ? Array.AsReadOnly(new List<TacticDiagnostic>(diagnostics).ToArray())
            : Array.AsReadOnly(Array.Empty<TacticDiagnostic>());
        Snapshot = snapshot;
    }
}
