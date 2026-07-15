using System.Collections.Generic;
using Godot;
using AutoCrawler.Assets.Script.TurnAction;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// draft의 안정 ActionId를 유닛의 catalog와 대조하는 seam(ADR-024 §12). Step 2 validator는 해석 가능성만
// 검사하고(Resolve), Step 3 compiler가 유닛별 새 TurnActionBase 인스턴스 생성을 추가한다. runtime BehaviorTree를
// 탐색하지 않고 catalog만 본다 — apply가 BT root를 교체·free해도 해석이 성립하기 위함이다.

// catalog 조회 결과(§12 blocking error 근거).
public enum TacticActionResolution
{
    Ok,
    Unknown,        // catalog에 ActionId 없음
    Duplicate,      // catalog에 같은 ActionId가 둘 이상
    NullTemplate    // entry는 있으나 template이 null
}

public interface ITacticActionResolver
{
    // ActionId가 이 유닛의 catalog에서 유효한 template로 해석되는지. 상태를 변경하지 않는다.
    TacticActionResolution Resolve(StringName actionId);

    // 유닛 전용 새 TurnActionBase 인스턴스를 만든다(Step 3, ADR-024 §12). 매 호출이 template/타 유닛/이전 snapshot과
    // 공유하지 않는 격리 인스턴스를 반환하고 template을 변형하지 않는다. 해석 불가(Ok가 아님)면 null.
    TurnActionBase CreateInstance(StringName actionId);
}

// TacticActionCatalog를 감싸는 read-only resolver. 생성 시 catalog를 1회 훑어 lookup을 만든다. catalog/entry/
// template을 변형하지 않는다(validator 불변 계약). 유닛마다 다른 catalog를 감싸면 "두 유닛 resolver 차이"가
// 자연히 표현된다.
public sealed class TacticActionCatalogResolver : ITacticActionResolver
{
    private readonly Dictionary<string, int> _counts = new();
    private readonly Dictionary<string, TurnActionBase> _templates = new();

    public TacticActionCatalogResolver(TacticActionCatalog catalog)
    {
        if (catalog?.Entries == null) return;

        foreach (TacticActionCatalogEntry entry in catalog.Entries)
        {
            if (entry == null) continue;
            string id = entry.ActionId.ToString();
            if (string.IsNullOrEmpty(id)) continue;

            _counts.TryGetValue(id, out int count);
            _counts[id] = count + 1;
            // 중복은 count로 표면화한다. template map은 첫 항목을 유지한다(중복이면 Resolve가 Duplicate 우선).
            if (!_templates.ContainsKey(id)) _templates[id] = entry.Template;
        }
    }

    public TacticActionResolution Resolve(StringName actionId)
    {
        string id = actionId.ToString();
        if (string.IsNullOrEmpty(id) || !_counts.TryGetValue(id, out int count) || count == 0)
            return TacticActionResolution.Unknown;
        if (count > 1) return TacticActionResolution.Duplicate;
        return _templates.TryGetValue(id, out TurnActionBase template) && template != null
            ? TacticActionResolution.Ok
            : TacticActionResolution.NullTemplate;
    }

    // Duplicate()는 새 C# Resource 인스턴스를 만든다 — [Export] 설정 값(사거리·비용 등)은 복사되고,
    // C# 인스턴스 필드(_usedCost/ActionQueue/_explicitTarget 등 mutable 런타임 상태)는 생성자 기본값으로
    // 초기화된다. 따라서 유닛 간 런타임 상태가 격리되고 template은 변형되지 않는다(ADR-024 §3/§12).
    public TurnActionBase CreateInstance(StringName actionId)
    {
        if (Resolve(actionId) != TacticActionResolution.Ok) return null;
        TurnActionBase template = _templates[actionId.ToString()];
        return template.Duplicate() as TurnActionBase;
    }
}
