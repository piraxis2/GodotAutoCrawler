#if TOOLS
using Godot;
using System;
using System.Collections.Generic;

namespace AutoCrawler.addons.behaviortree.debugger;

[Tool]
public partial class DebuggerWindow : Window
{
    private TabContainer _tabContainer;
    private BehaviorTreeEditor _editor;

    private HBoxContainer _targetSelectionBar;
    private OptionButton _targetOptionButton;
    private Button _startButton;
    private Button _stopButton;

    // 발견된 원격 BehaviorTree 목록: tree_path -> article_name
    private readonly Dictionary<string, string> _discoveredTrees = new();

    public event Action<string> DebugStartRequested;
    public event Action<string> DebugStopRequested;

    public override void _Ready()
    {
        _tabContainer = GetNode<TabContainer>("MarginContainer/VBoxContainer/TabContainer");
        TabBar tabBar = _tabContainer.GetTabBar();
        tabBar.TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowAlways;
        tabBar.TabClosePressed += OnTabClosePressed;
        CloseRequested += OnCloseRequested;

        BuildTargetSelectionPanel();
    }

    public void SetEditor(BehaviorTreeEditor editor)
    {
        _editor = editor;
    }

    private void BuildTargetSelectionPanel()
    {
        _targetSelectionBar = new HBoxContainer();
        _targetSelectionBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        
        var label = new Label();
        label.Text = "BehaviorTree:";
        _targetSelectionBar.AddChild(label);

        _targetOptionButton = new OptionButton();
        _targetOptionButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _targetSelectionBar.CustomMinimumSize = new Vector2(360, 0);
        _targetSelectionBar.AddChild(_targetOptionButton);

        _startButton = new Button();
        _startButton.Text = "Start";
        _startButton.Pressed += OnStartPressed;
        _targetSelectionBar.AddChild(_startButton);

        _stopButton = new Button();
        _stopButton.Text = "Stop";
        _stopButton.Pressed += OnStopPressed;
        _targetSelectionBar.AddChild(_stopButton);

        var vBox = GetNode<VBoxContainer>("MarginContainer/VBoxContainer");
        vBox.AddChild(_targetSelectionBar);
        vBox.MoveChild(_targetSelectionBar, 0);
        UpdateTargetButtons();
    }

    private void OnStartPressed()
    {
        RequestStartDebugging(GetSelectedTreePath());
    }

    private void OnStopPressed()
    {
        string treePath = GetSelectedTreePath();
        RequestStopDebugging(treePath);
        SetTabStale(treePath);
    }

    private string GetSelectedTreePath()
    {
        if (!GodotObject.IsInstanceValid(_targetOptionButton) || _targetOptionButton.Selected < 0)
        {
            return "";
        }

        return _targetOptionButton.GetItemMetadata(_targetOptionButton.Selected).AsString();
    }

    private void RequestStartDebugging(string treePath)
    {
        if (string.IsNullOrEmpty(treePath)) return;

        DebugStartRequested?.Invoke(treePath);
        if (GodotObject.IsInstanceValid(_editor))
        {
            _editor.StartDebugging(treePath);
        }
    }

    private void RequestStopDebugging(string treePath)
    {
        if (string.IsNullOrEmpty(treePath)) return;

        DebugStopRequested?.Invoke(treePath);
        if (GodotObject.IsInstanceValid(_editor))
        {
            _editor.StopDebugging(treePath);
        }
    }

    private void OnTabClosePressed(long tab)
    {
        CloseTab((int)tab);
    }

    public void CloseTab(int tabIndex)
    {
        if (!GodotObject.IsInstanceValid(_tabContainer) || tabIndex < 0 || tabIndex >= _tabContainer.GetChildCount())
        {
            return;
        }

        Node tabChild = _tabContainer.GetChild(tabIndex);
        if (tabChild != null)
        {
            if (tabChild.HasMeta("tree_path"))
            {
                string treePath = tabChild.GetMeta("tree_path").AsString();
                RequestStopDebugging(treePath);
            }
            _tabContainer.RemoveChild(tabChild);
            tabChild.QueueFree();
        }
    }

    private void OnCloseRequested()
    {
        // 닫힐 때 원격 디버깅 중인 모든 탭에 stop 메시지 발송
        foreach (var child in _tabContainer.GetChildren())
        {
            if (child.HasMeta("tree_path"))
            {
                string treePath = child.GetMeta("tree_path").AsString();
                RequestStopDebugging(treePath);
            }
        }
        QueueFree();
    }

    /// <summary>
    /// 로컬 에디터 씬 트리 구조 보기 탭을 추가합니다.
    /// 이 탭은 현재 에디터 프로세스의 scene Node 구조를 보여주며,
    /// 플레이 프로세스의 원격 tick payload 디버깅은 AddRemoteTab 경로가 담당합니다.
    /// </summary>
    public void AddTab(BehaviorTree tree)
    {
        if (!GodotObject.IsInstanceValid(tree))
        {
            GD.PrintErr("DebuggerWindow.AddTab: BehaviorTree is invalid or has been freed.");
            return;
        }

        string tabKey = tree.GetPath().ToString();
        // 중복 방지
        for (int i = 0; i < _tabContainer.GetChildCount(); i++)
        {
            var child = _tabContainer.GetChild(i);
            if (child.HasMeta("tree_path") && child.GetMeta("tree_path").AsString() == tabKey)
            {
                _tabContainer.CurrentTab = i;
                return;
            }
        }

        var splitContainer = new HSplitContainer();
        splitContainer.SetName(tree.ArticleName);
        splitContainer.SetMeta("tree_path", tabKey);
        splitContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        splitContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var debuggerTree = new DebuggerTree(tree);
        debuggerTree.CustomMinimumSize = new Vector2(250, 0);
        debuggerTree.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        splitContainer.AddChild(debuggerTree);

        var graphView = new UI.BehaviorTreeGraphView();
        graphView.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        graphView.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        splitContainer.AddChild(graphView);

        _tabContainer.AddChild(splitContainer);
        _tabContainer.SetTabTitle(_tabContainer.GetChildCount() - 1, $"{tree.ArticleName} (Local)");
        graphView.SetTree(tree);
        
        _tabContainer.CurrentTab = _tabContainer.GetChildCount() - 1;
    }

    /// <summary>
    /// 플레이 프로세스 payload를 기반으로 하는 원격 디버그 탭을 동적으로 생성합니다.
    /// </summary>
    public void AddRemoteTab(string treePath, string articleName)
    {
        // 중복 방지
        for (int i = 0; i < _tabContainer.GetChildCount(); i++)
        {
            var child = _tabContainer.GetChild(i);
            if (child.HasMeta("tree_path") && child.GetMeta("tree_path").AsString() == treePath)
            {
                _tabContainer.CurrentTab = i;
                string title = _tabContainer.GetTabTitle(i);
                if (title.EndsWith(" [STALE]"))
                {
                    _tabContainer.SetTabTitle(i, title[..^8]);
                }
                return;
            }
        }

        var splitContainer = new HSplitContainer();
        splitContainer.SetName(treePath.Replace("/", "_"));
        splitContainer.SetMeta("tree_path", treePath);
        splitContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        splitContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var debugGraphView = new UI.BehaviorTreeDebugGraphView();
        debugGraphView.Name = "DebugGraphView";
        debugGraphView.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        debugGraphView.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        splitContainer.AddChild(debugGraphView);

        _tabContainer.AddChild(splitContainer);
        _tabContainer.SetTabTitle(_tabContainer.GetChildCount() - 1, $"{articleName} [{GetTreePathTail(treePath)}] (Remote)");
        
        _tabContainer.CurrentTab = _tabContainer.GetChildCount() - 1;
    }

    /// <summary>
    /// 원격 메시지 수신 라우터
    /// </summary>
    public void HandleDebugMessage(string message, Godot.Collections.Dictionary payload)
    {
        if (payload == null) return;

        string treePath = payload.ContainsKey("tree_path") ? payload["tree_path"].AsString() : "";
        if (string.IsNullOrEmpty(treePath)) return;

        if (message == "behavior_tree:register")
        {
            string articleName = payload.ContainsKey("article_name") ? payload["article_name"].AsString() : "Unknown";
            _discoveredTrees[treePath] = articleName;
            UpdateTargetOptionButton();
        }
        else if (message == "behavior_tree:unregister")
        {
            SetTabStale(treePath);
            _discoveredTrees.Remove(treePath);
            UpdateTargetOptionButton();
        }
        else if (message == "behavior_tree:structure")
        {
            string articleName = _discoveredTrees.ContainsKey(treePath) ? _discoveredTrees[treePath] : "Remote";
            AddRemoteTab(treePath, articleName);

            var debugGraphView = FindDebugGraphView(treePath);
            if (debugGraphView != null && payload.ContainsKey("nodes") && payload["nodes"].Obj is Godot.Collections.Array nodesList)
            {
                debugGraphView.BuildGraph(nodesList);
            }
        }
        else if (message == "behavior_tree:tick")
        {
            var debugGraphView = FindDebugGraphView(treePath);
            if (debugGraphView != null)
            {
                debugGraphView.HandleDebugTick(payload);
            }
        }
    }

    private UI.BehaviorTreeDebugGraphView FindDebugGraphView(string treePath)
    {
        for (int i = 0; i < _tabContainer.GetChildCount(); i++)
        {
            var child = _tabContainer.GetChild(i);
            if (child.HasMeta("tree_path") && child.GetMeta("tree_path").AsString() == treePath)
            {
                var debugGraphView = child.GetNodeOrNull<UI.BehaviorTreeDebugGraphView>("DebugGraphView");
                if (GodotObject.IsInstanceValid(debugGraphView))
                {
                    return debugGraphView;
                }
            }
        }
        return null;
    }

    private void SetTabStale(string treePath)
    {
        if (string.IsNullOrEmpty(treePath)) return;

        for (int i = 0; i < _tabContainer.GetChildCount(); i++)
        {
            var child = _tabContainer.GetChild(i);
            if (child.HasMeta("tree_path") && child.GetMeta("tree_path").AsString() == treePath)
            {
                var debugGraphView = child.GetNodeOrNull<UI.BehaviorTreeDebugGraphView>("DebugGraphView");
                if (GodotObject.IsInstanceValid(debugGraphView))
                {
                    debugGraphView.SetStaleState();
                }
                string tabTitle = _tabContainer.GetTabTitle(i);
                if (!tabTitle.EndsWith(" [STALE]"))
                {
                    _tabContainer.SetTabTitle(i, tabTitle + " [STALE]");
                }
            }
        }
    }

    private void UpdateTargetOptionButton()
    {
        if (!GodotObject.IsInstanceValid(_targetOptionButton)) return;

        // 현재 선택 정보 백업
        string previouslySelected = "";
        if (_targetOptionButton.Selected >= 0)
        {
            previouslySelected = _targetOptionButton.GetItemMetadata(_targetOptionButton.Selected).AsString();
        }

        _targetOptionButton.Clear();

        int index = 0;
        int nextSelectIdx = -1;
        foreach (var pair in _discoveredTrees)
        {
            string treePath = pair.Key;
            string articleName = pair.Value;

            _targetOptionButton.AddItem($"{index + 1}. {articleName} — {treePath}");
            _targetOptionButton.SetItemMetadata(index, treePath);

            if (treePath == previouslySelected)
            {
                nextSelectIdx = index;
            }
            index++;
        }

        if (nextSelectIdx >= 0)
        {
            _targetOptionButton.Select(nextSelectIdx);
        }
        else if (_targetOptionButton.GetItemCount() > 0)
        {
            _targetOptionButton.Select(0);
        }

        UpdateTargetButtons();
    }

    private void UpdateTargetButtons()
    {
        bool hasSelection = GodotObject.IsInstanceValid(_targetOptionButton) && _targetOptionButton.GetItemCount() > 0;
        if (GodotObject.IsInstanceValid(_startButton))
        {
            _startButton.Disabled = !hasSelection;
        }
        if (GodotObject.IsInstanceValid(_stopButton))
        {
            _stopButton.Disabled = !hasSelection;
        }
    }

    private static string GetTreePathTail(string treePath)
    {
        if (string.IsNullOrEmpty(treePath)) return "unknown";
        int slashIndex = treePath.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex < treePath.Length - 1 ? treePath[(slashIndex + 1)..] : treePath;
    }

    public void HandleDebugTick(Godot.Collections.Dictionary payload)
    {
        // 4a legacy fallback
        HandleDebugMessage("behavior_tree:tick", payload);
    }
}
#endif
