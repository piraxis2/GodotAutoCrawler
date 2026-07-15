using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;
using Godot;
using Godot.Collections;

namespace AutoCrawler.addons.behaviortree;

[GlobalClass,Tool]
public partial class BehaviorTree : Node
{
    public BehaviorTree_Node Root => GetChildCount() > 0 ? GetChild(0) as BehaviorTree_Node : null;

    [Signal]
    public delegate void OnUpdateTreeEventHandler(BehaviorTree tree);
    
    private static readonly System.Collections.Generic.Dictionary<string, BehaviorTree> _registry = new();
    private static bool _isCaptureRegistered = false;

    public bool DebugEnabled { get; set; } = false;

    public Blackboard Blackboard { get; private set; }
    
    public string ArticleName => GetParent()?.Name;
    private bool _isUpdateRequested = false;
    // 명시적 root 교체 중에는 ChildOrderChanged의 deferred 갱신을 억제한다. InstallRoot가 Tree/child cache를
    // 동기적으로 배선하므로, 첫 tick이 이 callback에 의존하지 않는다(BT-003 Step 4, ADR-024 §8).
    private bool _isInstallingRoot;

    private static bool OnMessageCapture(string message, Godot.Collections.Array data)
    {
        if (message == "behavior_tree:start" || message == "start")
        {
            if (data.Count > 0)
            {
                string treePath = data[0].AsString();
                if (_registry.TryGetValue(treePath, out var tree) && GodotObject.IsInstanceValid(tree))
                {
                    tree.DebugEnabled = true;
                    tree.SendStructure();
                }
            }
            return true;
        }
        else if (message == "behavior_tree:stop" || message == "stop")
        {
            if (data.Count > 0)
            {
                string treePath = data[0].AsString();
                if (_registry.TryGetValue(treePath, out var tree) && GodotObject.IsInstanceValid(tree))
                {
                    tree.DebugEnabled = false;
                }
            }
            return true;
        }
        return false;
    }

    public override void _Ready()
    {
        Blackboard = new Blackboard();
        
        ChildOrderChanged += OnChildOrderChanged; 
        SetTree(Root);

        if (EngineDebugger.IsActive())
        {
            string treePath = GetPath().ToString();
            _registry[treePath] = this;

            if (!_isCaptureRegistered)
            {
                EngineDebugger.RegisterMessageCapture("behavior_tree", Callable.From<string, Godot.Collections.Array, bool>(OnMessageCapture));
                _isCaptureRegistered = true;
            }

            var payload = new Godot.Collections.Dictionary
            {
                { "tree_path", treePath },
                { "article_name", ArticleName ?? Name.ToString() }
            };
            EngineDebugger.SendMessage("behavior_tree:register", new Godot.Collections.Array { payload });
        }
    }

    public override void _ExitTree()
    {
        if (EngineDebugger.IsActive())
        {
            string treePath = GetPath().ToString();
            _registry.Remove(treePath);

            var payload = new Godot.Collections.Dictionary
            {
                { "tree_path", treePath }
            };
            EngineDebugger.SendMessage("behavior_tree:unregister", new Godot.Collections.Array { payload });
        }
    }

    private void OnChildOrderChanged()
    {
        if (_isInstallingRoot) return;
        OnUpdate();
    }

    private void SetTree(BehaviorTree_Node node)
    {
        if (node == null) return;
        
        node.Tree = this;
        foreach (var child in node.GetChildren())
        {
            if (child is BehaviorTree_Node behaviorTreeNode)
            {
                SetTree(behaviorTreeNode);
            }
        }
    }

    /// <summary>
    /// 이 컨테이너의 root를 동기적으로 교체한다. 새 root는 반드시 detached 상태여야 하며, 설치가 끝날 때까지
    /// 기존 root를 보존한다. 성공 시에만 기존 root의 tactic runtime을 reset하고 free한다.
    /// </summary>
    public bool InstallRoot(BehaviorTree_Node newRoot)
    {
        if (!GodotObject.IsInstanceValid(newRoot) || newRoot.GetParent() != null)
        {
            GD.PushError($"BehaviorTree '{Name}': detached이고 유효한 root만 설치할 수 있습니다.");
            return false;
        }

        BehaviorTree_Node oldRoot = Root;
        if (ReferenceEquals(oldRoot, newRoot))
        {
            GD.PushError($"BehaviorTree '{Name}': 같은 root를 다시 설치할 수 없습니다.");
            return false;
        }

        _isInstallingRoot = true;
        try
        {
            AddChild(newRoot);
            MoveChild(newRoot, 0);
            // _Ready/ChildOrderChanged의 deferred 순서를 기다리지 않고 현재 subtree 전체를 즉시 연결한다.
            SetTree(newRoot);
        }
        catch (Exception ex)
        {
            GD.PushError($"BehaviorTree '{Name}': root 설치 실패: {ex.Message}");
            if (GodotObject.IsInstanceValid(newRoot) && newRoot.GetParent() == this) RemoveChild(newRoot);
            return false;
        }
        finally
        {
            _isInstallingRoot = false;
        }

        // root 제거도 설치와 같은 update-suppression 구간에 둔다. 그렇지 않으면 ChildOrderChanged가
        // NotifyInstalledStructureChanged와 별도로 OnUpdateTree를 한 번 더 발생시킨다.
        _isInstallingRoot = true;
        try
        {
            if (GodotObject.IsInstanceValid(oldRoot) && oldRoot.GetParent() == this)
            {
                if (oldRoot is ITacticNode tacticRoot) tacticRoot.ResetForNewTurn();
                RemoveChild(oldRoot);
                oldRoot.Free();
            }
        }
        finally
        {
            _isInstallingRoot = false;
        }

        NotifyInstalledStructureChanged();
        return true;
    }

    /// <summary>
    /// apply owner가 자신이 설치한 root만 폐기하는 수명주기 API다. 수제 BT를 임의로 지우지 않도록 expectedRoot
    /// identity가 현재 root와 일치할 때만 동작한다.
    /// </summary>
    public bool RemoveInstalledRoot(BehaviorTree_Node expectedRoot)
    {
        if (!GodotObject.IsInstanceValid(expectedRoot) || !ReferenceEquals(Root, expectedRoot)) return false;

        _isInstallingRoot = true;
        try
        {
            if (expectedRoot is ITacticNode tacticRoot) tacticRoot.ResetForNewTurn();
            RemoveChild(expectedRoot);
            expectedRoot.Free();
        }
        finally
        {
            _isInstallingRoot = false;
        }

        NotifyInstalledStructureChanged();
        return true;
    }

    private void NotifyInstalledStructureChanged()
    {
        if (DebugEnabled) SendStructure();
        EmitSignal("OnUpdateTree", this);
    }

    public void SendStructure()
    {
        if (!EngineDebugger.IsActive()) return;

        var nodesArray = new Godot.Collections.Array();
        BuildStructurePayload(Root, nodesArray);

        var payload = new Godot.Collections.Dictionary
        {
            { "tree_path", GetPath().ToString() },
            { "nodes", nodesArray }
        };

        EngineDebugger.SendMessage("behavior_tree:structure", new Godot.Collections.Array { payload });
    }

    private void BuildStructurePayload(BehaviorTree_Node node, Godot.Collections.Array nodesArray)
    {
        if (node == null) return;

        string nodePath = GetPathTo(node).ToString();
        string parentPath = node.GetParent() is BehaviorTree_Node parentNode ? GetPathTo(parentNode).ToString() : "";
        Vector2 graphPosition = node.HasMeta("bt_graph_position") ? node.GetMeta("bt_graph_position").AsVector2() : Vector2.Zero;

        var nodeDict = new Godot.Collections.Dictionary
        {
            { "node_path", nodePath },
            { "name", node.Name.ToString() },
            { "type", node.GetType().Name },
            { "parent_path", parentPath },
            { "graph_position", graphPosition }
        };
        nodesArray.Add(nodeDict);

        foreach (var child in node.GetChildren())
        {
            if (child is BehaviorTree_Node childNode)
            {
                BuildStructurePayload(childNode, nodesArray);
            }
        }
    }

    // C#에서 EngineDebugger 전송을 위해 리포트 배치 저장
    private Godot.Collections.Array _tickReports = null;

    public void StartDebugTick()
    {
        if (DebugEnabled && EngineDebugger.IsActive())
        {
            _tickReports = new Godot.Collections.Array();
        }
    }

    public void ReportNodeExecution(string nodePath, string nodeName, string nodeType, BtStatus status, double elapsedTime)
    {
        if (_tickReports != null)
        {
            var report = new Godot.Collections.Dictionary
            {
                { "node_path", nodePath },
                { "status", (int)status },
                { "elapsed_time", elapsedTime }
            };
            _tickReports.Add(report);
        }
    }

    // 이번 tick에 선택된 택틱 행(BT-002 Step 4). 관전 HUD의 "현재 실행 행 하이라이트"와 행별 발동 통계가
    // 같은 실행 자료를 쓰도록 tick payload에 실어 보낸다. DebugEnabled가 꺼져 있으면 _tickReports가 null이라
    // 아무것도 쌓이지 않는다(debug-off 비용 계약 유지).
    private string _tickRowId;
    private int _tickRowResult = -1;

    public void ReportTacticRow(string rowId, int rowResult)
    {
        if (_tickReports == null) return;
        _tickRowId = rowId;
        _tickRowResult = rowResult;
    }

    // 이번 tick의 최종 debug payload를 만든다. 전송과 분리해 두어 EngineDebugger 없이도(headless 테스트)
    // 실제로 나가는 dictionary를 그대로 검증할 수 있다. 수집된 리포트가 없으면 null.
    public Godot.Collections.Dictionary BuildTickPayload()
    {
        if (_tickReports == null) return null;

        var payload = new Godot.Collections.Dictionary
        {
            { "tree_path", GetPath().ToString() },
            { "physics_frame", (long)Engine.GetPhysicsFrames() },
            { "nodes", _tickReports }
        };

        // 택틱 트리일 때만 실린다. 수제 BT는 이 키가 없어 기존 payload 계약이 그대로 유지된다.
        if (_tickRowId != null)
        {
            payload["tactic_row_id"] = _tickRowId;
            payload["tactic_row_result"] = _tickRowResult;
        }

        return payload;
    }

    public void EndDebugTick()
    {
        Godot.Collections.Dictionary payload = BuildTickPayload();
        if (payload == null) return;

        EngineDebugger.SendMessage("behavior_tree:tick", new Godot.Collections.Array { payload });
        _tickReports = null;
        _tickRowId = null;
        _tickRowResult = -1;
    }

    public BtStatus Behave(double delta, Node owner)
    {
        StartDebugTick();
        BtStatus status = Root?.Behave(delta, owner) ?? BtStatus.Failure;
        EndDebugTick();
        return status;
    }

    public void UpdateRequest()
    {
        if (_isUpdateRequested) return;
        _isUpdateRequested = true;
        CallDeferred(nameof(OnUpdate));
    }
    private void OnUpdate()
    {
        _isUpdateRequested = false;
        if (!IsInsideTree()) return;
        SetTree(Root);
        EmitSignal("OnUpdateTree", this);
        GD.Print($"OnUpdateTree : {Name}");
    }

    public void OnLeafNodeExecuted(BehaviorTree_Node leafNode, BtStatus status)
    {
        
    }
    
    public string GenerateMermaidGraph()
    {
        if (Root == null) return string.Empty;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("graph TD");
        GenerateMermaidGraph(Root, sb);
        DisplayServer.Singleton.ClipboardSet(sb.ToString());
        return sb.ToString();
    }

    private void GenerateMermaidGraph(BehaviorTree_Node node, StringBuilder sb)
    {
        foreach (var child in node.TreeChildren)
        {
            sb.AppendLine($"{node.Name} --> {child.Name}");
            GenerateMermaidGraph(child, sb);
        }
    }
    
    public List<BehaviorTree_Node> FindNodeByType(Type type)
    {
        return Root.FindNodeByType(type);
    }
    
    
}
