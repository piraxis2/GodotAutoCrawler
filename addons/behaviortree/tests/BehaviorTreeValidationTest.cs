#if TOOLS
using System;
using System.Collections.Generic;
using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.addons.behaviortree.node.Rating;
using AutoCrawler.addons.behaviortree.UI;

namespace AutoCrawler.addons.behaviortree.tests;

/// <summary>
/// BehaviorTreeValidation 로직을 검증하기 위한 C# 테스트 스크립트
/// </summary>
public partial class BehaviorTreeValidationTest : Node
{
    private int _failures = 0;

    public override void _Ready()
    {
        if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
        {
            int failures = RunTests();
            if (failures == 0)
            {
                GD.Print("[BT-001 Step5] ALL PASS");
                GetTree().Quit(0);
            }
            else
            {
                GD.Print($"[BT-001 Step5] FAILED: {failures} assertion(s)");
                GetTree().Quit(1);
            }
        }
    }

    public int RunTests()
    {
        GD.Print("[C# BT Validation Test] Running Tests...");
        try
        {
            // Step 1 Validation Tests
            TestValidTree();
            TestNoRoot();
            TestInvalidRootType();
            TestDecoratorExtraChild();
            TestActionHasChild();
            TestRatingSelectorInvalidChild();

            // Step 2 Authoring Logic Tests
            TestIsAncestorOfCycle();
            TestConnectionRules();
            TestSalvageChildren();

            // Step 3 Remote Debugging Helper Tests
            TestDebugTickReporting();

            // Step 4a Remote Debug Channel Tests
            TestRegistryRegisterUnregister();
            TestGateShortCircuit();
            TestRegistryRoutingAndGateToggle();
            TestPayloadRoundtrip();

            // Step 4b Remote Debug Graph Visualization Tests
            TestDebugGraphBuildFromStructure();
            TestDebugGraphTickHighlight();
            TestDebugGraphTickWithoutStructure();
            TestEditorForwardsAllRemoteMessages();

            // Step 5 Battle Debug Integration Tests
            TestRemoteMultiTreeRoutingIsolation();
            TestRemoteTabCloseStopsOnlyTarget();
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"[C# BT Validation Test] Uncaught Exception: {ex.Message}\n{ex.StackTrace}");
        }

        return _failures;
    }

    private void Check(string name, bool actual, bool expected)
    {
        if (actual == expected)
        {
            GD.Print($"  PASS: {name}");
        }
        else
        {
            _failures++;
            GD.Print($"  FAIL: {name} -> got {actual}, expected {expected}");
        }
    }

    private void TestValidTree()
    {
        GD.Print("[A] 정상 트리 검증");
        var tree = new BehaviorTree();
        
        var selector = new BehaviorTree_Selector();
        selector.Name = "RootSelector";
        tree.AddChild(selector);

        var sequence = new BehaviorTree_Sequence();
        sequence.Name = "ChildSequence";
        selector.AddChild(sequence);

        var results = BehaviorTreeValidation.ValidateTree(tree);
        Check("A.valid_count", results.Count == 0, true);

        tree.QueueFree();
    }

    private void TestNoRoot()
    {
        GD.Print("[B] 루트 노드 없음 검증");
        var tree = new BehaviorTree();

        var results = BehaviorTreeValidation.ValidateTree(tree);
        Check("B.error_count", results.Count == 1, true);
        Check("B.error_type", results.ContainsKey(tree) && results[tree].ErrorType == BtValidationErrorType.NoRoot, true);

        tree.QueueFree();
    }

    private void TestInvalidRootType()
    {
        GD.Print("[C] 잘못된 루트 타입 검증");
        var tree = new BehaviorTree();
        
        var timer = new Timer();
        timer.Name = "TimerRoot";
        tree.AddChild(timer);

        var results = BehaviorTreeValidation.ValidateTree(tree);
        Check("C.error_count", results.Count == 1, true);
        Check("C.error_type", results.ContainsKey(timer) && results[timer].ErrorType == BtValidationErrorType.InvalidRootType, true);

        tree.QueueFree();
    }

    private void TestDecoratorExtraChild()
    {
        GD.Print("[D] Decorator 자식 초과 검증");
        var tree = new BehaviorTree();

        // 0번 child 가 null 이면 SetTree 에서 return 되지만, 구조 검사 자체는 작동합니다.
        var decorator = new TestDecoratorNode();
        decorator.Name = "DecoratorRoot";
        tree.AddChild(decorator);

        // Decorator 아래에 자식을 2개 추가
        var child1 = new TestActionNode();
        child1.Name = "Action1";
        decorator.AddChild(child1);

        var child2 = new TestActionNode();
        child2.Name = "Action2";
        decorator.AddChild(child2);

        var results = BehaviorTreeValidation.ValidateTree(tree);
        Check("D.error_count", results.Count == 1, true);
        Check("D.error_type", results.ContainsKey(decorator) && results[decorator].ErrorType == BtValidationErrorType.DecoratorExtraChild, true);

        tree.QueueFree();
    }

    private void TestActionHasChild()
    {
        GD.Print("[E] Action 자식 존재 검증");
        var tree = new BehaviorTree();

        var action = new TestActionNode();
        action.Name = "ActionRoot";
        tree.AddChild(action);

        // Action 아래에 자식을 추가 (잘못된 구조)
        var child = new TestActionNode();
        child.Name = "SubAction";
        action.AddChild(child);

        var results = BehaviorTreeValidation.ValidateTree(tree);
        Check("E.error_count", results.Count == 1, true);
        Check("E.error_type", results.ContainsKey(action) && results[action].ErrorType == BtValidationErrorType.ActionHasChild, true);

        tree.QueueFree();
    }

    private void TestRatingSelectorInvalidChild()
    {
        GD.Print("[F] RatingSelector 잘못된 자식 경고 검증");
        var tree = new BehaviorTree();

        var ratingSelector = new BehaviorTree_RatingSelector();
        ratingSelector.Name = "RatingSelectorRoot";
        tree.AddChild(ratingSelector);

        // RatingSelector 아래에 일반 Action 자식 추가 (경고 대상)
        var action = new TestActionNode();
        action.Name = "RegularAction";
        ratingSelector.AddChild(action);

        var results = BehaviorTreeValidation.ValidateTree(tree);
        Check("F.warning_count", results.Count == 1, true);
        Check("F.warning_type", results.ContainsKey(ratingSelector) && results[ratingSelector].ErrorType == BtValidationErrorType.RatingSelectorInvalidChild, true);
        Check("F.is_warning_only", results[ratingSelector].IsWarning && !results[ratingSelector].IsError, true);

        tree.QueueFree();
    }

    private void TestIsAncestorOfCycle()
    {
        GD.Print("[G] IsAncestorOf 순환 감지 검증");
        var graphView = new BehaviorTreeGraphView();
        
        var root = new BehaviorTree_Selector();
        var child = new BehaviorTree_Sequence();
        var grandchild = new TestActionNode();

        root.AddChild(child);
        child.AddChild(grandchild);

        // 정상적인 상속 관계
        Check("G.normal_parent_child", graphView.IsAncestorOf(root, grandchild), true);
        Check("G.normal_reversed", graphView.IsAncestorOf(grandchild, root), false);

        // 동일 노드
        Check("G.same_node", graphView.IsAncestorOf(root, root), false);

        graphView.QueueFree();
        root.QueueFree();
    }

    private void TestConnectionRules()
    {
        GD.Print("[H] 연결 규칙(사전 차단) 검증");
        var graphView = new BehaviorTreeGraphView();
        
        var tree = new BehaviorTree();
        graphView.SetTree(tree);

        var selector = new BehaviorTree_Selector();
        var decorator = new TestDecoratorNode();
        var action = new TestActionNode();
        var leaf = new TestActionNode();

        // 1. Selector에 Action 연결 (정상)
        Check("H.normal_connection", graphView.CanConnect(selector, action, out _), true);

        // 2. Action에 자식 연결 시도 (거부)
        string reason;
        Check("H.action_has_child_denied", graphView.CanConnect(action, leaf, out reason), false);
        Check("H.action_has_child_reason", reason.Contains("Action 노드"), true);

        // 3. Decorator에 이미 1개 연결된 상태에서 추가 연결 시도 (거부)
        decorator.AddChild(action);
        Check("H.decorator_extra_child_denied", graphView.CanConnect(decorator, leaf, out reason), false);
        Check("H.decorator_extra_child_reason", reason.Contains("Decorator 노드"), true);
        decorator.RemoveChild(action);

        // 4. 다중 부모 연결 시도 (이미 부모가 있는 노드를 다른 노드에 연결)
        selector.AddChild(action);
        Check("H.multiple_parent_denied", graphView.CanConnect(decorator, action, out reason), false);
        Check("H.multiple_parent_reason", reason.Contains("다른 부모 노드"), true);
        selector.RemoveChild(action);

        // 5. 순환 연결 시도 (자식을 조상에게 부모로 연결하려 함)
        selector.AddChild(decorator);
        Check("H.cycle_denied", graphView.CanConnect(decorator, selector, out reason), false);
        Check("H.cycle_reason", reason.Contains("순환 연결"), true);
        
        // Clean up: Add children to tree to let QueueFree free them recursively, and manually free the unparented leaf.
        tree.AddChild(selector);
        leaf.QueueFree();

        graphView.QueueFree();
        tree.QueueFree();
    }

    private void TestSalvageChildren()
    {
        GD.Print("[I] 자식 Salvage(구출) 검증");
        var graphView = new BehaviorTreeGraphView();
        
        var tree = new BehaviorTree();
        graphView.SetTree(tree);

        var selector = new BehaviorTree_Selector();
        var action1 = new TestActionNode();
        var action2 = new TestActionNode();

        tree.AddChild(selector);
        selector.AddChild(action1);
        selector.AddChild(action2);

        // selector를 지우기 전에 salvage 실행
        graphView.SalvageChildren(selector);

        // 자식들이 tree 직속으로 올라왔는지 검증
        Check("I.action1_salvaged", action1.GetParent() == tree, true);
        Check("I.action2_salvaged", action2.GetParent() == tree, true);
        Check("I.selector_has_no_children", selector.GetChildCount() == 0, true);

        graphView.QueueFree();
        tree.QueueFree();
    }

    private void TestDebugTickReporting()
    {
        GD.Print("[J] 디버그 틱 리포팅 헬퍼 검증");
        var tree = new BehaviorTree();
        
        var action = new TestActionNode();
        action.Name = "DebugAction";
        tree.AddChild(action);
        
        // 트리 바인딩 호출
        tree._Ready();
        
        // Behave() 호출 시 디버그 리포팅 동작 예외 발생 여부 확인
        try
        {
            var status = tree.Behave(0.016, null);
            Check("J.behave_status_success", status == BtStatus.Success, true);
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"  FAIL: Behave() exception: {ex.Message}");
        }

        // EngineDebugger 활성화 여부와 무관하게 직접 헬퍼 호출 검증
        try
        {
            tree.StartDebugTick();
            tree.ReportNodeExecution("DebugAction", "DebugAction", "TestActionNode", BtStatus.Success, 100);
            tree.EndDebugTick();
            GD.Print("  PASS: J.direct_helper_calls");
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"  FAIL: Direct helper calls exception: {ex.Message}");
        }

        tree.QueueFree();
    }

    private static System.Collections.Generic.Dictionary<string, BehaviorTree> GetRegistry()
    {
        var field = typeof(BehaviorTree).GetField("_registry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (System.Collections.Generic.Dictionary<string, BehaviorTree>)field.GetValue(null);
    }

    private static bool InvokeOnMessageCapture(string message, Godot.Collections.Array data)
    {
        var method = typeof(BehaviorTree).GetMethod("OnMessageCapture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (bool)method.Invoke(null, new object[] { message, data });
    }

    private void TestRegistryRegisterUnregister()
    {
        GD.Print("[K] Registry 등록 및 해제 검증");
        var testTree = new BehaviorTree();
        testTree.Name = "TestLifecycleTree";
        AddChild(testTree); // _Ready() 트리거

        var registry = GetRegistry();
        if (EngineDebugger.IsActive())
        {
            Check("K.Registry_Contains_Ready", registry.ContainsKey(testTree.GetPath().ToString()), true);
        }
        else
        {
            Check("K.Registry_Empty_Ready", registry.ContainsKey(testTree.GetPath().ToString()), false);
        }

        RemoveChild(testTree); // _ExitTree() 트리거
        
        if (EngineDebugger.IsActive())
        {
            Check("K.Registry_Removed_ExitTree", registry.ContainsKey(testTree.GetPath().ToString()), false);
        }

        testTree.QueueFree();
    }

    private void TestGateShortCircuit()
    {
        GD.Print("[L] 게이트 OFF 단락 검증");
        var testTree = new BehaviorTree();
        var action = new TestActionNode();
        action.Name = "GateAction";
        testTree.AddChild(action);
        testTree._Ready();

        testTree.DebugEnabled = false;
        
        // Behave() 호출 시 DebugEnabled가 false이므로 _tickReports에 아무것도 쌓이지 않아야 함
        testTree.Behave(0.016, null);

        var tickReportsField = typeof(BehaviorTree).GetField("_tickReports", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var tickReportsValue = tickReportsField.GetValue(testTree);
        
        Check("L.GateOFF_NoTickReports", tickReportsValue == null, true);

        testTree.QueueFree();
    }

    private void TestRegistryRoutingAndGateToggle()
    {
        GD.Print("[M] Registry 라우팅 및 게이트 토글 검증");
        var testTree = new BehaviorTree();
        var action = new TestActionNode();
        testTree.AddChild(action);
        testTree._Ready();

        var registry = GetRegistry();
        string fakePath = "/root/FakeTreePath";
        registry[fakePath] = testTree;

        testTree.DebugEnabled = false;

        // 1) start 라우팅 → DebugEnabled = true
        bool handledStart = InvokeOnMessageCapture("behavior_tree:start", new Godot.Collections.Array { fakePath });
        Check("M.Routing_StartHandled", handledStart, true);
        Check("M.Routing_DebugEnabled_True", testTree.DebugEnabled, true);

        // 2) stop 라우팅 → DebugEnabled = false
        bool handledStop = InvokeOnMessageCapture("behavior_tree:stop", new Godot.Collections.Array { fakePath });
        Check("M.Routing_StopHandled", handledStop, true);
        Check("M.Routing_DebugEnabled_False", testTree.DebugEnabled, false);

        // 2b) Godot runtime capture가 namespace를 제거해 넘기는 경우도 허용
        bool handledShortStart = InvokeOnMessageCapture("start", new Godot.Collections.Array { fakePath });
        Check("M.Routing_ShortStartHandled", handledShortStart, true);
        Check("M.Routing_ShortStart_DebugEnabled_True", testTree.DebugEnabled, true);

        bool handledShortStop = InvokeOnMessageCapture("stop", new Godot.Collections.Array { fakePath });
        Check("M.Routing_ShortStopHandled", handledShortStop, true);
        Check("M.Routing_ShortStop_DebugEnabled_False", testTree.DebugEnabled, false);

        // 3) 없는 tree_path 라우팅 무시
        bool handledUnknown = InvokeOnMessageCapture("behavior_tree:start", new Godot.Collections.Array { "/root/UnknownTreePath" });
        Check("M.Routing_UnknownIgnored", handledUnknown, true); // dispatcher 자체는 true를 리턴함

        // 클린업
        registry.Remove(fakePath);
        testTree.QueueFree();
    }

    private void TestPayloadRoundtrip()
    {
        GD.Print("[N] Structure 및 Tick Payload 검증");
        var testTree = new BehaviorTree();
        var selector = new BehaviorTree_Selector();
        selector.Name = "MySelector";
        testTree.AddChild(selector);

        var action = new TestActionNode();
        action.Name = "MyAction";
        selector.AddChild(action);

        // 1) Structure Payload 생성 검증 (raw GetChildren() 기반 재귀 확인)
        var nodesArray = new Godot.Collections.Array();
        var buildMethod = typeof(BehaviorTree).GetMethod("BuildStructurePayload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        buildMethod.Invoke(testTree, new object[] { selector, nodesArray });

        Check("N.Structure_NodesCount", nodesArray.Count == 2, true);

        var node1 = (Godot.Collections.Dictionary)nodesArray[0];
        Check("N.Structure_Node1_Name", node1["name"].AsString() == "MySelector", true);
        Check("N.Structure_Node1_Type", node1["type"].AsString() == "BehaviorTree_Selector", true);
        Check("N.Structure_Node1_Parent", node1["parent_path"].AsString() == "", true);

        var node2 = (Godot.Collections.Dictionary)nodesArray[1];
        Check("N.Structure_Node2_Name", node2["name"].AsString() == "MyAction", true);
        Check("N.Structure_Node2_Type", node2["type"].AsString() == "TestActionNode", true);
        Check("N.Structure_Node2_Parent", node2["parent_path"].AsString() == "MySelector", true);

        // 2) Tick Payload 생성 검증
        testTree.DebugEnabled = true;
        var tickReportsField = typeof(BehaviorTree).GetField("_tickReports", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        tickReportsField.SetValue(testTree, new Godot.Collections.Array());

        // ReportNodeExecution 호출
        testTree.ReportNodeExecution("MySelector", "MySelector", "BehaviorTree_Selector", BtStatus.Success, 1.25);

        var tickReports = (Godot.Collections.Array)tickReportsField.GetValue(testTree);
        Check("N.Tick_ReportsCount", tickReports.Count == 1, true);

        var report = (Godot.Collections.Dictionary)tickReports[0];
        Check("N.Tick_Report_Path", report["node_path"].AsString() == "MySelector", true);
        Check("N.Tick_Report_Status", report["status"].AsInt32() == (int)BtStatus.Success, true);
        Check("N.Tick_Report_Time", report["elapsed_time"].AsDouble() == 1.25, true);

        testTree.QueueFree();
    }

    private void TestDebugGraphBuildFromStructure()
    {
        GD.Print("[O] 원격 디버그 그래프 Structure 빌드 검증");
        var view = new BehaviorTreeDebugGraphView();
        AddChild(view); // _Ready() 호출 유도
        
        // 가짜 structure payload 작성
        var nodesList = new Godot.Collections.Array();
        
        var rootNode = new Godot.Collections.Dictionary
        {
            { "node_path", "root" },
            { "name", "RootSelector" },
            { "type", "BehaviorTree_Selector" },
            { "parent_path", "" },
            { "graph_position", new Vector2(100, 100) }
        };
        var childNode = new Godot.Collections.Dictionary
        {
            { "node_path", "root/action" },
            { "name", "MyAction" },
            { "type", "TestActionNode" },
            { "parent_path", "root" },
            { "graph_position", new Vector2(100, 250) }
        };
        
        nodesList.Add(rootNode);
        nodesList.Add(childNode);
        
        view.BuildGraph(nodesList);
        
        var nodesMapField = typeof(BehaviorTreeDebugGraphView).GetField("_nodesMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var nodesMap = (System.Collections.Generic.Dictionary<string, GraphNode>)nodesMapField.GetValue(view);
        
        Check("O.GraphNode_Count", nodesMap.Count == 2, true);
        Check("O.Has_Root", nodesMap.ContainsKey("root"), true);
        Check("O.Has_Child", nodesMap.ContainsKey("root/action"), true);
        
        var rootGraphNode = nodesMap["root"];
        var childGraphNode = nodesMap["root/action"];
        
        Check("O.Root_Position", rootGraphNode.PositionOffset == new Vector2(100, 100), true);
        Check("O.Child_Position", childGraphNode.PositionOffset == new Vector2(100, 250), true);
        Check("O.Root_Title", rootGraphNode.Title == "RootSelector", true);
        
        var graphEditField = typeof(BehaviorTreeDebugGraphView).GetField("_graphEdit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var graphEdit = (GraphEdit)graphEditField.GetValue(view);
        
        var connections = graphEdit.GetConnectionList();
        Check("O.Connection_Count", connections.Count == 1, true);
        
        var conn = connections[0];
        Check("O.Connection_From", conn["from_node"].AsString() == rootGraphNode.Name, true);
        Check("O.Connection_To", conn["to_node"].AsString() == childGraphNode.Name, true);
        
        RemoveChild(view);
        view.QueueFree();
    }

    private void TestDebugGraphTickHighlight()
    {
        GD.Print("[P] 원격 디버그 그래프 Tick 하이라이트 및 Stale 복구 검증");
        var view = new BehaviorTreeDebugGraphView();
        AddChild(view);
        
        var nodesList = new Godot.Collections.Array();
        var node1 = new Godot.Collections.Dictionary
        {
            { "node_path", "node1" },
            { "name", "Node1" },
            { "type", "BehaviorTree_Selector" },
            { "parent_path", "" },
            { "graph_position", new Vector2(100, 100) }
        };
        var node2 = new Godot.Collections.Dictionary
        {
            { "node_path", "node2" },
            { "name", "Node2" },
            { "type", "TestActionNode" },
            { "parent_path", "" },
            { "graph_position", new Vector2(300, 100) }
        };
        nodesList.Add(node1);
        nodesList.Add(node2);
        
        view.BuildGraph(nodesList);

        var nodesMapField = typeof(BehaviorTreeDebugGraphView).GetField("_nodesMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var nodesMap = (System.Collections.Generic.Dictionary<string, GraphNode>)nodesMapField.GetValue(view);
        
        var defaultColorsField = typeof(BehaviorTreeDebugGraphView).GetField("_nodeDefaultColors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var defaultColors = (System.Collections.Generic.Dictionary<string, Color>)defaultColorsField.GetValue(view);

        Color defColor2 = defaultColors["node2"];

        // 1) tick payload로 node1은 Success, node2는 Running으로 하이라이트
        var tickNodes = new Godot.Collections.Array();
        tickNodes.Add(new Godot.Collections.Dictionary
        {
            { "node_path", "node1" },
            { "status", (int)BtStatus.Success },
            { "elapsed_time", 2.5 }
        });
        tickNodes.Add(new Godot.Collections.Dictionary
        {
            { "node_path", "node2" },
            { "status", (int)BtStatus.Running },
            { "elapsed_time", 4.0 }
        });
        
        var tickPayload = new Godot.Collections.Dictionary
        {
            { "tree_path", "/root/MyTree" },
            { "nodes", tickNodes }
        };

        view.HandleDebugTick(tickPayload);
        
        Check("P.Node1_Success_Color", nodesMap["node1"].SelfModulate == new Color(0.2f, 0.8f, 0.2f), true);
        Check("P.Node2_Running_Color", nodesMap["node2"].SelfModulate == new Color(0.3f, 0.6f, 1.0f), true);

        // 2) node2가 미보고된 새로운 틱을 보내면 node2는 기본 색상으로 복구되어야 함 (stale clear)
        var nextTickNodes = new Godot.Collections.Array();
        nextTickNodes.Add(new Godot.Collections.Dictionary
        {
            { "node_path", "node1" },
            { "status", (int)BtStatus.Failure },
            { "elapsed_time", 3.0 }
        });
        var nextTickPayload = new Godot.Collections.Dictionary
        {
            { "tree_path", "/root/MyTree" },
            { "nodes", nextTickNodes }
        };

        view.HandleDebugTick(nextTickPayload);
        
        Check("P.Node1_Updated_To_Failure", nodesMap["node1"].SelfModulate == new Color(0.9f, 0.3f, 0.3f), true);
        Check("P.Node2_Restored_Default", nodesMap["node2"].SelfModulate == defColor2, true);

        // 3) SetStaleState 호출 시 모든 노드가 stale 회색으로 변환하는지 확인
        view.SetStaleState();
        Check("P.Node1_Stale", nodesMap["node1"].SelfModulate == new Color(0.5f, 0.5f, 0.5f, 0.8f), true);
        Check("P.Node2_Stale", nodesMap["node2"].SelfModulate == new Color(0.5f, 0.5f, 0.5f, 0.8f), true);

        RemoveChild(view);
        view.QueueFree();
    }

    private void TestDebugGraphTickWithoutStructure()
    {
        GD.Print("[Q] Structure 없는 상태에서 Tick 수신 검증");
        var view = new BehaviorTreeDebugGraphView();
        AddChild(view);
        
        var tickNodes = new Godot.Collections.Array();
        tickNodes.Add(new Godot.Collections.Dictionary
        {
            { "node_path", "root" },
            { "status", (int)BtStatus.Success },
            { "elapsed_time", 1.0 }
        });
        var tickPayload = new Godot.Collections.Dictionary
        {
            { "tree_path", "/root/MyTree" },
            { "nodes", tickNodes }
        };

        try
        {
            view.HandleDebugTick(tickPayload);
            GD.Print("  PASS: Q.no_structure_tick_ignored");
        }
        catch (Exception ex)
        {
            _failures++;
            GD.Print($"  FAIL: Q.no_structure_tick exception: {ex.Message}");
        }

        RemoveChild(view);
        view.QueueFree();
    }

    private void TestEditorForwardsAllRemoteMessages()
    {
        GD.Print("[R] Editor 원격 메시지 전체 포워딩 검증");

        var editor = new BehaviorTreeEditor();
        var windowScene = GD.Load<PackedScene>("res://addons/behaviortree/debugger/DebuggerWindow.tscn");
        var window = windowScene.Instantiate<AutoCrawler.addons.behaviortree.debugger.DebuggerWindow>();
        AddChild(window);
        window.SetEditor(editor);

        var windowField = typeof(BehaviorTreeEditor).GetField("_debuggerWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        windowField.SetValue(editor, window);

        string treePath = "/root/ForwardTree";
        var registerPayload = new Godot.Collections.Dictionary
        {
            { "tree_path", treePath },
            { "article_name", "ForwardArticle" }
        };
        editor.HandleDebugMessage("behavior_tree:register", registerPayload);

        var discoveredField = typeof(AutoCrawler.addons.behaviortree.debugger.DebuggerWindow).GetField("_discoveredTrees", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var discovered = (System.Collections.Generic.Dictionary<string, string>)discoveredField.GetValue(window);
        Check("R.Register_Forwarded", discovered.ContainsKey(treePath), true);

        var nodesList = new Godot.Collections.Array
        {
            new Godot.Collections.Dictionary
            {
                { "node_path", "root" },
                { "name", "RootSelector" },
                { "type", "BehaviorTree_Selector" },
                { "parent_path", "" },
                { "graph_position", new Vector2(100, 100) }
            }
        };
        var structurePayload = new Godot.Collections.Dictionary
        {
            { "tree_path", treePath },
            { "nodes", nodesList }
        };
        editor.HandleDebugMessage("behavior_tree:structure", structurePayload);

        var tabContainer = window.GetNode<TabContainer>("MarginContainer/VBoxContainer/TabContainer");
        Check("R.Structure_Created_Tab", tabContainer.GetChildCount() == 1, true);

        var tickPayload = new Godot.Collections.Dictionary
        {
            { "tree_path", treePath },
            { "nodes", new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary
                    {
                        { "node_path", "root" },
                        { "status", (int)BtStatus.Success },
                        { "elapsed_time", 1.5 }
                    }
                }
            }
        };
        editor.HandleDebugMessage("behavior_tree:tick", tickPayload);

        var debugGraphView = tabContainer.GetChild(0).GetNodeOrNull<BehaviorTreeDebugGraphView>("DebugGraphView");
        var nodesMapField = typeof(BehaviorTreeDebugGraphView).GetField("_nodesMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var nodesMap = (System.Collections.Generic.Dictionary<string, GraphNode>)nodesMapField.GetValue(debugGraphView);
        Check("R.Tick_Forwarded", nodesMap["root"].SelfModulate == new Color(0.2f, 0.8f, 0.2f), true);

        var unregisterPayload = new Godot.Collections.Dictionary
        {
            { "tree_path", treePath }
        };
        editor.HandleDebugMessage("behavior_tree:unregister", unregisterPayload);
        Check("R.Unregister_Forwarded", discovered.ContainsKey(treePath), false);
        Check("R.Unregister_Stale", nodesMap["root"].SelfModulate == new Color(0.5f, 0.5f, 0.5f, 0.8f), true);

        RemoveChild(window);
        window.QueueFree();
    }

    private void TestRemoteMultiTreeRoutingIsolation()
    {
        GD.Print("[S] 다중 원격 tree_path 탭 라우팅 격리 검증");

        var window = MakeDebuggerWindow();
        string treePathA = "/root/Battle/AllyA/BehaviorTree";
        string treePathB = "/root/Battle/EnemyA/BehaviorTree";

        RegisterRemoteTree(window, treePathA, "Fighter");
        RegisterRemoteTree(window, treePathB, "Fighter");
        SendStructure(window, treePathA, "root_a", "AllyRoot");
        SendStructure(window, treePathB, "root_b", "EnemyRoot");

        var graphA = FindDebugGraphView(window, treePathA);
        var graphB = FindDebugGraphView(window, treePathB);
        var nodesA = GetDebugNodesMap(graphA);
        var nodesB = GetDebugNodesMap(graphB);

        SendTick(window, treePathA, "root_a", BtStatus.Success, 1.25);
        SendTick(window, treePathB, "root_b", BtStatus.Running, 2.5);

        Check("S.TreeA_Success_Color", nodesA["root_a"].SelfModulate == new Color(0.2f, 0.8f, 0.2f), true);
        Check("S.TreeB_Running_Color", nodesB["root_b"].SelfModulate == new Color(0.3f, 0.6f, 1.0f), true);

        SendTick(window, treePathA, "root_a", BtStatus.Failure, 3.0);

        Check("S.TreeA_Updated_To_Failure", nodesA["root_a"].SelfModulate == new Color(0.9f, 0.3f, 0.3f), true);
        Check("S.TreeB_Remains_Running", nodesB["root_b"].SelfModulate == new Color(0.3f, 0.6f, 1.0f), true);

        RemoveChild(window);
        window.QueueFree();
    }

    private void TestRemoteTabCloseStopsOnlyTarget()
    {
        GD.Print("[T] 원격 탭 닫기 stop 송신 대상 격리 검증");

        var window = MakeDebuggerWindow();
        string treePathA = "/root/Battle/AllyA/BehaviorTree";
        string treePathB = "/root/Battle/EnemyA/BehaviorTree";
        var stoppedPaths = new List<string>();
        window.DebugStopRequested += stoppedPaths.Add;

        RegisterRemoteTree(window, treePathA, "Ally");
        RegisterRemoteTree(window, treePathB, "Enemy");
        SendStructure(window, treePathA, "root_a", "AllyRoot");
        SendStructure(window, treePathB, "root_b", "EnemyRoot");

        var tabContainer = window.GetNode<TabContainer>("MarginContainer/VBoxContainer/TabContainer");
        Check("T.Two_Remote_Tabs", tabContainer.GetChildCount() == 2, true);

        window.CloseTab(0);

        Check("T.Stop_One_Path", stoppedPaths.Count == 1, true);
        Check("T.Stop_Target_A", stoppedPaths.Count > 0 && stoppedPaths[0] == treePathA, true);
        Check("T.Second_Tab_Remains", tabContainer.GetChildCount() == 1, true);
        Check("T.Remaining_Tab_Is_B", tabContainer.GetChild(0).GetMeta("tree_path").AsString() == treePathB, true);

        RemoveChild(window);
        window.QueueFree();
    }

    private AutoCrawler.addons.behaviortree.debugger.DebuggerWindow MakeDebuggerWindow()
    {
        var windowScene = GD.Load<PackedScene>("res://addons/behaviortree/debugger/DebuggerWindow.tscn");
        var window = windowScene.Instantiate<AutoCrawler.addons.behaviortree.debugger.DebuggerWindow>();
        AddChild(window);
        return window;
    }

    private static void RegisterRemoteTree(AutoCrawler.addons.behaviortree.debugger.DebuggerWindow window, string treePath, string articleName)
    {
        window.HandleDebugMessage("behavior_tree:register", new Godot.Collections.Dictionary
        {
            { "tree_path", treePath },
            { "article_name", articleName }
        });
    }

    private static void SendStructure(AutoCrawler.addons.behaviortree.debugger.DebuggerWindow window, string treePath, string nodePath, string nodeName)
    {
        window.HandleDebugMessage("behavior_tree:structure", new Godot.Collections.Dictionary
        {
            { "tree_path", treePath },
            { "nodes", new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary
                    {
                        { "node_path", nodePath },
                        { "name", nodeName },
                        { "type", "BehaviorTree_Selector" },
                        { "parent_path", "" },
                        { "graph_position", new Vector2(100, 100) }
                    }
                }
            }
        });
    }

    private static void SendTick(AutoCrawler.addons.behaviortree.debugger.DebuggerWindow window, string treePath, string nodePath, BtStatus status, double elapsedTime)
    {
        window.HandleDebugMessage("behavior_tree:tick", new Godot.Collections.Dictionary
        {
            { "tree_path", treePath },
            { "nodes", new Godot.Collections.Array
                {
                    new Godot.Collections.Dictionary
                    {
                        { "node_path", nodePath },
                        { "status", (int)status },
                        { "elapsed_time", elapsedTime }
                    }
                }
            }
        });
    }

    private static BehaviorTreeDebugGraphView FindDebugGraphView(AutoCrawler.addons.behaviortree.debugger.DebuggerWindow window, string treePath)
    {
        var tabContainer = window.GetNode<TabContainer>("MarginContainer/VBoxContainer/TabContainer");
        for (int i = 0; i < tabContainer.GetChildCount(); i++)
        {
            var child = tabContainer.GetChild(i);
            if (child.HasMeta("tree_path") && child.GetMeta("tree_path").AsString() == treePath)
            {
                return child.GetNodeOrNull<BehaviorTreeDebugGraphView>("DebugGraphView");
            }
        }

        return null;
    }

    private static System.Collections.Generic.Dictionary<string, GraphNode> GetDebugNodesMap(BehaviorTreeDebugGraphView view)
    {
        var nodesMapField = typeof(BehaviorTreeDebugGraphView).GetField("_nodesMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (System.Collections.Generic.Dictionary<string, GraphNode>)nodesMapField.GetValue(view);
    }
}

// 테스트를 위해 추상 클래스를 구체화한 임시 클래스 정의
public partial class TestDecoratorNode : BehaviorTree_Decorator
{
    protected override bool IsValid(BehaviorTree_Node child, double delta, Node owner)
    {
        return true;
    }
}

public partial class TestActionNode : BehaviorTree_Action
{
    protected override BtStatus PerformAction(double delta, Node owner)
    {
        return BtStatus.Success;
    }
}
#endif
