using System.Collections.Generic;
using Godot;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.addons.behaviortree.node.Rating;

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
    RatingSelectorInvalidChild  // RatingSelector 하위에 RatingDecorator가 아닌 노드가 존재함 (경고)
}

/// <summary>
/// 유효성 검사 결과 데이터 클래스
/// </summary>
public class BtValidationResult
{
    public BtValidationErrorType ErrorType { get; set; } = BtValidationErrorType.None;
    public string Message { get; set; } = string.Empty;
    public bool IsError => ErrorType == BtValidationErrorType.DecoratorExtraChild || ErrorType == BtValidationErrorType.ActionHasChild;
    public bool IsWarning => ErrorType == BtValidationErrorType.NoRoot || ErrorType == BtValidationErrorType.InvalidRootType || ErrorType == BtValidationErrorType.RatingSelectorInvalidChild;
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
