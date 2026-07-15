using System.Collections.Generic;
using Godot;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.addons.behaviortree.node.Rating;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

namespace AutoCrawler.addons.behaviortree;

/// <summary>
/// BehaviorTree 유효성 검사 에러 종류 정의
/// </summary>
public enum BtValidationErrorType
{
    None,
    NoRoot,                     // 루트 노드 없음
    InvalidRootType,            // 루트 노드가 BehaviorTree_Node가 아님
    DecoratorExtraChild,        // Decorator 노드가 2개 이상의 BehaviorTree_Node 자식을 가짐
    ActionHasChild,             // Action 노드가 자식을 가짐
    RatingSelectorInvalidChild, // RatingSelector 하위에 RatingDecorator가 아닌 노드가 존재함 (경고)

    // --- 택틱보드 구조(BT-002 Step 4). 런타임 fail-closed와 같은 규칙을 편집 시점에 표면화한다(ADR-023 §9). ---
    TacticSelectorInvalidChild, // 택틱 PrioritySelector의 자식이 TacticRow가 아님
    TacticRowNoAction,          // 택틱 Row에 행동(마지막 자식)이 없음
    TacticRowInvalidAction,     // 택틱 Row의 마지막 자식이 택틱 행동이 아님
    TacticRowSelectorMisplaced, // 타깃 셀렉터가 마지막 pre-action 자식이 아니거나 2개 이상임
    TacticSequenceInvalidChild  // 행동 시퀀스의 자식이 택틱 노드가 아님
}

/// <summary>
/// 유효성 검사 결과 데이터 클래스
/// </summary>
public class BtValidationResult
{
    public BtValidationErrorType ErrorType { get; set; } = BtValidationErrorType.None;
    public string Message { get; set; } = string.Empty;

    // 택틱 구조 위반은 런타임에서 fail-closed(행 미실행)되므로 경고가 아니라 오류다.
    public bool IsError =>
        ErrorType is BtValidationErrorType.DecoratorExtraChild
            or BtValidationErrorType.ActionHasChild
            or BtValidationErrorType.TacticSelectorInvalidChild
            or BtValidationErrorType.TacticRowNoAction
            or BtValidationErrorType.TacticRowInvalidAction
            or BtValidationErrorType.TacticRowSelectorMisplaced
            or BtValidationErrorType.TacticSequenceInvalidChild;

    public bool IsWarning => ErrorType is BtValidationErrorType.NoRoot
        or BtValidationErrorType.InvalidRootType
        or BtValidationErrorType.RatingSelectorInvalidChild;
}

/// <summary>
/// BehaviorTree 노드 트리의 구조적 유효성 검사를 담당하는 정적 헬퍼 클래스
/// </summary>
public static class BehaviorTreeValidation
{
    /// <summary>
    /// 지정된 BehaviorTree에 대해 유효성 검사를 수행합니다.
    /// </summary>
    /// <param name="tree">검사할 BehaviorTree 인스턴스</param>
    /// <returns>노드별 검사 결과 매핑 딕셔너리</returns>
    public static Dictionary<Node, BtValidationResult> ValidateTree(BehaviorTree tree)
    {
        var results = new Dictionary<Node, BtValidationResult>();
        if (tree == null) return results;

        // 1. Root 검사
        if (tree.GetChildCount() == 0)
        {
            results[tree] = new BtValidationResult
            {
                ErrorType = BtValidationErrorType.NoRoot,
                Message = "BehaviorTree에 자식 노드(루트)가 지정되지 않았습니다."
            };
            return results;
        }

        var firstChild = tree.GetChild(0);
        if (!(firstChild is BehaviorTree_Node))
        {
            results[firstChild] = new BtValidationResult
            {
                ErrorType = BtValidationErrorType.InvalidRootType,
                Message = $"루트 노드(0번째 자식 '{firstChild.Name}')가 BehaviorTree_Node 타입이 아닙니다."
            };
        }

        // 2. 전체 노드 수집 및 자식 수 제약 조건 검사
        var allNodes = new List<Node>();
        GatherAllNodes(tree, allNodes);

        foreach (var node in allNodes)
        {
            // BehaviorTree 루트 노드 자체는 검사 건너뜀
            if (node == tree) continue;

            if (node is BehaviorTree_Decorator decorator)
            {
                var children = node.GetChildren();
                int btChildCount = 0;
                foreach (var child in children)
                {
                    if (child is BehaviorTree_Node)
                    {
                        btChildCount++;
                    }
                }

                if (btChildCount > 1)
                {
                    results[node] = new BtValidationResult
                    {
                        ErrorType = BtValidationErrorType.DecoratorExtraChild,
                        Message = $"Decorator 노드는 자식을 1개만 가져야 하지만, 현재 {btChildCount}개의 BehaviorTree 노드가 감지되었습니다."
                    };
                }
            }
            else if (node is BehaviorTree_Action action)
            {
                var children = node.GetChildren();
                int btChildCount = 0;
                foreach (var child in children)
                {
                    if (child is BehaviorTree_Node)
                    {
                        btChildCount++;
                    }
                }

                if (btChildCount > 0)
                {
                    results[node] = new BtValidationResult
                    {
                        ErrorType = BtValidationErrorType.ActionHasChild,
                        Message = $"Action 노드는 자식을 가질 수 없지만, 현재 {btChildCount}개의 BehaviorTree 노드가 감지되었습니다."
                    };
                }
            }
            else if (node is TacticPrioritySelector)
            {
                // 택틱 selector는 택틱 노드(행)만 실행한다. 일반 BT 노드는 commit 규칙을 우회한다.
                foreach (var child in node.GetChildren())
                {
                    if (child is BehaviorTree_Node && child is not ITacticNode)
                    {
                        results[node] = new BtValidationResult
                        {
                            ErrorType = BtValidationErrorType.TacticSelectorInvalidChild,
                            Message = $"택틱 PrioritySelector의 자식 '{child.Name}'이 택틱 행이 아닙니다. 실행되지 않습니다."
                        };
                        break;
                    }
                }
            }
            else if (node is TacticRow)
            {
                var btChildren = new List<Node>();
                foreach (var child in node.GetChildren())
                {
                    if (child is BehaviorTree_Node) btChildren.Add(child);
                }

                if (btChildren.Count == 0)
                {
                    results[node] = new BtValidationResult
                    {
                        ErrorType = BtValidationErrorType.TacticRowNoAction,
                        Message = "택틱 행에 행동이 없습니다. 마지막 자식이 행동이어야 합니다."
                    };
                }
                else if (btChildren[^1] is not ITacticNode)
                {
                    results[node] = new BtValidationResult
                    {
                        ErrorType = BtValidationErrorType.TacticRowInvalidAction,
                        Message = $"택틱 행의 마지막 자식 '{btChildren[^1].Name}'이 택틱 행동이 아닙니다. commit 구분을 보고하지 못합니다."
                    };
                }
                else
                {
                    // 타깃 셀렉터는 마지막 pre-action 자식(행동 바로 앞)이어야 하고 최대 하나다. 셀렉터 뒤에
                    // 대상 조건이 오면 후보 교집합을 우회한다.
                    int lastPreAction = btChildren.Count - 2;
                    for (int i = 0; i <= lastPreAction; i++)
                    {
                        if (btChildren[i] is ITacticTargetSelector && i != lastPreAction)
                        {
                            results[node] = new BtValidationResult
                            {
                                ErrorType = BtValidationErrorType.TacticRowSelectorMisplaced,
                                Message = $"타깃 셀렉터 '{btChildren[i].Name}'는 행동 바로 앞(마지막 pre-action)에 하나만 있어야 합니다."
                            };
                            break;
                        }
                    }
                }
            }
            else if (node is TacticActionSequence)
            {
                foreach (var child in node.GetChildren())
                {
                    if (child is BehaviorTree_Node && child is not ITacticNode)
                    {
                        results[node] = new BtValidationResult
                        {
                            ErrorType = BtValidationErrorType.TacticSequenceInvalidChild,
                            Message = $"행동 시퀀스의 자식 '{child.Name}'이 택틱 노드가 아닙니다. 실행되지 않습니다."
                        };
                        break;
                    }
                }
            }
            else if (node is BehaviorTree_RatingSelector ratingSelector)
            {
                var children = node.GetChildren();
                bool hasInvalidChild = false;
                foreach (var child in children)
                {
                    if (child is BehaviorTree_Node btNode && !(btNode is BehaviorTree_RatingDecorator))
                    {
                        hasInvalidChild = true;
                        break;
                    }
                }

                if (hasInvalidChild)
                {
                    results[node] = new BtValidationResult
                    {
                        ErrorType = BtValidationErrorType.RatingSelectorInvalidChild,
                        Message = "RatingSelector의 모든 자식 노드는 BehaviorTree_RatingDecorator 타입이어야 합니다."
                    };
                }
            }
        }

        return results;
    }

    /// <summary>
    /// DFS 방식으로 노드 트리 아래의 모든 노드를 재귀 수집합니다.
    /// </summary>
    private static void GatherAllNodes(Node current, List<Node> list)
    {
        if (current == null) return;
        list.Add(current);
        foreach (var child in current.GetChildren())
        {
            GatherAllNodes(child, list);
        }
    }
}
