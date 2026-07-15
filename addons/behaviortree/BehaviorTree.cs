using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
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
