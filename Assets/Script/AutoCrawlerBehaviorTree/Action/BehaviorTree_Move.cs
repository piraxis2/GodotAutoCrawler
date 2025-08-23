using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;

[GlobalClass, Tool]
public partial class BehaviorTree_Move : BehaviorTree_Action
{
    private AStarGrid2D _aStar2D;
    private Vector2I? _targetPosition;
    private Tween _moveTween;
    private double _elapsedTime;

    protected override void OnInit(Node owner)
    {
    }

    private Vector2I? FindTarget(ArticleBase owner, BattleFieldTileMapLayer tileMapLayer)
    {
        tileMapLayer.UpdateAStar(ref _aStar2D);

        if (owner is not CharacterArticle characterArticle) return null;

        var articlesContainer = GlobalUtil.GetBattleFieldCoreNode<ArticlesContainer>(owner);
        if (articlesContainer == null) return null;

        var opponentList = articlesContainer.GetOpponentArticles(owner);
        if (opponentList.Count == 0) return null;

        // 대상 캐릭터의 주변으로 이동할 준비를 한다.
        List<Vector2I> targetPointList = new();
        foreach (var opponent in opponentList)
        {
            var directions = new List<Vector2I>
            {
                opponent.TilePosition + Vector2I.Right, // 동
                opponent.TilePosition + Vector2I.Left, // 서
                opponent.TilePosition + Vector2I.Down, // 남
                opponent.TilePosition + Vector2I.Up, // 북
            };

            targetPointList.AddRange(directions.Where(direction => tileMapLayer.GetUsedRect().HasPoint(direction) && !_aStar2D.IsPointSolid(direction)));
        }

        if (targetPointList.Count == 0) return null;

        // 가장 가까운 타겟을 찾는다.
        targetPointList.Sort((a, b) => (a - characterArticle.TilePosition).LengthSquared().CompareTo((b - characterArticle.TilePosition).LengthSquared()));
        var path = _aStar2D.GetIdPath(characterArticle.TilePosition, targetPointList[0], true);

        if (path.Count < 2) return null;

        return path.Count > 1 ? path[1] : null;
    }

    private BtStatus ActionExecuted()
    {
        _targetPosition = null;
        _moveTween?.Kill();
        _moveTween = null;
        _elapsedTime = 0;
        return BtStatus.Success;
    }

    protected override BtStatus PerformAction(double delta, Node owner)
    {
        if (owner is not ArticleBase article) return BtStatus.Failure;

        if (_moveTween != null)
        {
            if (!_moveTween.CustomStep(_elapsedTime))
            {
                article.TilePosition = _targetPosition ?? article.TilePosition;
                article.AnimationPlayer.Play("Idle");
                return ActionExecuted();
            }

            _elapsedTime += delta;
            return BtStatus.Running;
        }

        var tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(article)
                           ?? throw new NullReferenceException("TileMapLayer is null");

        _targetPosition ??= FindTarget(article, tileMapLayer);

        if (_targetPosition == null) return ActionExecuted();

        var targetGlobalPosition = tileMapLayer.ToGlobal(tileMapLayer.MapToLocal(_targetPosition.Value));

        _moveTween = article.CreateTween();
        _moveTween.TweenProperty(article, "global_position", targetGlobalPosition, 1f);
        _moveTween.Pause();
        article.AnimationPlayer.Play("Walk");

        return BtStatus.Running;
    }
}