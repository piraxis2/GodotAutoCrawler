using Godot;
using System;

public partial class WindowManager : Node
{
    private const string SubWindowGroup = "sub_windows";

    // 메인 윈도우의 이전 최소화 상태를 저장할 변수
    private bool _wasMinimized = false;

    private SceneTree _sceneTree;
    public override void _Ready()
    {
        _sceneTree = GetTree();
    }

    public override void _Process(double delta)
    {

        // 매 프레임 메인 윈도우의 현재 최소화 상태를 가져옵니다.
        bool isMinimized = _sceneTree.Root.Mode == Window.ModeEnum.Minimized;

        // 이전 상태와 현재 상태가 다를 때만 로직을 실행합니다.
        if (isMinimized != _wasMinimized)
        {
            if (isMinimized)
            {
                // 창이 새로 최소화되었을 때
                MinimizeSubWindows();
            }
            else
            {
                // 창이 새로 복원되었을 때
                RestoreSubWindows();
            }

            // 현재 상태를 이전 상태로 기록합니다.
            _wasMinimized = isMinimized;
        }
    }

    private void MinimizeSubWindows()
    {
        GD.Print("Main window minimized. Minimizing sub-windows...");
        foreach (Window window in GetTree().GetNodesInGroup(SubWindowGroup))
        {
            // 서브 윈도우의 현재 모드를 'userdata'에 저장해두어 나중에 복원할 수 있게 합니다.
            window.SetMeta("previous_mode", (int)window.Mode);
            window.Mode = Window.ModeEnum.Minimized;
        }
    }

    private void RestoreSubWindows()
    {
        GD.Print("Main window restored. Restoring sub-windows...");
        foreach (Window window in GetTree().GetNodesInGroup(SubWindowGroup))
        {
            // 'userdata'에 저장해 둔 이전 모드를 가져와 복원합니다.
            if (window.HasMeta("previous_mode"))
            {
                window.Mode = (Window.ModeEnum)(int)window.GetMeta("previous_mode");
            }
            else
            {
                // 만약 저장된 모드가 없다면 기본값으로 복원합니다.
                window.Mode = Window.ModeEnum.Windowed;
            }
        }
    }
}
