using System;
using System.Collections.Generic;

namespace AutoCrawler.Assets.Script.GameLog;

// GameLog의 순수 데이터/필터 모델(D6). Godot Control/Node를 모른다 — headless 테스트로 검증한다.
// append 순서를 보존하고, 최근 MaxEntries줄만 유지하며, 채널/등급 필터를 제공한다.
public sealed class GameLogModel : IGameLogSink
{
    // E-9 결정 ④: 표시 로그는 최근 200개만 유지한다. 초과분은 오래된 것부터 폐기.
    public const int MaxEntries = 200;

    private readonly List<GameLogEntry> _entries = new();
    private long _nextId = 1;

    // append + trim이 끝난 뒤, 새로 추가된 entry와 함께 발행한다. UI(Step 2)가 갱신 hook으로 쓴다.
    public event Action<GameLogEntry> EntryAdded;

    // Clear() 후 발행한다.
    public event Action Cleared;

    // 현재 보관 중인 entry 수(<= MaxEntries).
    public int Count => _entries.Count;

    // 사건을 추가한다. 모델이 Id를 부여(OD1: 증가 long)하고, MaxEntries 초과 시 가장 오래된 entry부터 제거한다.
    public void Append(GameLogEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        entry.Id = _nextId++;
        _entries.Add(entry);

        // 한 번에 하나씩 append하므로 초과는 최대 1개다. 방어적으로 루프를 돈다.
        while (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(0);
        }

        EntryAdded?.Invoke(entry);
    }

    // 전부 비운다. Id 카운터는 리셋하지 않는다 — Id는 모델 수명 동안 단조 증가로 안정하다.
    public void Clear()
    {
        if (_entries.Count == 0) return;
        _entries.Clear();
        Cleared?.Invoke();
    }

    // 채널/등급 필터로 append 순서(오래된 것 -> 최신)를 유지한 스냅샷을 반환한다.
    //
    // - channel == null: 모든 채널(전체 탭).
    // - channel != null: 그 채널만.
    // - Event는 항상 포함한다. includeDetail/includeTrace로 상세/추적을 더한다.
    //
    // 예:
    // - 기본 뷰(전체 탭) = GetEntries(null, includeDetail: false, includeTrace: false) -> 모든 채널 Event만.
    // - 채널 탭        = GetEntries(GameLogChannel.Combat, includeDetail: true, includeTrace: false).
    // - debug          = includeTrace: true.
    public IReadOnlyList<GameLogEntry> GetEntries(
        GameLogChannel? channel,
        bool includeDetail,
        bool includeTrace)
    {
        var result = new List<GameLogEntry>();
        foreach (GameLogEntry entry in _entries)
        {
            if (channel.HasValue && entry.Channel != channel.Value) continue;
            if (!IsSeverityVisible(entry.Severity, includeDetail, includeTrace)) continue;
            result.Add(entry);
        }

        return result;
    }

    private static bool IsSeverityVisible(GameLogSeverity severity, bool includeDetail, bool includeTrace)
    {
        return severity switch
        {
            GameLogSeverity.Event => true,
            GameLogSeverity.Detail => includeDetail,
            GameLogSeverity.Trace => includeTrace,
            _ => false,
        };
    }
}
