#if TOOLS
using System;
using System.Collections.Generic;
using Godot;

namespace AutoCrawler.addons.behaviortree.UI;

/// <summary>
/// 에디터 로컬 씬 노드 의존성 없이, 플레이 프로세스로부터 받은 structure 및 tick payload를 
/// 기반으로 디버그 그래프를 동적으로 구성하고 상태를 하이라이트하는 완전 읽기 전용 뷰입니다.
/// </summary>
[Tool]
public partial class BehaviorTreeDebugGraphView : MarginContainer
{
    private GraphEdit _graphEdit;
    
    // node_path -> GraphNode 인스턴스 매핑 캐시
    private readonly Dictionary<string, GraphNode> _nodesMap = new();
    
    // node_path -> 노드 타입별 기본 색상 캐시 (틱마다 재검증/재계산 방지)
    private readonly Dictionary<string, Color> _nodeDefaultColors = new();

    public override void _Ready()
    {
        // 1. GraphEdit 동적 생성 및 꽉 차게 배치
        _graphEdit = new GraphEdit();
        _graphEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _graphEdit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        
        // 디버그 뷰는 완전 읽기 전용이므로 편집 관련 상호작용 비활성화
        _graphEdit.RightDisconnects = false;
        _graphEdit.ConnectionRequest += (fromNode, fromPort, toNode, toPort) => {};
        _graphEdit.DisconnectionRequest += (fromNode, fromPort, toNode, toPort) => {};
        
        AddChild(_graphEdit);
        
        // 여백 설정
        AddThemeConstantOverride("margin_top", 10);
        AddThemeConstantOverride("margin_bottom", 10);
        AddThemeConstantOverride("margin_left", 10);
        AddThemeConstantOverride("margin_right", 10);
    }

    /// <summary>
    /// Godot Node Name 규칙에 맞게 node_path를 정제된 그래프 노드 명칭으로 변환합니다.
    /// </summary>
    private string GetGraphNodeName(string nodePath)
    {
        return "BTDebugNode_" + nodePath.Replace("/", "_").Replace(":", "_").Replace("@", "_").Replace(".", "_");
    }

    /// <summary>
    /// 기존 그래프 구성원을 완전히 클리어합니다.
    /// </summary>
    private void ClearGraph()
    {
        if (_graphEdit == null) return;

        _graphEdit.ClearConnections();
        foreach (var child in _graphEdit.GetChildren())
        {
            if (child is GraphNode graphNode)
            {
                _graphEdit.RemoveChild(graphNode);
                graphNode.QueueFree();
            }
        }
        _nodesMap.Clear();
        _nodeDefaultColors.Clear();
    }

    /// <summary>
    /// structure payload의 노드 목록 정보를 바탕으로 디버그 그래프 노드 및 연결선을 동적으로 구축합니다.
    /// </summary>
    public void BuildGraph(Godot.Collections.Array nodesList)
    {
        ClearGraph();
        if (_graphEdit == null || nodesList == null || nodesList.Count == 0) return;

        // 1. Layout을 위한 깊이(depth) 계산 인프라 구축
        var depths = new Dictionary<string, int>();
        var depthLists = new Dictionary<int, List<string>>();
        var childrenMap = new Dictionary<string, List<string>>();
        string rootPath = null;

        // 트리 계층 수집
        foreach (var nodeObj in nodesList)
        {
            if (nodeObj.Obj is Godot.Collections.Dictionary nodeDict)
            {
                string path = nodeDict.ContainsKey("node_path") ? nodeDict["node_path"].AsString() : "";
                string parentPath = nodeDict.ContainsKey("parent_path") ? nodeDict["parent_path"].AsString() : "";

                if (string.IsNullOrEmpty(path)) continue;

                if (string.IsNullOrEmpty(parentPath))
                {
                    rootPath = path;
                }
                else
                {
                    if (!childrenMap.ContainsKey(parentPath))
                    {
                        childrenMap[parentPath] = new List<string>();
                    }
                    childrenMap[parentPath].Add(path);
                }
            }
        }

        // BFS 레이아웃 좌표 연산
        if (rootPath != null)
        {
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((rootPath, 0));

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                string path = current.Path;
                int depth = current.Depth;

                if (depths.ContainsKey(path)) continue;

                depths[path] = depth;
                if (!depthLists.ContainsKey(depth))
                {
                    depthLists[depth] = new List<string>();
                }
                depthLists[depth].Add(path);

                if (childrenMap.TryGetValue(path, out var children))
                {
                    foreach (var child in children)
                    {
                        queue.Enqueue((child, depth + 1));
                    }
                }
            }
        }

        // 2. GraphNode 동적 인스턴스 생성 및 배치
        foreach (var nodeObj in nodesList)
        {
            if (nodeObj.Obj is Godot.Collections.Dictionary nodeDict)
            {
                string nodePath = nodeDict.ContainsKey("node_path") ? nodeDict["node_path"].AsString() : "";
                string name = nodeDict.ContainsKey("name") ? nodeDict["name"].AsString() : "Unknown";
                string type = nodeDict.ContainsKey("type") ? nodeDict["type"].AsString() : "BehaviorTree_Node";
                string parentPath = nodeDict.ContainsKey("parent_path") ? nodeDict["parent_path"].AsString() : "";
                Vector2 graphPosition = nodeDict.ContainsKey("graph_position") ? nodeDict["graph_position"].AsVector2() : Vector2.Zero;

                if (string.IsNullOrEmpty(nodePath)) continue;

                var graphNode = new GraphNode();
                graphNode.Name = GetGraphNodeName(nodePath);
                graphNode.Title = name;
                graphNode.CustomMinimumSize = new Vector2(240, 95);

                // Type 설명 라벨
                var typeLabel = new Label();
                typeLabel.Text = $"Type: {type}";
                typeLabel.HorizontalAlignment = HorizontalAlignment.Center;
                graphNode.AddChild(typeLabel);

                // 경과 시간 정보 출력용 라벨 (틱 수신 시 실시간 반영)
                var elapsedLabel = new Label();
                elapsedLabel.Name = "ElapsedTimeLabel";
                elapsedLabel.Text = "Elapsed: 0.00s";
                elapsedLabel.HorizontalAlignment = HorizontalAlignment.Center;
                graphNode.AddChild(elapsedLabel);

                // 노드 타입별 디폴트 색상 계산 및 캐싱
                Color defaultColor = Colors.White;
                if (type.Contains("Selector") || type.Contains("Sequence") || type.Contains("Composite"))
                {
                    defaultColor = new Color(0.8f, 0.95f, 1f); // Composite: 연한 파랑
                }
                else if (type.Contains("Decorator") || type.Contains("Limit") || type.Contains("Cooldown"))
                {
                    defaultColor = new Color(1f, 0.95f, 0.8f); // Decorator: 연한 주황
                }
                else
                {
                    defaultColor = new Color(0.8f, 1f, 0.8f); // Action: 연한 녹색
                }
                _nodeDefaultColors[nodePath] = defaultColor;
                graphNode.SelfModulate = defaultColor;

                // 슬롯 설정 (루트 여부 및 자식 노드 존재 여부 확인)
                bool enableInput = !string.IsNullOrEmpty(parentPath);
                bool enableOutput = childrenMap.ContainsKey(nodePath) && childrenMap[nodePath].Count > 0;

                // 포트 비주얼 색상
                Color portColor = defaultColor;
                graphNode.SetSlot(0, enableInput, 0, portColor, enableOutput, 0, portColor);

                // 노드 좌표 결정 (메타데이터 좌표 우선, 없으면 BFS 레이아웃 fallback)
                if (graphPosition != Vector2.Zero)
                {
                    graphNode.PositionOffset = graphPosition;
                }
                else if (depths.TryGetValue(nodePath, out int d))
                {
                    var siblings = depthLists[d];
                    int xIndex = siblings.IndexOf(nodePath);
                    int totalCount = siblings.Count;

                    float xPos = (xIndex - (totalCount - 1) / 2.0f) * 260.0f + 400.0f;
                    float yPos = d * 180.0f + 50.0f;
                    graphNode.PositionOffset = new Vector2(xPos, yPos);
                }

                _graphEdit.AddChild(graphNode);
                _nodesMap[nodePath] = graphNode;
            }
        }

        // 3. 연결선(Connection) 설정 복원
        foreach (var nodeObj in nodesList)
        {
            if (nodeObj.Obj is Godot.Collections.Dictionary nodeDict)
            {
                string nodePath = nodeDict.ContainsKey("node_path") ? nodeDict["node_path"].AsString() : "";
                string parentPath = nodeDict.ContainsKey("parent_path") ? nodeDict["parent_path"].AsString() : "";

                if (string.IsNullOrEmpty(nodePath) || string.IsNullOrEmpty(parentPath)) continue;

                if (_nodesMap.ContainsKey(parentPath) && _nodesMap.ContainsKey(nodePath))
                {
                    string parentNodeName = GetGraphNodeName(parentPath);
                    string childNodeName = GetGraphNodeName(nodePath);
                    _graphEdit.ConnectNode(parentNodeName, 0, childNodeName, 0);
                }
            }
        }
    }

    /// <summary>
    /// 디버그 틱 리포트를 수신하여 노드의 틱 하이라이트 상태를 갱신합니다.
    /// </summary>
    public void HandleDebugTick(Godot.Collections.Dictionary payload)
    {
        if (_graphEdit == null || payload == null) return;

        if (payload.ContainsKey("nodes") && payload["nodes"].Obj is Godot.Collections.Array nodesList)
        {
            // 틱 갱신 전 모든 노드를 기본 중립 색상으로 원복 (stale clear)
            ResetHighlights();

            // 이번 틱에 들어온 보고서로 하이라이트 설정
            foreach (var reportObj in nodesList)
            {
                if (reportObj.Obj is Godot.Collections.Dictionary report)
                {
                    string nodePath = report.ContainsKey("node_path") ? report["node_path"].AsString() : "";
                    BtStatus status = report.ContainsKey("status") ? (BtStatus)report["status"].AsInt32() : BtStatus.Failure;
                    double elapsedTime = report.ContainsKey("elapsed_time") ? report["elapsed_time"].AsDouble() : 0.0;

                    if (string.IsNullOrEmpty(nodePath)) continue;

                    if (_nodesMap.TryGetValue(nodePath, out var graphNode) && GodotObject.IsInstanceValid(graphNode))
                    {
                        // 1) 상태별 하이라이트 색상 설정
                        switch (status)
                        {
                            case BtStatus.Success:
                                graphNode.SelfModulate = new Color(0.2f, 0.8f, 0.2f); // 초록
                                break;
                            case BtStatus.Failure:
                                graphNode.SelfModulate = new Color(0.9f, 0.3f, 0.3f); // 빨강
                                break;
                            case BtStatus.Running:
                                graphNode.SelfModulate = new Color(0.3f, 0.6f, 1.0f); // 파랑
                                break;
                        }

                        // 2) 경과 시간 텍스트 업데이트
                        var elapsedLabel = graphNode.GetNodeOrNull<Label>("ElapsedTimeLabel");
                        if (elapsedLabel != null)
                        {
                            elapsedLabel.Text = $"Elapsed: {elapsedTime:F2}s";
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 모든 그래프 노드를 캐시된 디폴트 기본 색상으로 복구시킵니다.
    /// </summary>
    private void ResetHighlights()
    {
        foreach (var pair in _nodesMap)
        {
            string nodePath = pair.Key;
            GraphNode graphNode = pair.Value;

            if (GodotObject.IsInstanceValid(graphNode) && _nodeDefaultColors.TryGetValue(nodePath, out Color defaultColor))
            {
                graphNode.SelfModulate = defaultColor;
            }
        }
    }

    /// <summary>
    /// 원격 트리가 소멸되거나 디버깅이 중단되었을 때 탭 화면을 stale(회색조) 상태로 잠급니다.
    /// </summary>
    public void SetStaleState()
    {
        foreach (var pair in _nodesMap)
        {
            GraphNode graphNode = pair.Value;
            if (GodotObject.IsInstanceValid(graphNode))
            {
                // 노드를 흐릿한 회색조로 모듈레이트
                graphNode.SelfModulate = new Color(0.5f, 0.5f, 0.5f, 0.8f);

                var elapsedLabel = graphNode.GetNodeOrNull<Label>("ElapsedTimeLabel");
                if (elapsedLabel != null && !elapsedLabel.Text.EndsWith(" [STALE]"))
                {
                    elapsedLabel.Text += " [STALE]";
                }
            }
        }
    }
}
#endif
