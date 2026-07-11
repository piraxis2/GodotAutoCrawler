using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

/// <summary>
/// Root/Main window는 콘텐츠 화면이 아니라 작업대 관리자다(D2).
/// W0에서는 상단 드롭다운 메뉴에서 managed window와 preset/명령을 조작한다.
/// </summary>
public partial class WorkspaceShell : Node
{
    private const int FirstMenuItemId = 1;
    private const int GatherMenuItemId = 10_001;
    private const int SaveLayoutMenuItemId = 10_002;
    private const int LoadLayoutMenuItemId = 10_003;

    [Export] private WorkspaceWindowManager _manager;
    [Export] private MenuButton _windowMenu;
    [Export] private MenuButton _presetMenu;

    /// <summary>
    /// 셸 entry scene에서만 켠다. 켜지면 시작 시 저장 배치를 복원하고, 종료 시 자동 저장한다.
    /// 테스트는 트리 진입 전에 이 값을 false로 두어 raw registry 상태를 관찰한다.
    /// </summary>
    [Export] private bool _restoreLayoutOnReady;

    private readonly Dictionary<int, string> _windowItemIds = new();
    private readonly Dictionary<int, System.Action> _presetActions = new();

    public override void _Ready()
    {
        if (_manager == null || _windowMenu == null)
        {
            GD.PushError("[WS-001] WorkspaceShell: _manager or _windowMenu is not assigned.");
            return;
        }

        BuildWindowMenu();
        BuildPresetMenu();

        if (!_restoreLayoutOnReady) return;

        // 저장 배치를 복원한다(첫 실행이면 기본 preset). 종료 시 자동 저장을 위해 close 요청을 가로챈다.
        _manager.LoadLayout();
        SyncWindowMenuChecks();

        Godot.Window root = GetTree().Root;
        GetTree().AutoAcceptQuit = false;
        root.CloseRequested += OnRootCloseRequested;
    }

    private void OnRootCloseRequested()
    {
        _manager.SaveLayout();
        GetTree().Quit();
    }

    private void BuildWindowMenu()
    {
        PopupMenu popup = _windowMenu.GetPopup();
        popup.Clear();
        _windowItemIds.Clear();

        int itemId = FirstMenuItemId;
        foreach (string id in _manager.Registry.Keys)
        {
            popup.AddCheckItem(id, itemId);
            _windowItemIds[itemId] = id;

            if (_manager.TryGetWindow(id, out WorkspaceWindow window))
            {
                window.VisibilityChanged += SyncWindowMenuChecks;
            }

            itemId++;
        }

        popup.IdPressed += OnWindowMenuIdPressed;
        SyncWindowMenuChecks();
    }

    private void BuildPresetMenu()
    {
        if (_presetMenu == null) return;

        PopupMenu popup = _presetMenu.GetPopup();
        popup.Clear();
        _presetActions.Clear();

        int itemId = FirstMenuItemId;
        foreach (string presetId in WorkspaceLayoutPreset.PresetIds)
        {
            int capturedId = itemId;
            popup.AddItem(presetId, capturedId);
            _presetActions[capturedId] = () => _manager.ApplyPreset(presetId);
            itemId++;
        }

        popup.AddSeparator();
        popup.AddItem("창 모아오기", GatherMenuItemId);
        popup.AddItem("배치 저장", SaveLayoutMenuItemId);
        popup.AddItem("배치 복원", LoadLayoutMenuItemId);

        _presetActions[GatherMenuItemId] = () => _manager.GatherIntoContentArea();
        _presetActions[SaveLayoutMenuItemId] = () => _manager.SaveLayout();
        _presetActions[LoadLayoutMenuItemId] = () => _manager.LoadLayout();

        popup.IdPressed += OnPresetMenuIdPressed;
    }

    private void OnWindowMenuIdPressed(long pressedId)
    {
        int itemId = (int)pressedId;
        if (!_windowItemIds.TryGetValue(itemId, out string id)) return;

        if (_manager.IsWindowVisible(id)) _manager.HideWindow(id);
        else _manager.ShowWindow(id);

        SyncWindowMenuChecks();
    }

    private void OnPresetMenuIdPressed(long pressedId)
    {
        int itemId = (int)pressedId;
        if (_presetActions.TryGetValue(itemId, out System.Action action)) action();
        SyncWindowMenuChecks();
    }

    private void SyncWindowMenuChecks()
    {
        if (_windowMenu == null) return;

        PopupMenu popup = _windowMenu.GetPopup();
        foreach (KeyValuePair<int, string> pair in _windowItemIds)
        {
            int index = popup.GetItemIndex(pair.Key);
            if (index < 0) continue;
            popup.SetItemChecked(index, _manager.IsWindowVisible(pair.Value));
        }
    }
}

