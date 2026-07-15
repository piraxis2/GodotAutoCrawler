using System;
using System.Collections.Generic;
using Godot;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using AutoCrawler.Assets.Script.TurnAction;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Compile;

// 컴파일 성공 결과의 단일 소유 스냅샷(BT-003 Step 3, ADR-024 §7). 특정 유닛 resolver context에서 만든 per-unit
// 결과로, 아직 부모가 없는 BT-002 root subtree와 유닛 전용 TurnActionBase 인스턴스를 소유한다. 편집 Resource를
// 실행 중 다시 읽지 않는다. 같은 draft라도 유닛마다 새 snapshot을 만든다(Node/action 격리).
public sealed class CompiledBoardSnapshot
{
    public StringName SourceBoardId { get; }
    public int SourceSchemaVersion { get; }
    // compile 대상 유닛 식별자(없으면 빈 StringName). 미래 PartySnapshot unit_id seam.
    public StringName UnitId { get; }
    // 저장 순서가 보존된 행 RowId 목록(debug/발동 통계 연결).
    public IReadOnlyList<StringName> RowIds { get; }
    // 정규화된 schema field + 행 순서로 계산한 결정론 signature(instance id/path 미사용, ADR-024 §7).
    public string SourceSignature { get; }
    // applied stale 판정 입력 = SourceSignature ⊕ catalog CatalogRevision(ADR-024 §7/§9).
    public string CompileInputSignature { get; }
    // 아직 부모가 없는 TacticPrioritySelector root subtree.
    public TacticPrioritySelector Root { get; }
    // resolver가 만든 유닛 전용 TurnActionBase 인스턴스(격리 검증/소유).
    public IReadOnlyList<TurnActionBase> Actions { get; }

    public CompiledBoardSnapshot(StringName sourceBoardId, int sourceSchemaVersion, StringName unitId,
        IReadOnlyList<StringName> rowIds, string sourceSignature, string compileInputSignature,
        TacticPrioritySelector root, IReadOnlyList<TurnActionBase> actions)
    {
        SourceBoardId = sourceBoardId;
        SourceSchemaVersion = sourceSchemaVersion;
        UnitId = unitId ?? new StringName();
        // 방어 복사: 외부 mutable list로 snapshot metadata를 변조할 수 없게 한다(public 불변 계약).
        RowIds = rowIds != null
            ? Array.AsReadOnly(new List<StringName>(rowIds).ToArray())
            : Array.AsReadOnly(Array.Empty<StringName>());
        SourceSignature = sourceSignature;
        CompileInputSignature = compileInputSignature;
        Root = root;
        Actions = actions != null
            ? Array.AsReadOnly(new List<TurnActionBase>(actions).ToArray())
            : Array.AsReadOnly(Array.Empty<TurnActionBase>());
    }

    // 소유한 root subtree를 폐기한다. 아직 어떤 컨테이너에도 설치하지 않은(부모 없는) snapshot을 버릴 때 쓴다.
    // 설치 후 교체/폐기는 Step 4 apply owner가 소유한다.
    public void FreeRoot()
    {
        if (GodotObject.IsInstanceValid(Root) && Root.GetParent() == null) Root.Free();
    }
}
