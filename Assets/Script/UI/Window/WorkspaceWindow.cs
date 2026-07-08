using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

/// <summary>
/// Workspace 작업대의 managed window 공통 기반이다.
/// 기존 <see cref="GameWindow"/>(스냅 실험 코드)를 상속하지 않는다. 스냅은 WS-001 범위 밖이다.
/// </summary>
public partial class WorkspaceWindow : Godot.Window
{
    /// <summary>
    /// registry lookup에 쓰는 안정 식별자다. 비어 있으면 manager가 등록을 거부한다.
    /// </summary>
    [Export] public string WindowId { get; set; } = "";

    /// <summary>content mode 표시용 placeholder. 없어도 된다.</summary>
    [Export] private Label _contentModeLabel;

    private string _contentMode = "";

    /// <summary>
    /// World 창의 hub/battle/replay 같은 콘텐츠 모드다(D4). Godot <c>Window.ModeEnum</c>과 무관하다.
    /// 전환 때 새 창을 만들지 않고 같은 Window의 content만 바꾼다.
    /// </summary>
    public string ContentMode
    {
        get => _contentMode;
        set
        {
            if (_contentMode == value) return;
            _contentMode = value ?? "";
            if (_contentModeLabel != null) _contentModeLabel.Text = $"content_mode: {_contentMode}";
        }
    }

    public override void _Ready()
    {
        // 닫기 버튼/OS close는 queue_free가 아니라 hide다. 창의 작업 상태를 잃지 않고 메뉴에서 다시 부를 수 있다.
        CloseRequested += HideWindow;
    }

    public override void _ExitTree()
    {
        CloseRequested -= HideWindow;
    }

    private void HideWindow()
    {
        Hide();
    }
}
