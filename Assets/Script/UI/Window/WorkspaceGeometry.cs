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

    private static Rect2I TitleStrip(Rect2I decoRect)
    {
        int height = Mathf.Min(TitleBarHeight, decoRect.Size.Y);
        return new Rect2I(decoRect.Position, new Vector2I(decoRect.Size.X, height));
    }
}
