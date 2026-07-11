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

    // --- WS-002 마스터 레이아웃 상수 (D6) ---
    // content area = 마스터 rect − 좌측 메뉴바(폭) − 하단 로그 도크(높이 비율) − 상단 타이틀바 inset.
    // .tscn의 MenuPanel/LogDock anchor가 이 상수와 일치해야 시각 배치와 clamp 판정이 어긋나지 않는다.

    /// <summary>좌측 메뉴바 폭(px). workspace.tscn MenuPanel offset_right와 일치시킨다.</summary>
    public const int MenuBarWidth = 200;

    /// <summary>하단 로그 도크 높이 비율. workspace.tscn LogDock anchor_top(=1-ratio)와 일치시킨다.</summary>
    public const float LogDockHeightRatio = 0.2f;

    /// <summary>임베디드 창 타이틀바가 마스터 상단 밖으로 잘리지 않게 남기는 content 상단 여백.</summary>
    public static readonly int ContentTopInset = WorkspaceGeometry.TitleBarHeight;

    /// <summary>슬롯 자석 거리 = content area 짧은 변의 이 비율(Step 0 리뷰 Finding 3 기본 계약).</summary>
    public const float SlotSnapRatio = 0.06f;

    /// <summary>
    /// 이 노드의 <see cref="WorkspaceWindow"/> 자식들을 _Ready에서 자동 등록한다.
    /// </summary>
    [Export] private Node _windowRoot;

    /// <summary>슬롯 스냅 드래그 중 후보 슬롯을 표시하는 오버레이(옵션, GUI 전용). 없어도 로직은 동작한다.</summary>
    [Export] private Control _slotHighlight;

    private readonly Dictionary<string, WorkspaceWindow> _registry = new();
    private WorkspaceLayoutStore _layoutStore = new();

    // 메인 창 최소화 직전에 "보이고 있던" 창 id. 숨겨진 창은 여기에 들어가지 않으므로 복원에서도 숨긴 채로 남는다.
    private readonly List<string> _visibleBeforeMinimize = new();

    private bool _wasRootMinimized;
    private bool _rootSizeChangedSubscribed;
    private bool _nativeOwnershipRefreshQueued;
    private Godot.Window _root;

    // 현재 적용된 preset. 슬롯 스냅 후보가 이 국면 기준으로 바뀐다.
    private string _currentPresetId = DefaultPresetId;

    // GUI 슬롯 드래그 상태(headless는 _Process 미실행이라 사용 안 함).
    private string _draggingWindowId;
    private readonly Dictionary<string, Vector2I> _lastWindowPos = new();

    public IReadOnlyDictionary<string, WorkspaceWindow> Registry => _registry;

    /// <summary>
    /// 현재 managed Window들이 root viewport에 임베드되는 모드인가.
    /// headless 검증은 실제 OS 창이 없으므로 기존 content 좌표 계약을 쓰는 embedded 경로로 취급한다.
    /// </summary>
    public bool IsEmbeddedMode()
    {
        if (IsHeadlessRun()) return true;
        return _root?.GuiEmbedSubwindows ?? false;
    }


    private static bool IsHeadlessRun()
    {
        return OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless" || DisplayServer.GetScreenCount() <= 0;
    }
    public override void _Ready()
    {
        _root = GetTree()?.Root;
        if (_root != null && !IsHeadlessRun()) _root.GuiEmbedSubwindows = false;

        // headless DisplayServer는 root window를 Minimized로 보고한다(기존 WindowManager autoload도 같은 로그를 찍는다).
        // 폴링을 그대로 두면 headless 실행에서 첫 프레임에 모든 창이 숨는다. 최소화 동기화는 실제 창이 있을 때만 뜻이 있다.
        SetProcess(!IsHeadlessRun());

        // 임베디드 모드에서만 마스터 리사이즈가 managed window의 유효 영역을 줄인다.
        // native pop-out 모드에서는 OS 창이 마스터 밖으로 나갈 수 있어야 하므로 자동 content 회수를 걸지 않는다.
        if (_root != null && IsEmbeddedMode())
        {
            _root.SizeChanged += OnMasterSizeChanged;
            _rootSizeChangedSubscribed = true;
        }

        if (_windowRoot == null) return;
        foreach (Node child in _windowRoot.GetChildren())
        {
            if (child is WorkspaceWindow window) RegisterWindow(window);
        }
    }

    public override void _ExitTree()
    {
        if (_root != null && _rootSizeChangedSubscribed)
        {
            _root.SizeChanged -= OnMasterSizeChanged;
            _rootSizeChangedSubscribed = false;
        }
    }

    /// <summary>마스터 리사이즈 hook. 새 content rect 기준으로 visible 창을 회수한다(도크/메뉴 비침범).</summary>
    private void OnMasterSizeChanged()
    {
        GatherIntoContentArea();
    }

    public override void _Process(double delta)
    {
        // 이 _Process는 GUI에서만 돈다(headless는 _Ready에서 SetProcess(false)). 슬롯 드래그 구동은 GUI 전용.
        if (_root == null) return;

        if (IsEmbeddedMode()) UpdateSlotDragDriver();
        else HideSlotHighlight();

        // Godot은 root window 최소화 signal을 주지 않으므로 상태 전이를 폴링한다.
        bool isMinimized = _root.Mode == Godot.Window.ModeEnum.Minimized;
        if (isMinimized == _wasRootMinimized) return;

        _wasRootMinimized = isMinimized;
        if (isMinimized) MinimizeManagedWindows();
        else RestoreManagedWindows();
    }

    /// <summary>
    /// GUI 슬롯 드래그 구동(GUI 전용). 로직은 전부 테스트된 <see cref="PreviewSlotSnap"/>/
    /// <see cref="CommitSlotSnap"/>에 위임한다. 여기서는 드래그 감지 + 하이라이트 표시만 한다.
    /// Alt를 누르면 스냅 무시(Step 0 리뷰 Finding 3 disable flag).
    /// </summary>
    private void UpdateSlotDragDriver()
    {
        bool mouseDown = Input.IsMouseButtonPressed(MouseButton.Left);
        bool snapDisabled = Input.IsKeyPressed(Key.Alt);

        if (mouseDown)
        {
            if (_draggingWindowId == null)
            {
                // 이번 프레임에 위치가 바뀐 visible 창을 드래그 대상으로 확정한다.
                foreach (KeyValuePair<string, WorkspaceWindow> entry in _registry)
                {
                    WorkspaceWindow w = entry.Value;
                    if (w.Visible && _lastWindowPos.TryGetValue(entry.Key, out Vector2I last) && last != w.Position)
                    {
                        _draggingWindowId = entry.Key;
                        break;
                    }
                }
            }

            if (_draggingWindowId != null)
                ShowSlotHighlight(PreviewSlotSnap(_draggingWindowId, snapDisabled));
        }
        else
        {
            if (_draggingWindowId != null)
            {
                CommitSlotSnap(_draggingWindowId, snapDisabled);
                _draggingWindowId = null;
            }

            HideSlotHighlight();
        }

        foreach (KeyValuePair<string, WorkspaceWindow> entry in _registry)
            _lastWindowPos[entry.Key] = entry.Value.Position;
    }

    private void ShowSlotHighlight(WorkspaceGeometry.SlotSnapResult result)
    {
        if (_slotHighlight == null) return;
        if (!result.HasCandidate) { _slotHighlight.Visible = false; return; }

        _slotHighlight.Visible = true;
        _slotHighlight.Position = result.HighlightRect.Position;
        _slotHighlight.Size = result.HighlightRect.Size;
    }

    private void HideSlotHighlight()
    {
        if (_slotHighlight != null) _slotHighlight.Visible = false;
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

        window.ConfigureAsWorkspaceChildWindow();
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
        if (!window.Visible)
        {
            window.Show();
            QueueNativeOwnershipRefresh();
        }

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
        else
        {
            window.Show();
            QueueNativeOwnershipRefresh();
        }

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
        Rect2I content = GetContentRect();
        Rect2I placementContent = GetPlacementContentRect();

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

            // 창을 먼저 표시해야 embedded window가 만들어진다. 순서를 뒤집으면 Show()가 initial_position
            // 규칙으로 좌표를 덮어쓴다. WS-002: 좌표는 screen/decoration이 아니라 content area 기준이다.
            if (layout.Visible) window.Show();
            else window.Hide();

            // 정규화 비율(E-7)을 content px로 resolve한 '보이는 영역(타이틀바 포함)' rect. 실제 client 좌표는
            // ApplyDecorationRect가 타이틀바 inset을 반영해 계산한다 — 임베디드 타이틀바가 이웃 창을 침범하지 않게.
            Rect2I localRequested = WorkspaceLayoutPreset.ResolveRect(layout.NormalizedRect, content);
            Rect2I requested = TranslateFromContentSpace(localRequested, content, placementContent);
            Rect2I target = IsEmbeddedMode()
                ? WorkspaceGeometry.GatherIntoContent(requested, placementContent)
                : requested;
            ApplyDecorationRect(window, target);
        }

        // 임베디드 모드에서는 min_size가 작은 슬롯보다 커져 content 밖으로 삐져나갈 수 있으므로
        // 한 번 더 회수한다. native pop-out 모드에서는 사용자가 마스터 밖으로 뺄 수 있어야 하므로 회수하지 않는다.
        if (IsEmbeddedMode()) GatherIntoContentArea();
        else QueueNativeOwnershipRefresh();

        _currentPresetId = presetId; // 슬롯 스냅 후보가 이 국면 기준으로 resolve된다.
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
    /// Native transient child 창은 마스터 밖 이동을 허용하지만, 복원 시 마스터가 있는 모니터의 작업영역 밖에
    /// 남아 있으면 같은 모니터 안으로 회수한다. 잘못 저장된 멀티모니터 좌표가 다음 실행까지 전파되지 않게 한다.
    /// </summary>
    private int GatherNativeWindowsToMasterScreenWorkArea()
    {
        PruneStaleEntries();
        Rect2I workArea = GetWorkArea();
        int moved = 0;

        foreach (KeyValuePair<string, WorkspaceWindow> entry in _registry)
        {
            WorkspaceWindow window = entry.Value;
            Rect2I decoRect = GetDecorationRect(window);
            if (WorkspaceGeometry.IsReachable(decoRect, workArea)) continue;

            ApplyDecorationRect(window, WorkspaceGeometry.ClampIntoWorkArea(decoRect, workArea));
            moved++;
        }

        return moved;
    }
    // --- WS-002 마스터 내부 좌표계 (D6) ---
    // 위 GatherWindows/GetWorkArea/GetDecorationRect는 WS-001 native 좌표계다(회귀 테스트가 계속 커버한다).
    // 마스터(embedded) 경로는 아래 content area 기준으로 동작한다. 창은 screen이 아니라 마스터 content
    // rect 안에서만 움직이며, 메뉴바/로그 도크를 침범하지 않는다.

    /// <summary>
    /// 마스터(root window) 크기. GUI에서는 live root.Size(리사이즈 반영)를, headless에서는 신뢰할 수 없는
    /// 작은 기본값 대신 프로젝트 viewport(=GUI 초기 창 크기)를 쓴다. minimize 폴링과 같은 headless 분기다.
    /// </summary>
    public Vector2I GetMasterSize()
    {
        if (_root != null && DisplayServer.GetName() != "headless")
        {
            Vector2I size = _root.Size;
            if (size.X > 0 && size.Y > 0) return size;
        }

        return GetProjectViewportSize();
    }

    private int GetLogDockHeight(Vector2I masterSize)
    {
        return Mathf.RoundToInt(masterSize.Y * LogDockHeightRatio);
    }

    /// <summary>메뉴바·로그 도크·상단 inset을 제외한 창 배치 영역. 슬롯 스냅/clamp의 기준 rect다.</summary>
    public Rect2I GetContentRect()
    {
        Vector2I master = GetMasterSize();
        return WorkspaceGeometry.ComputeContentRect(master, MenuBarWidth, GetLogDockHeight(master), ContentTopInset);
    }

    /// <summary>하단 전폭 로그 도크 rect. 마스터가 소유하며 저장 대상이 아니다(preset/크기에서 재계산).</summary>
    public Rect2I GetLogDockRect()
    {
        Vector2I master = GetMasterSize();
        int dockHeight = GetLogDockHeight(master);
        return new Rect2I(0, master.Y - dockHeight, master.X, dockHeight);
    }

    /// <summary>
    /// 창 배치/gather에 쓰는 content rect다. embedded는 마스터 로컬 좌표, native pop-out은 OS 화면 좌표다.
    /// </summary>
    private Rect2I GetPlacementContentRect()
    {
        Rect2I local = GetContentRect();
        if (IsEmbeddedMode()) return local;
        return new Rect2I(GetMasterScreenOrigin() + local.Position, local.Size);
    }

    private Vector2I GetMasterScreenOrigin()
    {
        if (_root != null && DisplayServer.GetName() != "headless") return _root.Position;
        return Vector2I.Zero;
    }

    private static Rect2I TranslateFromContentSpace(Rect2I rect, Rect2I fromContent, Rect2I toContent)
    {
        return new Rect2I(toContent.Position + (rect.Position - fromContent.Position), rect.Size);
    }
    /// <summary>
    /// D6. content area 밖으로 나간 창을 회수한다. 판정·clamp는 마스터 content rect 기준 client rect다.
    /// 회수 후 창은 메뉴바/로그 도크를 침범하지 않는다. 이미 안에 있는 창은 건드리지 않는다(멱등).
    /// </summary>
    /// <returns>실제로 옮기거나 줄인 창 수.</returns>
    public int GatherIntoContentArea()
    {
        PruneStaleEntries();
        Rect2I content = GetContentRect();
        Rect2I placementContent = GetPlacementContentRect();
        int moved = 0;

        foreach (KeyValuePair<string, WorkspaceWindow> entry in _registry)
        {
            WorkspaceWindow window = entry.Value;
            // 판정·clamp는 타이틀바 포함 '보이는 영역' 기준이다(임베디드 타이틀바가 도크/메뉴/이웃을 침범 방지).
            Rect2I decoRect = GetDecorationRect(window);

            Rect2I gathered = WorkspaceGeometry.GatherIntoContent(decoRect, placementContent);
            if (gathered == decoRect) continue;

            ApplyDecorationRect(window, gathered);
            moved++;
        }

        return moved;
    }

    // --- WS-002 Step 3: 슬롯 스냅 (D5) ---

    /// <summary>현재 적용된 preset id. 슬롯 후보가 이 국면 기준이다.</summary>
    public string CurrentPresetId => _currentPresetId;

    /// <summary>현재 국면 preset의 visible 슬롯 rect(content 기준). 슬롯 스냅 후보의 소스다.</summary>
    public IReadOnlyDictionary<string, Rect2I> GetCurrentSlots()
    {
        return WorkspaceLayoutPreset.ResolveSlots(_currentPresetId, GetContentRect());
    }

    /// <summary>자석 거리(px) = content area 짧은 변 × <see cref="SlotSnapRatio"/>.</summary>
    public int GetSnapDistance()
    {
        Rect2I content = GetContentRect();
        return Mathf.RoundToInt(Mathf.Min(content.Size.X, content.Size.Y) * SlotSnapRatio);
    }

    /// <summary>
    /// 드래그 중인 창에 대한 슬롯 스냅 후보를 계산한다(하이라이트용). 창을 옮기지 않는다.
    /// <paramref name="snapDisabled"/>면 항상 후보 없음이다(스냅 무시 조작).
    /// </summary>
    public WorkspaceGeometry.SlotSnapResult PreviewSlotSnap(string draggedWindowId, bool snapDisabled)
    {
        if (!TryGetWindow(draggedWindowId, out WorkspaceWindow window))
            return WorkspaceGeometry.SlotSnapResult.None;

        // 슬롯과 동일한 '보이는 영역(decoration)' 좌표로 거리를 판정한다.
        Rect2I dragged = GetDecorationRect(window);
        return WorkspaceGeometry.FindSlotSnap(
            dragged, GetCurrentSlots(), GetSnapDistance(), GetContentRect(), snapDisabled);
    }

    /// <summary>
    /// 드래그 종료 시 후보가 있으면 창을 슬롯 rect(content 안으로 clamp)로 정렬한다.
    /// </summary>
    /// <returns>실제로 스냅했으면 true.</returns>
    public bool CommitSlotSnap(string draggedWindowId, bool snapDisabled)
    {
        if (!TryGetWindow(draggedWindowId, out WorkspaceWindow window)) return false;

        WorkspaceGeometry.SlotSnapResult result = PreviewSlotSnap(draggedWindowId, snapDisabled);
        if (!result.HasCandidate) return false;

        ApplyDecorationRect(window, result.AppliedRect);
        return true;
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
            if (IsEmbeddedMode()) GatherIntoContentArea();
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

        // embedded mode에서는 저장된 좌표가 현재 마스터 content 밖일 수 있다(마스터 리사이즈).
        // native transient mode에서는 마스터 밖 좌표는 허용하되, 마스터가 있는 모니터 밖 좌표는 회수한다.
        if (IsEmbeddedMode()) GatherIntoContentArea();
        else
        {
            GatherNativeWindowsToMasterScreenWorkArea();
            QueueNativeOwnershipRefresh();
        }

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

        QueueNativeOwnershipRefresh();
        _visibleBeforeMinimize.Clear();
    }

    private void QueueNativeOwnershipRefresh()
    {
        if (_nativeOwnershipRefreshQueued || _root == null || IsHeadlessRun() || IsEmbeddedMode()) return;

        _nativeOwnershipRefreshQueued = true;
        CallDeferred(nameof(RefreshNativeOwnership));
    }

    public void RefreshNativeOwnership()
    {
        _nativeOwnershipRefreshQueued = false;
        if (_root == null || IsHeadlessRun() || IsEmbeddedMode()) return;

        foreach (KeyValuePair<string, WorkspaceWindow> entry in _registry)
        {
            WorkspaceWindow window = entry.Value;
            if (!window.Visible) continue;
            WorkspaceNativeWindowOwner.TryAttachToMaster(window, _root);
        }
    }

    private static bool IsUsable(WorkspaceWindow window)
    {
        return GodotObject.IsInstanceValid(window) && !window.IsQueuedForDeletion();
    }
}














