using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

/// <summary>
/// D7. Window 객체를 모르는 순수 geometry 로직이다. 입력도 출력도 <see cref="Rect2I"/>뿐이라
/// headless에서 창을 띄우지 않고 off-screen/tiny screen/multi-monitor 경계를 단언할 수 있다.
///
/// 모든 rect는 **decoration(타이틀바/테두리)을 포함한** 좌표다. Step 1 실측에서 Godot은 창을 화면 안으로
/// 클램프할 때 client rect만 보고 decoration을 무시했고, 그 결과 타이틀바가 화면 밖에 남아 창을 잡을 수
/// 없었다. 따라서 이 클래스는 decoration 포함 rect만 다룬다.
/// </summary>
public static class WorkspaceGeometry
{
    /// <summary>창을 마우스로 잡으려면 타이틀바가 가로로 최소 이만큼 보여야 한다.</summary>
    public const int MinGrabWidth = 64;

    /// <summary>타이틀바 추정 높이. 이 띠 전체가 work area 안에 있어야 드래그·닫기가 가능하다.</summary>
    public const int TitleBarHeight = 32;

    /// <summary>
    /// decoration 크기를 알 수 없는 headless/pure test용 보수적 inset.
    /// 실측(Windows 11, scale 1.0): leading = (8, 31), total extra = (16, 39).
    /// </summary>
    public static readonly Vector2I FallbackLeadingInset = new(8, TitleBarHeight);
    public static readonly Vector2I FallbackTotalExtra = new(16, TitleBarHeight + 8);

    /// <summary>
    /// decoration 포함 rect의 중심점이 포함된 screen을 고른다. 어떤 screen에도 속하지 않으면 primary다.
    /// screen 원점이 (0, 0)이라고 가정하지 않는다(실측: screen[0] pos = (0, 542)).
    /// </summary>
    public static int ResolveTargetScreen(Rect2I decoRect, IReadOnlyList<Rect2I> usableRects, int primaryScreen)
    {
        if (usableRects == null || usableRects.Count == 0) return 0;

        int fallback = primaryScreen >= 0 && primaryScreen < usableRects.Count ? primaryScreen : 0;

        Vector2I center = decoRect.Position + decoRect.Size / 2;
        for (int i = 0; i < usableRects.Count; i++)
        {
            if (usableRects[i].HasPoint(center)) return i;
        }

        return fallback;
    }

    /// <summary>
    /// 창을 잡을 수 있는가. 타이틀바 띠가 세로로 온전히 work area 안에 있고, 가로로 최소 폭만큼 겹쳐야 한다.
    /// 창 아래쪽만 살짝 걸친 상태는 잡을 수 없으므로 false다.
    /// </summary>
    public static bool IsReachable(Rect2I decoRect, Rect2I workArea)
    {
        Rect2I titleStrip = TitleStrip(decoRect);
        Rect2I intersection = titleStrip.Intersection(workArea);

        // 타이틀바 띠가 세로로 잘리면(위로 넘어가거나 아래로 밀리면) 잡을 수 없다.
        if (intersection.Size.Y != titleStrip.Size.Y) return false;

        int requiredWidth = Mathf.Min(MinGrabWidth, decoRect.Size.X);
        return intersection.Size.X >= requiredWidth;
    }

    /// <summary>
    /// decoration 포함 rect를 work area 안으로 회수한다. work area보다 큰 창은 먼저 축소한다.
    /// 반환 rect는 항상 <see cref="IsReachable"/>를 만족한다.
    /// </summary>
    public static Rect2I ClampIntoWorkArea(Rect2I decoRect, Rect2I workArea)
    {
        Vector2I size = new(
            Mathf.Min(decoRect.Size.X, workArea.Size.X),
            Mathf.Min(decoRect.Size.Y, workArea.Size.Y));

        Vector2I maxPosition = workArea.Position + workArea.Size - size;
        Vector2I position = new(
            Mathf.Clamp(decoRect.Position.X, workArea.Position.X, maxPosition.X),
            Mathf.Clamp(decoRect.Position.Y, workArea.Position.Y, maxPosition.Y));

        return new Rect2I(position, size);
    }

    /// <summary>회수가 필요하면 clamp한 rect를, 이미 잡을 수 있으면 원본을 그대로 돌려준다.</summary>
    public static Rect2I Gather(Rect2I decoRect, Rect2I workArea)
    {
        return IsReachable(decoRect, workArea) ? decoRect : ClampIntoWorkArea(decoRect, workArea);
    }

    // --- WS-002 마스터 내부(embedded) 좌표계 ---
    // 임베디드 subwindow는 OS decoration/screen이 없다. 창은 마스터의 content area(메뉴바·로그 도크를 제외한
    // rect) 안에서만 움직인다. 판정·clamp는 screen work area가 아니라 이 content rect 기준의 client rect다.

    /// <summary>
    /// 마스터 크기에서 content area rect를 계산한다. 메뉴바(좌측 <paramref name="menuWidth"/>)와
    /// 하단 로그 도크(<paramref name="dockHeight"/>)를 제외하고, 상단에 임베디드 타이틀바용
    /// <paramref name="topInset"/>를 남긴다.
    ///
    /// 마스터가 메뉴/도크보다 작아도 반환 rect는 항상 마스터 경계 안에 있고 크기 ≥ 1이다(fail-safe):
    /// position은 [0, master-1]로 clamp하고, 크기는 남은 공간(master - position)을 넘지 않는다.
    /// </summary>
    public static Rect2I ComputeContentRect(Vector2I masterSize, int menuWidth, int dockHeight, int topInset)
    {
        int masterW = Mathf.Max(1, masterSize.X);
        int masterH = Mathf.Max(1, masterSize.Y);

        int x = Mathf.Clamp(menuWidth, 0, masterW - 1);
        int y = Mathf.Clamp(topInset, 0, masterH - 1);

        // 남은 공간을 넘지 않게 크기를 clamp한다. x + w <= masterW, y + h <= masterH 보장(마스터 밖 시작 방지).
        int w = masterW - x;                                  // x <= masterW-1 이므로 항상 >= 1
        int h = Mathf.Clamp(masterH - dockHeight - y, 1, masterH - y);
        return new Rect2I(x, y, w, h);
    }

    /// <summary><paramref name="inner"/>가 <paramref name="outer"/> 안에 완전히 들어가는가.</summary>
    public static bool IsInside(Rect2I inner, Rect2I outer)
    {
        return inner.Position.X >= outer.Position.X
            && inner.Position.Y >= outer.Position.Y
            && inner.Position.X + inner.Size.X <= outer.Position.X + outer.Size.X
            && inner.Position.Y + inner.Size.Y <= outer.Position.Y + outer.Size.Y;
    }

    /// <summary>
    /// 창 client rect를 content area 안으로 회수한다. content보다 큰 창은 먼저 축소한 뒤 clamp한다.
    /// 이미 content 안에 있으면 원본을 그대로 돌려준다(gather 멱등). 반환 rect는 항상
    /// <see cref="IsInside"/>(content)를 만족한다 — 즉 메뉴바/로그 도크를 침범하지 않는다.
    /// </summary>
    public static Rect2I GatherIntoContent(Rect2I windowRect, Rect2I contentRect)
    {
        return IsInside(windowRect, contentRect) ? windowRect : ClampIntoWorkArea(windowRect, contentRect);
    }

    // --- WS-002 Step 3: 레이아웃 슬롯 스냅 (D5) ---

    /// <summary>슬롯 스냅 판정 결과. 후보 없음이면 <see cref="HasCandidate"/>가 false다.</summary>
    public readonly struct SlotSnapResult
    {
        public SlotSnapResult(bool hasCandidate, string slotId, Rect2I highlightRect, Rect2I appliedRect)
        {
            HasCandidate = hasCandidate;
            SlotId = slotId;
            HighlightRect = highlightRect;
            AppliedRect = appliedRect;
        }

        public bool HasCandidate { get; }

        /// <summary>후보 슬롯 id(=창 id). 후보 없음이면 null.</summary>
        public string SlotId { get; }

        /// <summary>드래그 중 표시할 하이라이트 rect(슬롯 rect).</summary>
        public Rect2I HighlightRect { get; }

        /// <summary>drop 시 창에 적용할 rect. content area를 벗어나지 않고 로그 도크와 겹치지 않는다.</summary>
        public Rect2I AppliedRect { get; }

        public static SlotSnapResult None => new(false, null, default, default);
    }

    /// <summary>
    /// D5. 드래그 중인 창 rect의 중심이 현재 국면 슬롯 중심에서 <paramref name="snapDistance"/> 안이면
    /// 가장 가까운 슬롯을 후보로 반환한다. 거리 밖·<paramref name="snapDisabled"/>·빈 슬롯이면 후보 없음이다.
    ///
    /// 같은 거리 tie-break는 슬롯 중심의 정규 순서(Y → X → slot id ordinal)로 stable하다.
    /// applied rect는 슬롯 rect를 content area 안으로 clamp한 값이다.
    /// </summary>
    public static SlotSnapResult FindSlotSnap(
        Rect2I draggedRect,
        IReadOnlyDictionary<string, Rect2I> slots,
        int snapDistance,
        Rect2I contentRect,
        bool snapDisabled)
    {
        if (snapDisabled || slots == null || slots.Count == 0) return SlotSnapResult.None;

        Vector2I draggedCenter = draggedRect.Position + draggedRect.Size / 2;
        long maxDistSq = (long)snapDistance * snapDistance;

        string bestId = null;
        Rect2I bestSlot = default;
        Vector2I bestCenter = default;
        long bestDistSq = long.MaxValue;

        foreach (KeyValuePair<string, Rect2I> entry in slots)
        {
            Rect2I slot = entry.Value;
            Vector2I slotCenter = slot.Position + slot.Size / 2;
            Vector2I d = slotCenter - draggedCenter;
            long distSq = (long)d.X * d.X + (long)d.Y * d.Y;
            if (distSq > maxDistSq) continue; // 거리 밖

            bool better = bestId == null
                || distSq < bestDistSq
                || (distSq == bestDistSq && IsCanonicallyBefore(slotCenter, entry.Key, bestCenter, bestId));
            if (!better) continue;

            bestId = entry.Key;
            bestSlot = slot;
            bestCenter = slotCenter;
            bestDistSq = distSq;
        }

        if (bestId == null) return SlotSnapResult.None;
        return new SlotSnapResult(true, bestId, bestSlot, GatherIntoContent(bestSlot, contentRect));
    }

    /// <summary>같은 거리에서 A가 B보다 앞서는가. 정규 순서: 중심 Y → 중심 X → slot id ordinal.</summary>
    private static bool IsCanonicallyBefore(Vector2I aCenter, string aId, Vector2I bCenter, string bId)
    {
        if (aCenter.Y != bCenter.Y) return aCenter.Y < bCenter.Y;
        if (aCenter.X != bCenter.X) return aCenter.X < bCenter.X;
        return string.CompareOrdinal(aId, bId) < 0;
    }

    private static Rect2I TitleStrip(Rect2I decoRect)
    {
        int height = Mathf.Min(TitleBarHeight, decoRect.Size.Y);
        return new Rect2I(decoRect.Position, new Vector2I(decoRect.Size.X, height));
    }
}
