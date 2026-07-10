using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem;

// 유닛별 스킬 ammo remaining을 보관하는 순수 C# 상태. ADR-021.
// StatusController와 동형으로 CharacterArticle이 소유한다. Godot Resource가 아니며 .tres에 직렬화되지 않으므로
// 같은 SkillDefinition을 여러 유닛이 공유해도 remaining이 섞이지 않는다.
// Unlimited 스킬은 등록하지 않는다(무제한). 미등록(미충전) 제한 스킬은 fail-closed(0발)로 취급한다.
public sealed class SkillAmmoState
{
    private readonly Dictionary<StringName, Entry> _entries = new();

    private struct Entry
    {
        public int Remaining;
        public int Max;
        public SkillAmmoResetScope Scope;
    }

    // 제한 스킬을 등록하고 remaining을 ammo로 충전한다. Unlimited/음수 ammo/빈 id는 무시한다.
    // Step 1은 테스트 helper가, Step 3은 전투 시작 reset이 이 창구로 충전한다.
    public void Charge(StringName skillId, int ammo, SkillAmmoResetScope scope)
    {
        if (skillId == null || skillId == default || scope == SkillAmmoResetScope.Unlimited || ammo < 0)
        {
            return;
        }

        _entries[skillId] = new Entry { Remaining = ammo, Max = ammo, Scope = scope };
    }

    // 제한 스킬이 아직 사용 가능한 remaining을 가지는지. 미등록 제한 스킬은 false(fail-closed).
    public bool HasAmmo(StringName skillId)
    {
        return _entries.TryGetValue(skillId, out var entry) && entry.Remaining > 0;
    }

    // remaining을 1 차감한다. 미등록/0이면 아무 것도 하지 않고 false. 커밋 후 환불 창구는 두지 않는다(ADR-021 §3).
    public bool Consume(StringName skillId)
    {
        if (!_entries.TryGetValue(skillId, out var entry) || entry.Remaining <= 0)
        {
            return false;
        }

        entry.Remaining--;
        _entries[skillId] = entry;
        return true;
    }

    // 읽기 전용 조회. Unlimited/미등록은 false. 후속 HUD/report가 소비한다(ADR-021 §6).
    public bool TryGetAmmo(StringName skillId, out int remaining, out int max)
    {
        if (_entries.TryGetValue(skillId, out var entry))
        {
            remaining = entry.Remaining;
            max = entry.Max;
            return true;
        }

        remaining = 0;
        max = 0;
        return false;
    }
}
