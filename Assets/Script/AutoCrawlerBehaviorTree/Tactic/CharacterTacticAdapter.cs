using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 실제 전장 어댑터(BT-002 Step 3.5). 택틱 런타임에 타일/AStar/상대/이동을 제공한다.
//
// 무변형 계약(R3): 성립 검사는 전장 상태(타일 점유 `_placedArticles`, 유닛 위치, 자원)를 변형하지 않는다.
// AStar는 `BattleFieldTileMapLayer.UpdateAStar`가 채우는 **이 어댑터의 사본**을 쓰므로, 그 위에서
// `SetPointSolid`를 해도 월드에는 영향이 없다. 실제 이동은 Approach에서만 일어난다.
//
// 이동량 단일 소스(OD5): 한 턴 경로 노드 예산 = `Mobility + 1`(시작 타일 포함) → 실제 이동 최대 `Mobility`칸.
// 성립 검사(CanReachAndActThisTurn)와 실제 이동(Approach)이 같은 예산·같은 정규 경로 규칙을 공유한다.
public sealed class CharacterTacticAdapter : ITacticActionAdapter
{
    private AStarGrid2D _aStar;

    // 이동 연출 소유권(Step 4): 남은 스텝은 좌표로만 들고, 살아 있는 tween은 항상 최대 1개다.
    private readonly Queue<Vector2I> _pendingSteps = new();
    private Tween _stepTween;
    private Vector2I _stepTarget;
    private double _elapsed;

    private static BattleFieldTileMapLayer TileMap => BattleFieldScene.BattleField?.BattleFieldTileMap;

    // 한 턴 경로 노드 예산 = Mobility + 1 (시작 타일 포함). 기존 이동 노드와 같은 단일 소스.
    private static int MovementNodeBudget(Node owner)
    {
        if (owner is not ArticleBase article) return 1;
        article.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Mobility), out var element);
        return ((element as Mobility)?.Value ?? 2) + 1;
    }

    public IReadOnlyList<GodotObject> GetLivingOpponents(Node owner)
    {
        var container = BattleFieldScene.BattleField?.Articles;
        if (container == null || owner is not ArticleBase article) return Array.Empty<GodotObject>();

        return container.GetOpponentArticles(article)
            .Where(opponent => opponent is { IsAlive: true })
            .Cast<GodotObject>()
            .ToList();
    }

    // --- mutation-free feasibility ---

    public bool IsInRange(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets)
    {
        if (owner is not ArticleBase o || target is not ArticleBase t || !t.IsAlive) return false;
        return IsInRangeFrom(o.TilePosition, t.TilePosition, rangeOffsets);
    }

    private static bool IsInRangeFrom(Vector2I from, Vector2I targetPosition, IReadOnlyList<Vector2I> rangeOffsets)
        => rangeOffsets.Any(offset => from + offset == targetPosition);

    // 이번 턴 이동력(Mobility+1 노드) 안에서 사거리 진입이 가능한가.
    public bool CanReachAndActThisTurn(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets)
        => FindApproachPath(owner, target, rangeOffsets, MovementNodeBudget(owner)) != null;

    // 턴을 넘겨서라도 사거리까지 이르는 경로가 있는가.
    public bool HasApproachPath(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets)
        => FindApproachPath(owner, target, rangeOffsets, int.MaxValue) != null;

    // owner가 서면 target이 사거리에 들어오는, 서 있을 수 있는 타일들.
    private static List<Vector2I> GoalTiles(ArticleBase owner, ArticleBase target,
        IReadOnlyList<Vector2I> rangeOffsets, AStarGrid2D grid)
    {
        Rect2I region = TileMap.GetUsedRect();
        var goals = new List<Vector2I>();
        foreach (Vector2I offset in rangeOffsets)
        {
            // owner가 g에 서면 사거리 안: g + offset == target  =>  g = target - offset
            Vector2I goal = target.TilePosition - offset;
            if (!region.HasPoint(goal)) continue;
            if (goal != owner.TilePosition && grid.IsPointSolid(goal)) continue;   // 점유 타일 제외
            goals.Add(goal);
        }
        return goals;
    }

    // 어댑터 사본 grid를 전장 현재 상태로 갱신한다(월드 무변형). 자기 타일은 출발 가능하도록 비운다.
    private AStarGrid2D BuildGrid(ArticleBase owner)
    {
        TileMap.UpdateAStar(ref _aStar);
        _aStar.SetPointSolid(owner.TilePosition, false);
        return _aStar;
    }

    // 예산 안에서 사거리 진입 타일까지 가는 정규 경로(ADR-017). 없으면 null.
    private Godot.Collections.Array<Vector2I> FindApproachPath(Node owner, GodotObject target,
        IReadOnlyList<Vector2I> rangeOffsets, int nodeBudget)
    {
        if (owner is not ArticleBase o || target is not ArticleBase t || !t.IsAlive || TileMap == null) return null;
        if (rangeOffsets == null || rangeOffsets.Count == 0) return null;

        AStarGrid2D grid = BuildGrid(o);
        List<Vector2I> goals = GoalTiles(o, t, rangeOffsets, grid);
        if (goals.Count == 0) return null;

        // 이동이 필요한 경로만(Count>=2). 이미 사거리 안이면 IsInRange가 먼저 참이므로 여기 오지 않는다.
        var candidates = goals
            .Select(goal => grid.GetIdPath(o.TilePosition, goal, true))
            .Where(path => path.Count >= 2 && path.Count <= nodeBudget);

        return SkillUtil.SelectCanonicalPath(candidates, o.TilePosition);
    }

    // 이번 턴에 도달할 수 없을 때, 가능한 만큼만 접근하는 부분 경로(ApproachAllowed).
    private Godot.Collections.Array<Vector2I> FindPartialPath(ArticleBase owner, ArticleBase target,
        IReadOnlyList<Vector2I> rangeOffsets)
    {
        Godot.Collections.Array<Vector2I> full = FindApproachPath(owner, target, rangeOffsets, int.MaxValue);
        if (full == null) return null;

        int budget = MovementNodeBudget(owner);
        if (full.Count <= budget) return full;

        var truncated = new Godot.Collections.Array<Vector2I>();
        for (int i = 0; i < budget; i++) truncated.Add(full[i]);
        return truncated;
    }

    // --- commit ---

    public ApproachStep Approach(Node owner, GodotObject target, IReadOnlyList<Vector2I> rangeOffsets, double delta)
    {
        if (owner is not ArticleBase o || TileMap == null) return ApproachStep.Exhausted;
        ArticleBase t = target as ArticleBase;

        // 진행 중인 이동 연출을 이어간다.
        if (_stepTween != null || _pendingSteps.Count > 0) return ContinueMove(o, t, rangeOffsets, delta);

        if (t == null || !t.IsAlive) return ApproachStep.Exhausted;

        // 이번 턴에 사거리 진입이 가능하면 그 경로, 아니면 갈 수 있는 만큼만.
        Godot.Collections.Array<Vector2I> path =
            FindApproachPath(owner, target, rangeOffsets, MovementNodeBudget(owner))
            ?? FindPartialPath(o, t, rangeOffsets);

        if (path == null || path.Count < 2) return ApproachStep.Exhausted;

        // path[0]은 현재 타일. 이후 타일만 스텝으로 쌓는다(tween은 스텝마다 하나씩 만든다).
        _pendingSteps.Clear();
        for (int i = 1; i < path.Count; i++) _pendingSteps.Enqueue(path[i]);

        o.AnimationPlayer?.Play("Walk");
        return ContinueMove(o, t, rangeOffsets, delta);
    }

    // 이동 연출은 **한 번에 tween 하나만** 살려 둔다(Step 4). 경로 전체를 미리 tween으로 만들어 두면 턴이
    // 중간에 끝나거나 종료될 때 소유자 없는 tween이 남아 누수로 보고된다.
    private ApproachStep ContinueMove(ArticleBase owner, ArticleBase target,
        IReadOnlyList<Vector2I> rangeOffsets, double delta)
    {
        if (_stepTween == null)
        {
            if (_pendingSteps.Count == 0) return FinishMove(owner, target, rangeOffsets);

            _stepTarget = _pendingSteps.Dequeue();
            owner.DecisionFlipH(_stepTarget);
            _stepTween = owner.CreateTween();
            _stepTween.TweenProperty(owner, "global_position",
                TileMap.ToGlobal(TileMap.MapToLocal(_stepTarget)), 1f);
            _stepTween.Pause();
            _elapsed = 0;
        }

        if (!_stepTween.CustomStep(_elapsed))
        {
            KillStepTween();
            owner.TilePosition = _stepTarget;
            _elapsed = 0;
            return _pendingSteps.Count == 0
                ? FinishMove(owner, target, rangeOffsets)
                : ApproachStep.Moving;
        }

        _elapsed += delta;
        return ApproachStep.Moving;
    }

    private static ApproachStep FinishMove(ArticleBase owner, ArticleBase target,
        IReadOnlyList<Vector2I> rangeOffsets)
    {
        owner.AnimationPlayer?.Play("Idle");
        bool arrived = target is { IsAlive: true }
                       && IsInRangeFrom(owner.TilePosition, target.TilePosition, rangeOffsets);
        return arrived ? ApproachStep.Arrived : ApproachStep.Exhausted;
    }

    // Kill()은 tween을 멈출 뿐 managed wrapper의 native 참조는 놓지 않는다. Dispose까지 해야 refcount가
    // 결정적으로 떨어져 종료 시 "leaked instance"로 보고되지 않는다(Step 4).
    private void KillStepTween()
    {
        if (_stepTween == null) return;
        if (GodotObject.IsInstanceValid(_stepTween)) _stepTween.Kill();
        _stepTween.Dispose();
        _stepTween = null;
    }

    // 진행 중 이동을 취소한다(tween 누수 방지). 이미 확정된 타일 이동은 되돌리지 않는다.
    public void CancelApproach(Node owner)
    {
        KillStepTween();
        _pendingSteps.Clear();
        _elapsed = 0;
        if (owner is ArticleBase article && GodotObject.IsInstanceValid(article))
            article.AnimationPlayer?.Play("Idle");
    }

    // 새 자기 턴: 남은 이동 연출을 정리한다(이동 예산은 Approach가 매 턴 새로 계산한다).
    public void ResetTurn(Node owner) => CancelApproach(owner);
}
