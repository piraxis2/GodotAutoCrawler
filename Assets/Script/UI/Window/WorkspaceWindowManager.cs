using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.UI.Window;

/// <summary>
/// WS-001 W0 셸의 managed window registry다.
///
/// 기존 <c>WindowManager</c> autoload와 책임이 겹치지 않는다. 이 manager는 셸 entry scene
/// (<c>res://Assets/Scenes/workspace.tscn</c>) 하위 노드이며, managed window는 autoload가 보는
/// <c>sub_windows</c> 그룹에 등록하지 않는다. 최소화 동기화는 이 manager가 자기 registry만 보고 수행한다.
/// </summary>
public partial class WorkspaceWindowManager : Node
{
    /// <summary>배치를 복원할 파일이 없거나 손상됐을 때 적용하는 기본 preset.</summary>
    public const string DefaultPresetId = WorkspaceLayoutPreset.Outgame;

    /// <summary>
    /// 이 노드의 <see cref="WorkspaceWindow"/> 자식들을 _Ready에서 자동 등록한다.
    /// </summary>
    [Export] private Node _windowRoot;

    private readonly Dictionary<string, WorkspaceWindow> _registry = new();
    private WorkspaceLayoutStore _layoutStore = new();

    // 메인 창 최소화 직전에 "보이고 있던" 창 id. 숨겨진 창은 여기에 들어가지 않으므로 복원에서도 숨긴 채로 남는다.
    private readonly List<string> _visibleBeforeMinimize = new();

    private bool _wasRootMinimized;
    private Godot.Window _root;

    public IReadOnlyDictionary<string, WorkspaceWindow> Registry => _registry;

    public override void _Ready()
    {
        _root = GetTree()?.Root;

        // headless DisplayServer는 root window를 Minimized로 보고한다(기존 WindowManager autoload도 같은 로그를 찍는다).
        // 폴링을 그대로 두면 headless 실행에서 첫 프레임에 모든 창이 숨는다. 최소화 동기화는 실제 창이 있을 때만 뜻이 있다.
        SetProcess(DisplayServer.GetName() != "headless");

        if (_windowRoot == null) return;
        foreach (Node child in _windowRoot.GetChildren())
        {
            if (child is WorkspaceWindow window) RegisterWindow(window);
        }
    }

    public override void _Process(double delta)
    {
        // Godot은 root window 최소화 signal을 주지 않으므로 상태 전이를 폴링한다.
        if (_root == null) return;

        bool isMinimized = _root.Mode == Godot.Window.ModeEnum.Minimized;
        if (isMinimized == _wasRootMinimized) return;

        _wasRootMinimized = isMinimized;
        if (isMinimized) MinimizeManagedWindows();
        else RestoreManagedWindows();
    }

    /// <returns>새 entry가 만들어졌으면 true. 중복 id, 빈 id, 재등록은 false다(fail-closed).</returns>
    public bool RegisterWindow(WorkspaceWindow window)
    {
        if (!IsUsable(window))
        {
            GD.PushError("[WS-001] RegisterWindow: window is null or freed.");
            return false;
        }

        string id = window.WindowId;
        if (string.IsNullOrEmpty(id))
        {
            GD.PushError($"[WS-001] RegisterWindow: empty WindowId on '{window.Name}'.");
            return false;
        }

        PruneStaleEntries();

        if (_registry.TryGetValue(id, out WorkspaceWindow existing))
        {
            if (existing == window) return false; // 같은 인스턴스 재등록은 중복 entry를 만들지 않는다.
            GD.PushError($"[WS-001] RegisterWindow: duplicate WindowId '{id}'. Keeping the first registration.");
            return false;
        }

        _registry[id] = window;
        return true;
    }

    public bool UnregisterWindow(string id)
    {
        return _registry.Remove(id);
    }

    /// <summary>
    /// 창을 표시한다. 이미 보이는 창에 호출해도 새 창을 만들거나 position/size를 되돌리지 않는다.
    /// </summary>
    public bool ShowWindow(string id)
    {
        if (!TryGetWindow(id, out WorkspaceWindow window)) return false;
        if (!window.Visible) window.Show();
        return true;
    }

    public bool HideWindow(string id)
    {
        if (!TryGetWindow(id, out WorkspaceWindow window)) return false;
        if (window.Visible) window.Hide();
        return true;
    }

    public bool ToggleWindow(string id)
    {
        if (!TryGetWindow(id, out WorkspaceWindow window)) return false;
        if (window.Visible) window.Hide();
        else window.Show();
        return true;
    }

    public bool IsWindowVisible(string id)
    {
        return TryGetWindow(id, out WorkspaceWindow window) && window.Visible;
    }

    /// <summary>
    /// 살아 있는 window만 돌려준다. 미등록 id와 freed/stale entry는 false다(fail-closed).
    /// stale entry는 여기서 registry에서 제거된다.
    /// </summary>
    public bool TryGetWindow(string id, out WorkspaceWindow window)
    {
        window = null;
        if (string.IsNullOrEmpty(id)) return false;
        if (!_registry.TryGetValue(id, out WorkspaceWindow found)) return false;

        if (!IsUsable(found))
        {
            _registry.Remove(id);
            GD.PushWarning($"[WS-001] TryGetWindow: dropped stale entry '{id}'.");
            return false;
        }

        window = found;
        return true;
    }

    /// <summary>freed/queued-for-deletion window entry를 registry에서 제거한다.</summary>
    public int PruneStaleEntries()
    {
        List<string> stale = null;
        foreach (KeyValuePair<string, WorkspaceWindow> entry in _registry)
        {
            if (IsUsable(entry.Value)) continue;
            stale ??= new List<string>();
            stale.Add(entry.Key);
        }

        if (stale == null) return 0;
        foreach (string id in stale) _registry.Remove(id);
        return stale.Count;
    }

    /// <summary>
    /// D3. preset은 창들의 {visible, position, size, content_mode}를 적용하는 명령이다.
    /// 창을 새로 만들지 않는다. 적용 후 각 창은 work area 안으로 회수된다.
    /// </summary>
    /// <returns>unknown preset id면 아무것도 바꾸지 않고 false다(fail-closed).</returns>
    public bool ApplyPreset(string presetId)
    {
        if (!WorkspaceLayoutPreset.TryGetPreset(presetId, out IReadOnlyDictionary<string, WindowLayout> layouts))
        {
            GD.PushError($"[WS-001] ApplyPreset: unknown preset '{presetId}'.");
            return false;
        }

        PruneStaleEntries();
        Rect2I workArea = GetWorkArea();

        foreach (KeyValuePair<string, WindowLayout> entry in layouts)
        {
            if (!TryGetWindow(entry.Key, out WorkspaceWindow window))
            {
                // preset이 아직 없는 창(후속 TacticBoard 등)을 참조해도 나머지 적용을 막지 않는다.
                GD.PushWarning($"[WS-001] ApplyPreset: preset '{presetId}' references unknown window '{entry.Key}'.");
                continue;
            }

            WindowLayout layout = entry.Value;
            if (layout.ContentMode != null) window.ContentMode = layout.ContentMode;

            // 창을 먼저 표시해야 native window가 만들어져 실제 decoration 크기를 읽을 수 있다.
            // 순서를 뒤집으면 Show()가 initial_position 규칙으로 좌표를 다시 덮어쓴다.
            if (layout.Visible) window.Show();
            else window.Hide();

            Vector2I decoPosition = workArea.Position + layout.Offset;
            Rect2I requested = new(decoPosition, layout.Size + GetDecorationExtra(window));
            ApplyDecorationRect(window, WorkspaceGeometry.Gather(requested, workArea));
        }

        return true;
    }

    /// <summary>
    /// D5. 화면 밖으로 밀린 창을 회수한다. 판정과 clamp는 decoration 포함 rect 기준이다.
    /// 대상 screen은 창 중심이 포함된 screen이고, 없으면 primary다.
    /// </summary>
    /// <returns>실제로 옮기거나 줄인 창 수.</returns>
    public int GatherWindows()
    {
        PruneStaleEntries();

        List<Rect2I> usableRects = GetScreenUsableRects();
        int primary = DisplayServer.GetPrimaryScreen();
        int moved = 0;

        foreach (KeyValuePair<string, WorkspaceWindow> entry in _registry)
        {
            WorkspaceWindow window = entry.Value;
            Rect2I decoRect = GetDecorationRect(window);

            int screen = WorkspaceGeometry.ResolveTargetScreen(decoRect, usableRects, primary);
            Rect2I workArea = usableRects[screen];
            if (WorkspaceGeometry.IsReachable(decoRect, workArea)) continue;

            ApplyDecorationRect(window, WorkspaceGeometry.ClampIntoWorkArea(decoRect, workArea));
            moved++;
        }

        return moved;
    }

    /// <summary>
    /// 현재 창 배치를 `user://workspace_layout.cfg`에 저장한다. 각 창의 client position/size/visible/content_mode를 기록한다.
    /// </summary>
    /// <returns>Godot `Error.Ok`면 성공.</returns>
    public Error SaveLayout()
    {
        PruneStaleEntries();

        var snapshots = new Dictionary<string, WindowSnapshot>();
        foreach (KeyValuePair<string, WorkspaceWindow> entry in _registry)
        {
            WorkspaceWindow window = entry.Value;
            snapshots[entry.Key] = new WindowSnapshot(
                window.Visible, window.Position, window.Size, window.ContentMode);
        }

        return _layoutStore.Save(snapshots);
    }

    /// <summary>
    /// 저장된 배치를 복원한다. 파일이 없거나(첫 실행) 손상됐거나 version이 맞지 않으면 기본 preset으로 fallback한다.
    /// 복원 후에는 항상 gather를 돌려, 저장 당시와 화면 구성이 달라 밖으로 나간 창을 회수한다.
    /// </summary>
    /// <returns>어떤 경로로 복원했는지. 호출자·테스트가 first-run/corrupt/normal을 구분할 수 있다.</returns>
    public LayoutLoadStatus LoadLayout()
    {
        PruneStaleEntries();
        LayoutLoadResult result = _layoutStore.Load();

        if (!result.HasUsableSnapshots)
        {
            // first-run / corrupt / unsupported version. 손상 파일은 파괴하지 않고 기본 배치만 적용한다.
            ApplyPreset(DefaultPresetId);
            GatherWindows();
            return result.Status;
        }

        foreach (KeyValuePair<string, WindowSnapshot> entry in result.Snapshots)
        {
            if (!TryGetWindow(entry.Key, out WorkspaceWindow window)) continue;

            WindowSnapshot snapshot = entry.Value;
            window.ContentMode = snapshot.ContentMode;
            window.InitialPosition = Godot.Window.WindowInitialPosition.Absolute;

            if (snapshot.Visible) window.Show();
            else window.Hide();

            window.Size = snapshot.Size.Max(window.MinSize == Vector2I.Zero ? Vector2I.One : window.MinSize);
            window.Position = snapshot.Position;
        }

        // 저장된 좌표가 현재 화면 밖일 수 있다(모니터 구성/해상도 변경). decoration 포함 rect 기준으로 회수한다.
        GatherWindows();
        return result.Status;
    }

    /// <summary>저장 파일 경로. 테스트/진단용.</summary>
    public string LayoutStorePath => _layoutStore.Path;

    /// <summary>저장 파일 경로를 바꾼다. 테스트 격리용(user://settings 등 실제 파일 오염 방지).</summary>
    public void UseLayoutStorePath(string path)
    {
        _layoutStore = new WorkspaceLayoutStore(path);
    }

    /// <summary>root window가 놓인 screen의 work area. preset 좌표의 원점이다.</summary>
    public Rect2I GetWorkArea()
    {
        int screen = _root?.CurrentScreen ?? DisplayServer.GetPrimaryScreen();
        return SanitizeWorkArea(DisplayServer.ScreenGetUsableRect(screen));
    }

    /// <summary>
    /// headless DisplayServer는 screen이 0개이고 usable rect가 비어 있다(실측: `screen_count = 0`).
    /// 그 값을 그대로 쓰면 preset이 음수 좌표로 무너지므로, 프로젝트 viewport 크기를 work area로 가정한다.
    /// </summary>
    private static Rect2I SanitizeWorkArea(Rect2I usable)
    {
        if (usable.Size.X > 0 && usable.Size.Y > 0) return usable;
        return new Rect2I(Vector2I.Zero, GetProjectViewportSize());
    }

    private static Vector2I GetProjectViewportSize()
    {
        var width = (int)ProjectSettings.GetSetting("display/window/size/viewport_width", 1152);
        var height = (int)ProjectSettings.GetSetting("display/window/size/viewport_height", 648);
        return new Vector2I(Mathf.Max(width, 1), Mathf.Max(height, 1));
    }

    private static List<Rect2I> GetScreenUsableRects()
    {
        int count = DisplayServer.GetScreenCount();
        if (count <= 0) return new List<Rect2I> { SanitizeWorkArea(new Rect2I()) };

        var rects = new List<Rect2I>(count);
        for (int i = 0; i < count; i++) rects.Add(SanitizeWorkArea(DisplayServer.ScreenGetUsableRect(i)));
        return rects;
    }

    // Godot Window.Position/Size는 client 영역 기준이다. decoration 크기를 못 얻는 환경에서는
    // WorkspaceGeometry의 보수적 fallback inset을 쓴다.
    private static Vector2I GetLeadingInset(Godot.Window window)
    {
        Vector2I inset = window.Position - window.GetPositionWithDecorations();
        return inset == Vector2I.Zero ? WorkspaceGeometry.FallbackLeadingInset : inset;
    }

    private static Vector2I GetDecorationExtra(Godot.Window window)
    {
        Vector2I extra = window.GetSizeWithDecorations() - window.Size;
        return extra == Vector2I.Zero ? WorkspaceGeometry.FallbackTotalExtra : extra;
    }

    /// <summary>창의 decoration 포함 rect. gather/preset 판정의 유일한 좌표계다.</summary>
    public static Rect2I GetDecorationRect(Godot.Window window)
    {
        return new Rect2I(window.Position - GetLeadingInset(window), window.Size + GetDecorationExtra(window));
    }

    private static void ApplyDecorationRect(Godot.Window window, Rect2I decoRect)
    {
        // 좌표를 코드가 정한 뒤에는 Godot의 initial_position 규칙(중앙 정렬 등)이 다음 Show()에서
        // 그 좌표를 덮어쓰면 안 된다.
        window.InitialPosition = Godot.Window.WindowInitialPosition.Absolute;

        Vector2I clientSize = decoRect.Size - GetDecorationExtra(window);
        clientSize = clientSize.Max(window.MinSize == Vector2I.Zero ? Vector2I.One : window.MinSize);

        window.Size = clientSize;
        window.Position = decoRect.Position + GetLeadingInset(window);
    }

    /// <summary>메인 창 최소화 동기화. 보이던 창만 숨기고 그 목록을 기억한다.</summary>
    public void MinimizeManagedWindows()
    {
        PruneStaleEntries();
        _visibleBeforeMinimize.Clear();

        foreach (KeyValuePair<string, WorkspaceWindow> entry in _registry)
        {
            if (!entry.Value.Visible) continue; // 숨겨둔 창은 복원 대상이 아니다.
            _visibleBeforeMinimize.Add(entry.Key);
            entry.Value.Hide();
        }
    }

    /// <summary>메인 창 복원 동기화. 최소화 직전에 보이던 창만 다시 표시한다.</summary>
    public void RestoreManagedWindows()
    {
        foreach (string id in _visibleBeforeMinimize)
        {
            if (TryGetWindow(id, out WorkspaceWindow window)) window.Show();
        }

        _visibleBeforeMinimize.Clear();
    }

    private static bool IsUsable(WorkspaceWindow window)
    {
        return GodotObject.IsInstanceValid(window) && !window.IsQueuedForDeletion();
    }
}
