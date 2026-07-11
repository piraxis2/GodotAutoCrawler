using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

/// <summary>World 창의 content mode다. Godot <c>Window.ModeEnum</c>과 다른 개념이다(D4).</summary>
public static class WorldContentMode
{
    public const string Hub = "hub";
    public const string Battle = "battle";
    public const string Replay = "replay";
}

/// <summary>TacticBoard 창의 content mode. 전투=읽기, 분석=편집 placeholder 구분(WS-002 Step 4).</summary>
public static class TacticBoardContentMode
{
    public const string Read = "read";
    public const string Edit = "edit";
}

/// <summary>
/// preset이 창 하나에 적용할 값이다. <see cref="NormalizedRect"/>는 절대 px가 아니라
/// **마스터 content area 대비 정규화 비율**(position/size, 0~1)이다. 실제 px는 런타임 content rect에서
/// resolve한다(Step 0 리뷰 Finding 2 계약). E-7 개정판 레이아웃 비율의 소스다.
/// </summary>
public readonly struct WindowLayout
{
    public WindowLayout(bool visible, Rect2 normalizedRect, string contentMode = null)
    {
        Visible = visible;
        NormalizedRect = normalizedRect;
        ContentMode = contentMode;
    }

    public bool Visible { get; }

    /// <summary>content area 대비 정규화 rect(0~1). 보이는 영역(타이틀바 포함) 비율이다.</summary>
    public Rect2 NormalizedRect { get; }

    /// <summary>World/TacticBoard content mode. null이면 content mode를 건드리지 않는다.</summary>
    public string ContentMode { get; }
}

/// <summary>
/// D3. preset은 고정 화면이 아니라 창들의 {visible, normalized rect, content_mode}를 적용하는 명령이다.
///
/// WS-002 창 다이어트(E-2): floating managed 창은 `World`/`TacticBoard`/`Report` 3종이다. `Log`는 마스터
/// 하단 도크이고, `Situation`/`WeeklyAction`/`Calendar`는 World 거점 모드 내부 고정 패널로 이관됐다.
///
/// E-7 개정판 비율(content area 대비): outgame=World 전면, battle=World 64% + TacticBoard 33%,
/// analysis=Report 34% + TacticBoard 36% + World 26%. 하단 로그 도크는 마스터가 별도로 소유한다.
/// </summary>
public static class WorkspaceLayoutPreset
{
    public const string Outgame = "outgame";
    public const string Battle = "battle";
    public const string Analysis = "analysis";

    private static readonly Dictionary<string, Dictionary<string, WindowLayout>> Presets = new()
    {
        // 아웃게임: 거점 세계 창 하나가 content를 채운다(캘린더/위험/행동 카드는 World 내부 패널).
        [Outgame] = new Dictionary<string, WindowLayout>
        {
            ["world"] = new(true, new Rect2(0f, 0f, 1f, 1f), WorldContentMode.Hub),
            ["tacticboard"] = new(false, new Rect2(0.66f, 0f, 0.34f, 1f), TacticBoardContentMode.Read),
            ["report"] = new(false, new Rect2(0f, 0f, 0.33f, 1f))
        },
        // 전투: 세계(전장) 64% + 택틱 보드(읽기) 33% (E-7 개정판). 리포트 숨김.
        [Battle] = new Dictionary<string, WindowLayout>
        {
            ["world"] = new(true, new Rect2(0f, 0f, 0.64f, 1f), WorldContentMode.Battle),
            ["tacticboard"] = new(true, new Rect2(0.66f, 0f, 0.33f, 1f), TacticBoardContentMode.Read),
            ["report"] = new(false, new Rect2(0f, 0f, 0.34f, 1f))
        },
        // 분석: 리포트 34% + 택틱 보드(편집) 36% + 세계(리플레이) 26% (E-7 개정판).
        [Analysis] = new Dictionary<string, WindowLayout>
        {
            ["report"] = new(true, new Rect2(0f, 0f, 0.34f, 1f)),
            ["tacticboard"] = new(true, new Rect2(0.36f, 0f, 0.36f, 1f), TacticBoardContentMode.Edit),
            ["world"] = new(true, new Rect2(0.74f, 0f, 0.26f, 1f), WorldContentMode.Replay)
        }
    };

    public static IEnumerable<string> PresetIds => Presets.Keys;

    /// <summary>정규화 rect를 content area 기준 px rect로 변환한다.</summary>
    public static Rect2I ResolveRect(Rect2 normalizedRect, Rect2I contentRect)
    {
        Vector2 size = contentRect.Size;
        var position = new Vector2I(
            contentRect.Position.X + Mathf.RoundToInt(size.X * normalizedRect.Position.X),
            contentRect.Position.Y + Mathf.RoundToInt(size.Y * normalizedRect.Position.Y));
        var pixelSize = new Vector2I(
            Mathf.RoundToInt(size.X * normalizedRect.Size.X),
            Mathf.RoundToInt(size.Y * normalizedRect.Size.Y));
        return new Rect2I(position, pixelSize);
    }

    /// <summary>
    /// D5. 현재 국면 preset의 슬롯 rect를 content area 기준으로 resolve한다. 슬롯 스냅 후보의 소스다.
    /// **visible 창만** 슬롯이 된다(hidden slot 제외). rect는 정규화 비율을 content px로 변환한 뒤 content
    /// 안으로 clamp한 '보이는 영역(타이틀바 포함)'이다. unknown preset이면 빈 dict다(fail-closed).
    /// </summary>
    public static Dictionary<string, Rect2I> ResolveSlots(string presetId, Rect2I contentRect)
    {
        var slots = new Dictionary<string, Rect2I>();
        if (!TryGetPreset(presetId, out IReadOnlyDictionary<string, WindowLayout> layouts)) return slots;

        foreach (KeyValuePair<string, WindowLayout> entry in layouts)
        {
            if (!entry.Value.Visible) continue; // hidden slot 제외

            Rect2I px = ResolveRect(entry.Value.NormalizedRect, contentRect);
            slots[entry.Key] = WorkspaceGeometry.GatherIntoContent(px, contentRect);
        }

        return slots;
    }

    /// <summary>unknown preset id는 fail-closed다(false, layouts = null).</summary>
    public static bool TryGetPreset(string presetId, out IReadOnlyDictionary<string, WindowLayout> layouts)
    {
        layouts = null;
        if (string.IsNullOrEmpty(presetId)) return false;
        if (!Presets.TryGetValue(presetId, out Dictionary<string, WindowLayout> found)) return false;

        layouts = found;
        return true;
    }
}
