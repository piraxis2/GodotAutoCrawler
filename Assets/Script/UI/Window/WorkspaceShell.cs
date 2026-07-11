using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

/// <summary>
/// Root/Main window는 콘텐츠 화면이 아니라 작업대 관리자다(D2).
/// W0에서는 registry의 managed window마다 toggle 버튼 하나를 만든다.
/// </summary>
public partial class WorkspaceShell : Node
{
    [Export] private WorkspaceWindowManager _manager;
    [Export] private BoxContainer _windowMenu;
    [Export] private BoxContainer _presetMenu;

    /// <summary>
    /// 셸 entry scene에서만 켠다. 켜지면 시작 시 저장 배치를 복원하고, 종료 시 자동 저장한다.
    /// 테스트는 트리 진입 전에 이 값을 false로 두어 raw registry 상태를 관찰한다.
    /// </summary>
    [Export] private bool _restoreLayoutOnReady;

    public override void _Ready()
    {
        if (_manager == null || _windowMenu == null)
        {
            GD.PushError("[WS-001] WorkspaceShell: _manager or _windowMenu is not assigned.");
            return;
        }

        // manager는 자식이므로 이 시점에 이미 _Ready에서 registry를 채웠다.
        foreach (string id in _manager.Registry.Keys)
        {
            _windowMenu.AddChild(MakeToggleButton(id));
        }

        if (_presetMenu != null)
        {
            foreach (string presetId in WorkspaceLayoutPreset.PresetIds)
            {
                _presetMenu.AddChild(MakePresetButton(presetId));
            }

            _presetMenu.AddChild(MakeGatherButton());
            _presetMenu.AddChild(MakeSimpleButton("SaveLayout", "배치 저장", () => _manager.SaveLayout()));
            _presetMenu.AddChild(MakeSimpleButton("LoadLayout", "배치 복원", () => _manager.LoadLayout()));
        }

        if (!_restoreLayoutOnReady) return;

        // 저장 배치를 복원한다(첫 실행이면 기본 preset). 종료 시 자동 저장을 위해 close 요청을 가로챈다.
        _manager.LoadLayout();

        Godot.Window root = GetTree().Root;
        GetTree().AutoAcceptQuit = false;
        root.CloseRequested += OnRootCloseRequested;
    }

    private void OnRootCloseRequested()
    {
        _manager.SaveLayout();
        GetTree().Quit();
    }

    private Button MakeSimpleButton(string name, string text, System.Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += () => onPressed();
        return button;
    }

    private Button MakePresetButton(string presetId)
    {
        var button = new Button { Name = $"Preset_{presetId}", Text = presetId };
        button.Pressed += () => _manager.ApplyPreset(presetId);
        return button;
    }

    private Button MakeGatherButton()
    {
        var button = new Button { Name = "GatherWindows", Text = "창 모아오기" };
        button.Pressed += () => _manager.GatherIntoContentArea();
        return button;
    }

    private CheckButton MakeToggleButton(string id)
    {
        var button = new CheckButton
        {
            Name = $"Toggle_{id}",
            Text = id,
            ButtonPressed = _manager.IsWindowVisible(id)
        };

        button.Toggled += pressed =>
        {
            if (pressed) _manager.ShowWindow(id);
            else _manager.HideWindow(id);
        };

        // OS close(=hide)나 최소화 동기화로 창이 사라져도 버튼이 눌린 채로 남으면
        // 메뉴에서 다시 부르려면 두 번 눌러야 한다. 창 쪽 상태를 권위로 삼아 되돌린다.
        if (_manager.TryGetWindow(id, out WorkspaceWindow window))
        {
            window.VisibilityChanged += () => button.SetPressedNoSignal(window.Visible);
        }

        return button;
    }
}
