#if TOOLS
using System;
using System.Collections.Generic;
using Godot;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.addons.behaviortree.node.Rating;

namespace AutoCrawler.addons.behaviortree.UI;

[Tool]
public partial class BehaviorTreeGraphView : MarginContainer
{
    private GraphEdit _graphEdit;
    private BehaviorTree _tree;
    private List<Node> _subscribedNodes = new();

    private PopupMenu _contextMenu;
    private Vector2 _clickPosition;

    public override void _Ready()
    {
        // 1. GraphEdit 동적 생성 및 배치
        _graphEdit = new GraphEdit();
        _graphEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _graphEdit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _graphEdit.RightDisconnects = true; // 우측 포트에서 선을 끌어 해제 허용
        AddChild(_graphEdit);

        // 2. 연결 및 해제 시그널 구독
        _graphEdit.ConnectionRequest += OnConnectionRequest;
        _graphEdit.DisconnectionRequest += OnDisconnectionRequest;
        _graphEdit.GuiInput += OnGraphEditGuiInput;

        // 3. 우클릭 컨텍스트 메뉴 초기화
        _contextMenu = new PopupMenu();
        _contextMenu.AddItem("Add Selector", 0);
        _contextMenu.AddItem("Add Sequence", 1);
        _contextMenu.AddItem("Add RatingSelector", 2);
        _contextMenu.AddItem("Add FindOpponent (Decorator)", 3);
        _contextMenu.AddItem("Add MultipleMove (Action)", 4);
        _contextMenu.AddItem("Add TurnAction (Action)", 5);
        _contextMenu.IdPressed += OnContextMenuIdPressed;
        AddChild(_contextMenu);

        // 여백 및 스타일 조정
        ThemeOverrideConstantsMargins();
    }

    private void ThemeOverrideConstantsMargins()
    {
        AddThemeConstantOverride("margin_top", 10);
        AddThemeConstantOverride("margin_bottom", 10);
        AddThemeConstantOverride("margin_left", 10);
        AddThemeConstantOverride("margin_right", 10);
    }

    /// <summary>
    /// 시각화할 BehaviorTree를 주입받아 그래프를 갱신합니다.
    /// </summary>
    public void SetTree(BehaviorTree tree)
    {
        if (GodotObject.IsInstanceValid(_tree))
        {
            _tree.OnUpdateTree -= OnTreeUpdate;
        }

        _tree = tree;

        if (GodotObject.IsInstanceValid(_tree))
        {
            _tree.OnUpdateTree += OnTreeUpdate;
            RebuildGraph();
        }
        else
        {
            ClearGraph();
        }
    }

    private void OnTreeUpdate(BehaviorTree tree)
    {
        RebuildGraph();
    }

    protected override void Dispose(bool disposing)
    {
        ClearRenamedSubscriptions();
        if (GodotObject.IsInstanceValid(_tree))
        {
            _tree.OnUpdateTree -= OnTreeUpdate;
        }
        base.Dispose(disposing);
    }

    private void ClearRenamedSubscriptions()
    {
        foreach (var node in _subscribedNodes)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.Renamed -= OnNodeRenamed;
            }
        }
        _subscribedNodes.Clear();
    }

    private void OnNodeRenamed()
    {
        if (GodotObject.IsInstanceValid(_tree))
        {
            _tree.UpdateRequest();
        }
    }

    /// <summary>
    /// 그래프의 모든 노드와 연결선을 정리합니다.
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
    }

    /// <summary>
    /// BehaviorTree 구조를 반영하여 GraphEdit를 재구성합니다.
    /// </summary>
    public void RebuildGraph()
    {
        ClearRenamedSubscriptions();
        ClearGraph();
        if (_tree == null || _graphEdit == null) return;

        // 1. 유효성 검사 수행
        var validationResults = BehaviorTreeValidation.ValidateTree(_tree);

        // 2. 에디터 씬에 존재하는 모든 노드 수집
        var allNodes = new List<Node>();
        GatherAllNodes(_tree, allNodes);

        // 3. Auto-layout 연산을 위한 노드 깊이(depth) 및 순서(index) 계산
        var nodeDepths = new Dictionary<Node, int>();
        var depthLists = new Dictionary<int, List<Node>>();
        
        CalculateNodeLayouts(_tree, nodeDepths, depthLists);

        // 4. GraphNode 생성 및 배치
        var nodeToGraphNodeName = new Dictionary<Node, string>();

        foreach (var node in allNodes)
        {
            if (node == _tree) continue; // BehaviorTree 자체는 그리개에서 제외

            var graphNode = new GraphNode();
            string uniqueName = $"BTNode_{node.GetInstanceId()}";
            graphNode.Name = uniqueName;
            nodeToGraphNodeName[node] = uniqueName;

            // 노드 헤더 타이틀 설정
            graphNode.Title = node.Name;
            
            // GraphNode 크기 조정
            graphNode.CustomMinimumSize = new Vector2(240, 110);

            // 타입 정보 출력을 위한 내부 라벨 추가
            var typeLabel = new Label();
            typeLabel.Text = $"Type: {node.GetType().Name}";
            typeLabel.HorizontalAlignment = HorizontalAlignment.Center;
            graphNode.AddChild(typeLabel);

            // Sibling Order 조정을 위한 UI 버튼 및 삭제 버튼 통합 추가
            int siblingIndex = GetSiblingOrder(node);
            var parent = node.GetParent();

            var orderHBox = new HBoxContainer();
            orderHBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            orderHBox.Alignment = BoxContainer.AlignmentMode.Center;

            // Up/Down 버튼은 부모 노드가 있을 때만 활성화 (없으면 비활성화)
            bool hasValidParent = parent != null && (parent is BehaviorTree_Node || parent is BehaviorTree);

            var btnUp = new Button();
            btnUp.Text = "▲";
            btnUp.TooltipText = "실행 순서 위로 이동";
            btnUp.Disabled = !hasValidParent;
            btnUp.Pressed += () => MoveSiblingOrder(node, -1);
            orderHBox.AddChild(btnUp);

            var orderLabel = new Label();
            orderLabel.Text = $"Order: {siblingIndex}";
            orderLabel.HorizontalAlignment = HorizontalAlignment.Center;
            orderLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            orderHBox.AddChild(orderLabel);

            var btnDown = new Button();
            btnDown.Text = "▼";
            btnDown.TooltipText = "실행 순서 아래로 이동";
            btnDown.Disabled = !hasValidParent;
            btnDown.Pressed += () => MoveSiblingOrder(node, 1);
            orderHBox.AddChild(btnDown);

            // ✕ 노드 삭제 버튼 추가 (항상 활성)
            var btnDelete = new Button();
            btnDelete.Text = "✕";
            btnDelete.TooltipText = "노드 삭제 (자식 노드는 자동 보존)";
            btnDelete.AddThemeColorOverride("font_color", Colors.Tomato);
            btnDelete.Pressed += () => OnNodeDeleteRequest(node);
            orderHBox.AddChild(btnDelete);

            graphNode.AddChild(orderHBox);

            // 포트(Slot) 설정 - 좌측(Input), 우측(Output)
            Color portColor = GetPortColor(node);
            
            // 0번 슬롯 포트 활성화: Input(Left), Output(Right) 둘 다 켬
            graphNode.SetSlot(0, 
                node != _tree.GetChild(0), 
                0, 
                portColor, 
                true, 
                0, 
                portColor);

            // 5. 유효성 검사 결과 UI 반영
            if (validationResults.TryGetValue(node, out var valResult))
            {
                var valLabel = new Label();
                valLabel.Text = $"⚠️ {valResult.Message}";
                valLabel.AutowrapMode = TextServer.AutowrapMode.Word;
                valLabel.HorizontalAlignment = HorizontalAlignment.Center;
                
                if (valResult.IsError)
                {
                    valLabel.AddThemeColorOverride("font_color", Colors.Red);
                    graphNode.SelfModulate = Colors.Tomato; // 에러 노드는 붉은 계열 색상
                }
                else if (valResult.IsWarning)
                {
                    valLabel.AddThemeColorOverride("font_color", Colors.Yellow);
                    graphNode.SelfModulate = Colors.Gold;   // 경고 노드는 노란 계열 색상
                }
                
                graphNode.AddChild(valLabel);
            }
            else
            {
                // 정상 노드의 경우 타입별로 살짝 모듈레이션
                if (node is BehaviorTree_Composite)
                {
                    graphNode.SelfModulate = new Color(0.8f, 0.95f, 1f); // Composite: 하늘색 계열
                }
                else if (node is BehaviorTree_Decorator)
                {
                    graphNode.SelfModulate = new Color(1f, 0.95f, 0.8f); // Decorator: 황색 계열
                }
                else if (node is BehaviorTree_Action)
                {
                    graphNode.SelfModulate = new Color(0.8f, 1f, 0.8f); // Action: 녹색 계열
                }
            }

            _graphEdit.AddChild(graphNode);

            // 노드 클릭/선택 시 메인 인스펙터 연동
            graphNode.NodeSelected += () =>
            {
                if (GodotObject.IsInstanceValid(node))
                {
                    EditorInterface.Singleton.EditNode(node);
                }
            };

            // 노드 이름 변경 감지 리스너 추가
            if (!_subscribedNodes.Contains(node))
            {
                node.Renamed += OnNodeRenamed;
                _subscribedNodes.Add(node);
            }

            // 드래그 위치 이동 시 메타데이터 및 씬 더티 즉시 반영
            graphNode.PositionOffsetChanged += () =>
            {
                if (GodotObject.IsInstanceValid(node))
                {
                    node.SetMeta("bt_graph_position", graphNode.PositionOffset);
                    EditorInterface.Singleton.MarkSceneAsUnsaved();
                }
            };

            // 6. 위치 배치 (저장된 메타데이터 활용 또는 BFS Auto-layout 오프셋 설정)
            if (node.HasMeta("bt_graph_position"))
            {
                graphNode.PositionOffset = (Vector2)node.GetMeta("bt_graph_position");
            }
            else if (nodeDepths.TryGetValue(node, out int depth))
            {
                var siblingsAtDepth = depthLists[depth];
                int xIndex = siblingsAtDepth.IndexOf(node);
                int totalCount = siblingsAtDepth.Count;

                float xPos = (xIndex - (totalCount - 1) / 2.0f) * 260.0f + 400.0f;
                float yPos = depth * 180.0f + 50.0f;

                graphNode.PositionOffset = new Vector2(xPos, yPos);
            }
        }

        // BehaviorTree에 자식이 없는 경우 경고 박스 표시
        if (_tree.GetChildCount() == 0 && validationResults.TryGetValue(_tree, out var treeError))
        {
            var warningNode = new GraphNode();
            warningNode.Title = "Warning";
            warningNode.PositionOffset = new Vector2(300, 100);
            warningNode.CustomMinimumSize = new Vector2(250, 80);
            warningNode.SelfModulate = Colors.Gold;

            var errorLabel = new Label();
            errorLabel.Text = treeError.Message;
            errorLabel.AutowrapMode = TextServer.AutowrapMode.Word;
            warningNode.AddChild(errorLabel);

            _graphEdit.AddChild(warningNode);
        }

        // 7. 연결선(Connection) 생성
        foreach (var node in allNodes)
        {
            if (node == _tree) continue;

            foreach (var child in node.GetChildren())
            {
                if (child is BehaviorTree_Node childBtNode)
                {
                    if (nodeToGraphNodeName.TryGetValue(node, out string fromName) &&
                        nodeToGraphNodeName.TryGetValue(childBtNode, out string toName))
                    {
                        _graphEdit.ConnectNode(fromName, 0, toName, 0);
                    }
                }
                else if (child is Node childNonBtNode) // 0번 자식이 non-BT인 경우 등 예외 표시용 연결선 생성
                {
                    if (nodeToGraphNodeName.TryGetValue(node, out string fromName) &&
                        nodeToGraphNodeName.TryGetValue(childNonBtNode, out string toName))
                    {
                        _graphEdit.ConnectNode(fromName, 0, toName, 0);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 노드 트리 구조에 맞춰 깊이(depth)별 노드 리스트를 작성합니다.
    /// </summary>
    private void CalculateNodeLayouts(Node current, Dictionary<Node, int> depths, Dictionary<int, List<Node>> depthLists)
    {
        if (current == null) return;

        // BFS로 레이아웃 깊이 계산
        var queue = new Queue<(Node Node, int Depth)>();
        
        // BehaviorTree의 모든 자식을 depth 0으로 삽입
        if (current is BehaviorTree)
        {
            foreach (var child in current.GetChildren())
            {
                queue.Enqueue((child, 0));
            }
        }
        else
        {
            queue.Enqueue((current, 0));
        }

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            var node = item.Node;
            int depth = item.Depth;

            if (depths.ContainsKey(node)) continue;

            depths[node] = depth;
            if (!depthLists.ContainsKey(depth))
            {
                depthLists[depth] = new List<Node>();
            }
            if (!depthLists[depth].Contains(node))
            {
                depthLists[depth].Add(node);
            }

            foreach (var child in node.GetChildren())
            {
                if (child is BehaviorTree_Node || node is BehaviorTree_Node)
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
        }
    }

    /// <summary>
    /// Sibling order 계산 (부모 노드 하위에서 해당 노드가 몇 번째 BehaviorTree_Node 자식인지 반환)
    /// </summary>
    private int GetSiblingOrder(Node node)
    {
        var parent = node.GetParent();
        if (parent == null) return 0;

        int order = 0;
        foreach (var child in parent.GetChildren())
        {
            if (child == node)
            {
                return order;
            }
            if (child is BehaviorTree_Node)
            {
                order++;
            }
        }
        return order;
    }

    /// <summary>
    /// 노드 타입에 매칭되는 시각적 포트 색상을 반환합니다.
    /// </summary>
    private Color GetPortColor(Node node)
    {
        if (node is BehaviorTree_Composite)
        {
            return Colors.Cyan;
        }
        if (node is BehaviorTree_Decorator)
        {
            return Colors.Gold;
        }
        if (node is BehaviorTree_Action)
        {
            return Colors.LightGreen;
        }
        return Colors.Red; // Invalid Node Type
    }

    private void GatherAllNodes(Node current, List<Node> list)
    {
        if (current == null) return;
        list.Add(current);
        foreach (var child in current.GetChildren())
        {
            GatherAllNodes(child, list);
        }
    }

    // --- GraphEdit 조작 시그널 핸들러 및 헬퍼 ---

    private void OnGraphEditGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Right && mouseBtn.Pressed)
            {
                Vector2 mouseLocalPos = _graphEdit.GetLocalMousePosition();
                var connection = _graphEdit.GetClosestConnectionAtPoint(mouseLocalPos, 8.0f);
                if (connection.Count > 0)
                {
                    StringName fromNode = connection["from_node"].AsStringName();
                    long fromPort = connection["from_port"].AsInt64();
                    StringName toNode = connection["to_node"].AsStringName();
                    long toPort = connection["to_port"].AsInt64();

                    OnDisconnectionRequest(fromNode, fromPort, toNode, toPort);
                    AcceptEvent();
                }
                else
                {
                    _clickPosition = mouseBtn.Position;
                    _contextMenu.Position = DisplayServer.Singleton.MouseGetPosition();
                    _contextMenu.Popup();
                    AcceptEvent();
                }
            }
        }
    }

    private void OnContextMenuIdPressed(long id)
    {
        if (_tree == null) return;

        BehaviorTree_Node newNode = null;
        string defaultName = "";

        switch (id)
        {
            case 0:
                newNode = new BehaviorTree_Selector();
                defaultName = "BehaviorTree_Selector";
                break;
            case 1:
                newNode = new BehaviorTree_Sequence();
                defaultName = "BehaviorTree_Sequence";
                break;
            case 2:
                newNode = new node.Rating.BehaviorTree_RatingSelector();
                defaultName = "BehaviorTree_RatingSelector";
                break;
            case 3:
                newNode = new AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Decorator.BehaviorTree_FindOpponent();
                defaultName = "BehaviorTree_FindOpponent";
                break;
            case 4:
                newNode = new AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action.BehaviorTree_MultipleMove();
                defaultName = "BehaviorTree_MultipleMove";
                break;
            case 5:
                newNode = new AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action.BehaviorTree_TurnAction();
                defaultName = "BehaviorTree_TurnAction";
                break;
        }

        if (newNode != null)
        {
            newNode.Name = GetUniqueNodeName(defaultName);
            
            // 초기 위치 저장 (줌과 스크롤 오프셋을 적용하여 그래프 공간 좌표로 변환)
            Vector2 graphPosition = (_clickPosition + _graphEdit.ScrollOffset) / _graphEdit.Zoom;
            newNode.SetMeta("bt_graph_position", graphPosition);
            _tree.AddChild(newNode);

            // 씬 Owner 지정 (씬 저장 보존용 - 에디터 활성 탭이 아닌 BehaviorTree의 실제 씬 루트인 Owner 기준)
            Node sceneOwner = _tree.Owner;
            if (sceneOwner != null)
            {
                SetOwnerRecursive(newNode, sceneOwner);
            }
            else
            {
                GD.PushWarning($"[BT Editor] Warning: _tree.Owner is null. Node {newNode.Name} owner cannot be set.");
            }

            EditorInterface.Singleton.MarkSceneAsUnsaved();
            _tree.UpdateRequest();
        }
    }

    private string GetUniqueNodeName(string baseName)
    {
        string name = baseName;
        int index = 1;
        while (_tree.HasNode(name))
        {
            name = $"{baseName}_{index}";
            index++;
        }
        return name;
    }

    internal bool CanConnect(Node fromNode, Node toNode, out string reason)
    {
        reason = string.Empty;

        if (fromNode == null || toNode == null)
        {
            reason = "노드가 null입니다.";
            return false;
        }

        if (!(fromNode is BehaviorTree_Node fromBtNode) || !(toNode is BehaviorTree_Node toBtNode))
        {
            reason = "노드가 BehaviorTree_Node가 아닙니다.";
            return false;
        }

        // 제한 조건 1: Decorator 자식 1개 제약
        if (fromBtNode is BehaviorTree_Decorator decorator)
        {
            int childCount = 0;
            foreach (var child in decorator.GetChildren())
            {
                if (child is BehaviorTree_Node) childCount++;
            }
            if (childCount >= 1)
            {
                reason = "Decorator 노드는 자식을 1개만 연결할 수 있습니다.";
                return false;
            }
        }

        // 제한 조건 2: Action 자식 연결 불가
        if (fromBtNode is BehaviorTree_Action)
        {
            reason = "Action 노드는 자식을 가질 수 없습니다.";
            return false;
        }

        // 제한 조건 3: 다중 부모 방지 (toNode가 이미 다른 BT 노드를 부모로 두고 있는 경우)
        var currentParent = toBtNode.GetParent();
        if (currentParent is BehaviorTree_Node && currentParent != fromBtNode)
        {
            reason = "자식 노드는 이미 다른 부모 노드에 연결되어 있습니다.";
            return false;
        }

        // 제한 조건 4: Cycle (순환 참조) 방지
        if (IsAncestorOf(toBtNode, fromBtNode))
        {
            reason = "순환 연결(Cycle)이 발생합니다.";
            return false;
        }

        return true;
    }

    private void OnConnectionRequest(StringName fromNodeName, long fromPort, StringName toNodeName, long toPort)
    {
        if (_tree == null) return;

        Node fromNode = FindNodeByGraphName(fromNodeName);
        Node toNode = FindNodeByGraphName(toNodeName);

        if (!CanConnect(fromNode, toNode, out string reason))
        {
            GD.PrintErr($"[BT Editor] 연결 거부: {reason}");
            return;
        }

        BehaviorTree_Node fromBtNode = (BehaviorTree_Node)fromNode;
        BehaviorTree_Node toBtNode = (BehaviorTree_Node)toNode;

        // 연결 반영: 기존 부모에게서 분리 후 새 부모로 이동
        Vector2 cachedOffset = Vector2.Zero;
        foreach (var child in _graphEdit.GetChildren())
        {
            if (child is GraphNode gn && gn.Name == toNodeName)
            {
                cachedOffset = gn.PositionOffset;
                break;
            }
        }

        if (toBtNode.GetParent() != null)
        {
            toBtNode.GetParent().RemoveChild(toBtNode);
        }
        fromBtNode.AddChild(toBtNode);

        // 이전 그래프 위치를 메타데이터로 영구 반영
        if (cachedOffset != Vector2.Zero)
        {
            toBtNode.SetMeta("bt_graph_position", cachedOffset);
        }

        Node sceneOwner = _tree.Owner;
        if (sceneOwner != null)
        {
            SetOwnerRecursive(toBtNode, sceneOwner);
        }
        else
        {
            GD.PushWarning($"[BT Editor] Warning: _tree.Owner is null. Node {toBtNode.Name} owner cannot be set.");
        }

        EditorInterface.Singleton.MarkSceneAsUnsaved();
        _tree.UpdateRequest();
    }

    private void OnDisconnectionRequest(StringName fromNodeName, long fromPort, StringName toNodeName, long toPort)
    {
        if (_tree == null) return;

        Node fromNode = FindNodeByGraphName(fromNodeName);
        Node toNode = FindNodeByGraphName(toNodeName);

        if (fromNode == null || toNode == null) return;

        if (toNode.GetParent() == fromNode)
        {
            Vector2 cachedOffset = Vector2.Zero;
            foreach (var child in _graphEdit.GetChildren())
            {
                if (child is GraphNode gn && gn.Name == toNodeName)
                {
                    cachedOffset = gn.PositionOffset;
                    break;
                }
            }

            fromNode.RemoveChild(toNode);
            _tree.AddChild(toNode);

            if (cachedOffset != Vector2.Zero)
            {
                toNode.SetMeta("bt_graph_position", cachedOffset);
            }

            Node sceneOwner = _tree.Owner;
            if (sceneOwner != null)
            {
                SetOwnerRecursive(toNode, sceneOwner);
            }
            else
            {
                GD.PushWarning($"[BT Editor] Warning: _tree.Owner is null. Node {toNode.Name} owner cannot be set.");
            }

            EditorInterface.Singleton.MarkSceneAsUnsaved();
            _tree.UpdateRequest();
        }
    }

    internal void SalvageChildren(Node node)
    {
        if (node == null || _tree == null) return;

        // 자식 노드 구출 (Salvage) - 부모 노드 삭제 시 서브트리 보호
        var childrenToSalvage = new List<Node>();
        foreach (var child in node.GetChildren())
        {
            childrenToSalvage.Add(child);
        }

        foreach (var child in childrenToSalvage)
        {
            node.RemoveChild(child);
            _tree.AddChild(child);

            Node sceneOwner = _tree.Owner;
            if (sceneOwner != null)
            {
                SetOwnerRecursive(child, sceneOwner);
            }
            else
            {
                GD.PushWarning($"[BT Editor] Warning: _tree.Owner is null. Node {child.Name} owner cannot be set.");
            }
        }
    }

    private void OnNodeDeleteRequest(Node node)
    {
        if (_tree == null || node == null) return;

        SalvageChildren(node);

        if (node.GetParent() != null)
        {
            node.GetParent().RemoveChild(node);
        }
        node.QueueFree();

        EditorInterface.Singleton.MarkSceneAsUnsaved();
        _tree.UpdateRequest();
    }

    private void MoveSiblingOrder(Node node, int offset)
    {
        var parent = node.GetParent();
        if (parent == null) return;

        int currentRawIndex = node.GetIndex();
        int newRawIndex = currentRawIndex + offset;

        if (newRawIndex >= 0 && newRawIndex < parent.GetChildCount())
        {
            parent.MoveChild(node, newRawIndex);

            Node sceneOwner = _tree.Owner;
            if (sceneOwner != null)
            {
                SetOwnerRecursive(node, sceneOwner);
            }
            else
            {
                GD.PushWarning($"[BT Editor] Warning: _tree.Owner is null. Node {node.Name} owner cannot be set.");
            }

            EditorInterface.Singleton.MarkSceneAsUnsaved();
            _tree.UpdateRequest();
        }
    }

    internal bool IsAncestorOf(Node potentialAncestor, Node node)
    {
        var current = node.GetParent();
        while (current != null && current != _tree)
        {
            if (current == potentialAncestor) return true;
            current = current.GetParent();
        }
        return false;
    }

    private Node FindNodeByGraphName(string graphName)
    {
        if (_tree == null || string.IsNullOrEmpty(graphName)) return null;
        if (!graphName.StartsWith("BTNode_")) return null;

        string idStr = graphName.Substring(7);
        if (ulong.TryParse(idStr, out ulong instanceId))
        {
            return GodotObject.InstanceFromId(instanceId) as Node;
        }
        return null;
    }

    private void SetOwnerRecursive(Node node, Node owner)
    {
        node.Owner = owner;
        foreach (var child in node.GetChildren())
        {
            SetOwnerRecursive(child, owner);
        }
    }

    public void HandleDebugTick(Godot.Collections.Dictionary payload)
    {
        if (_tree == null || _graphEdit == null) return;

        if (payload.ContainsKey("nodes") && payload["nodes"].Obj is Godot.Collections.Array nodesList)
        {
            ResetHighlights();

            foreach (var nodeReportObj in nodesList)
            {
                if (nodeReportObj.Obj is Godot.Collections.Dictionary report)
                {
                    string nodePath = report.ContainsKey("node_path") ? report["node_path"].AsString() : "";
                    BtStatus status = report.ContainsKey("status") ? (BtStatus)report["status"].AsInt64() : BtStatus.Failure;
                    long elapsedTime = report.ContainsKey("elapsed_time") ? report["elapsed_time"].AsInt64() : 0;

                    Node realNode = _tree.GetNodeOrNull(nodePath);
                    if (realNode != null)
                    {
                        string graphNodeName = $"BTNode_{realNode.GetInstanceId()}";
                        var graphNode = _graphEdit.GetNodeOrNull<GraphNode>(graphNodeName);
                        if (graphNode != null)
                        {
                            HighlightGraphNode(graphNode, status, elapsedTime);
                        }
                    }
                }
            }
        }
    }

    private void ResetHighlights()
    {
        foreach (var child in _graphEdit.GetChildren())
        {
            if (child is GraphNode graphNode)
            {
                Node node = FindNodeByGraphName(graphNode.Name);
                if (node != null)
                {
                    RestoreDefaultColor(graphNode, node);
                }
            }
        }
    }

    private void RestoreDefaultColor(GraphNode graphNode, Node node)
    {
        var validationResults = BehaviorTreeValidation.ValidateTree(_tree);
        if (validationResults.TryGetValue(node, out var valResult))
        {
            if (valResult.IsError)
            {
                graphNode.SelfModulate = Colors.Tomato;
            }
            else if (valResult.IsWarning)
            {
                graphNode.SelfModulate = Colors.Gold;
            }
        }
        else
        {
            if (node is BehaviorTree_Composite)
            {
                graphNode.SelfModulate = new Color(0.8f, 0.95f, 1f);
            }
            else if (node is BehaviorTree_Decorator)
            {
                graphNode.SelfModulate = new Color(1f, 0.95f, 0.8f);
            }
            else if (node is BehaviorTree_Action)
            {
                graphNode.SelfModulate = new Color(0.8f, 1f, 0.8f);
            }
            else
            {
                graphNode.SelfModulate = Colors.White;
            }
        }
    }

    private void HighlightGraphNode(GraphNode graphNode, BtStatus status, long elapsedTime)
    {
        switch (status)
        {
            case BtStatus.Success:
                graphNode.SelfModulate = new Color(0.2f, 0.8f, 0.2f); // Green
                break;
            case BtStatus.Failure:
                graphNode.SelfModulate = new Color(0.9f, 0.3f, 0.3f); // Red
                break;
            case BtStatus.Running:
                graphNode.SelfModulate = new Color(0.3f, 0.6f, 1.0f); // Blue
                break;
        }
    }
}
#endif
