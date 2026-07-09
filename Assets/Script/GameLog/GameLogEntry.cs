using System.Collections.Generic;

namespace AutoCrawler.Assets.Script.GameLog;

// 플레이어에게 보여줄 표시용 로그 항목(E-9). 전투/스킬 raw fact가 아니라 adapter를 거친 표시 데이터다(D4).
// entry는 표시/저장/필터/링크 정보만 담고, 실행하지 않는다(D2) — 클릭 동작은 interaction handler가 맡는다.
public sealed class GameLogEntry
{
    private static readonly IReadOnlyDictionary<string, string> EmptyArgs = new Dictionary<string, string>();

    public GameLogEntry(
        GameLogChannel channel,
        GameLogSeverity severity,
        GameLogImportance importance,
        string title,
        string body = null,
        string occurredAt = null,
        GameLogLink link = null,
        bool isCollapsed = false,
        string titleKey = null,
        IReadOnlyDictionary<string, string> args = null)
    {
        Channel = channel;
        Severity = severity;
        Importance = importance;
        Title = title ?? string.Empty;
        Body = body;
        OccurredAt = occurredAt;
        Link = link;
        IsCollapsed = isCollapsed;
        TitleKey = titleKey;
        Args = args ?? EmptyArgs;
    }

    // 안정 식별자. GameLogModel.Append가 부여한다(OD1: 증가 long). append 전에는 0이다.
    public long Id { get; internal set; }

    public GameLogChannel Channel { get; }
    public GameLogSeverity Severity { get; }
    public GameLogImportance Importance { get; }

    // 1줄 제목. v0 표시 fallback이며, 현지화 renderer가 없을 때 그대로 표시한다. null은 빈 문자열로 정규화한다.
    public string Title { get; }

    // optional 현지화 키. 예: `combat.skill.stun_miss`. renderer가 있으면 Args로 채워 표시한다.
    public string TitleKey { get; }

    // optional 현지화 인자(name -> value). 저장 가능한 string 값만 담는다. null이면 빈 dict.
    public IReadOnlyDictionary<string, string> Args { get; }

    // 상세 본문. optional(null 허용).
    public string Body { get; }

    // 주차/턴/전투 시각의 표시용 문자열(OD2: v0는 string). optional.
    public string OccurredAt { get; }

    // 클릭 대상 예약 데이터. optional(null = 링크 없음).
    public GameLogLink Link { get; }

    // 대화 아카이브처럼 접힘으로 시작하는 항목의 표시 상태. UI가 펼침/접힘을 토글한다(Step 2).
    public bool IsCollapsed { get; set; }
}
