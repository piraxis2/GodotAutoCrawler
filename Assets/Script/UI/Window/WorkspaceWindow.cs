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

    /// <summary>
    /// 특정 content mode(<see cref="_gatedModeId"/>)에서만 보이는 패널. World 거점 모드의 고정 패널
    /// (캘린더/위험 게이지/행동 카드)이 여기 들어간다. null이면 게이팅 없음(다른 창은 미사용).
    /// </summary>
    [Export] private Control _modeGatedPanels;

    /// <summary><see cref="_modeGatedPanels"/>가 보이는 content mode. 기본 <c>hub</c>.</summary>
    [Export] private string _gatedModeId = WorldContentMode.Hub;

    private string _contentMode = "";

    /// <summary>
    /// World 창의 hub/battle/replay 같은 콘텐츠 모드다(D4). Godot <c>Window.ModeEnum</c>과 무관하다.
    /// 전환 때 새 창을 만들지 않고 같은 Window의 content만 바꾼다. 모드에 묶인 패널의 표시도 갱신한다.
    /// </summary>
    public string ContentMode
    {
        get => _contentMode;
        set
        {
            if (_contentMode == value) return;
            _contentMode = value ?? "";
            if (_contentModeLabel != null) _contentModeLabel.Text = $"content_mode: {_contentMode}";
            UpdateModeGatedPanels();
        }
    }

    public override void _Ready()
    {
        ConfigureAsWorkspaceChildWindow();

        // 닫기 버튼/OS close는 queue_free가 아니라 hide다. 창의 작업 상태를 잃지 않고 메뉴에서 다시 부를 수 있다.
        CloseRequested += HideWindow;
        UpdateModeGatedPanels();
    }

    /// <summary>
    /// Native pop-out 모드에서도 OS 독립 앱 창이 아니라 마스터에 종속된 하위 창으로 동작하게 한다.
    /// 별도 작업표시줄 항목을 만들지 않고, 마스터 위 z-order를 유지하는 계약이다. Exclusive는 modal 입력 잠금을 만들기 때문에 끈다.
    /// </summary>
    public void ConfigureAsWorkspaceChildWindow()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless" || DisplayServer.GetScreenCount() <= 0)
            return;

        Transient = true;
        TransientToFocused = false;
        Exclusive = false;
        AlwaysOnTop = false;
    }


    private static bool IsHeadlessRun()
    {
        return OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless" || DisplayServer.GetScreenCount() <= 0;
    }
    /// <summary>모드에 묶인 패널은 현재 content mode가 <see cref="_gatedModeId"/>일 때만 보인다.</summary>
    private void UpdateModeGatedPanels()
    {
        if (_modeGatedPanels != null) _modeGatedPanels.Visible = _contentMode == _gatedModeId;
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






