using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

public partial class GameWindow : Godot.Window
{

    [Export] private Control _snapTarget;
    [Export] public float SnapDistance { get; set; } = 20.0f;

    public override void _Notification(int what)
    {
        // 창 위치가 변경되었을 때 Godot가 보내는 알림을 확인합니다.
        if (what == (int)NotificationWMPositionChanged)
        {
            // 스냅 로직 실행
            SnapToTargets();
        }
    }

    private void SnapToTargets()
    {
        Rect2 windowRect = new Rect2(Position, Size);
        Vector2 newPos = windowRect.Position;
        bool didSnap = false;

        Vector2 mainGameWindowPos = DisplayServer.Singleton.WindowGetPosition();

        Vector2 targetAbsolutePos = mainGameWindowPos + _snapTarget.GetGlobalPosition();
        Rect2 targetRect = new Rect2(targetAbsolutePos, _snapTarget.Size);

        // --- 스냅 로직 ---
        // 창의 왼쪽 -> 타겟의 오른쪽
        if (Mathf.Abs(windowRect.Position.X - (targetRect.Position.X + targetRect.Size.X)) < SnapDistance)
        {
            newPos.X = targetRect.Position.X + targetRect.Size.X;
            didSnap = true;
        }

        // 창의 오른쪽 -> 타겟의 왼쪽
        if (Mathf.Abs(windowRect.Position.X + windowRect.Size.X - targetRect.Position.X) < SnapDistance)
        {
            newPos.X = targetRect.Position.X - windowRect.Size.X;
            didSnap = true;
        }
        // (수직 스냅 로직도 동일하게 적용...)

        if (didSnap)
        {
            // 이 알림은 사용자가 창을 움직일 때만 발생하므로,
            // 여기서 Position을 설정해도 무한 루프가 발생하지 않습니다.
            Position = (Vector2I)newPos;
        }
    }
}