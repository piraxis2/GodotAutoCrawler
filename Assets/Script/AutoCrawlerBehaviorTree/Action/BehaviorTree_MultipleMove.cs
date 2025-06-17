using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Element;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Action;


[GlobalClass, Tool]
public partial class BehaviorTree_MultipleMove : BehaviorTree_Action
{
    private AStarGrid2D _aStar2D;
    private Queue<KeyValuePair<Vector2I, Tween>> _moveTweenQueue = new();
    private double _elapsedTime;

    protected override void OnInit(Node owner) { }

    private Line2D _line2D;
    private List<Vector2I> FindPath(ArticleBase owner, BattleFieldTileMapLayer tileMapLayer)
    {
        tileMapLayer.UpdateAStar(ref _aStar2D);

        if (owner is not CharacterArticle) return null;

        var articlesContainer = GlobalUtil.GetBattleFieldCoreNode<ArticlesContainer>(owner);
        if (articlesContainer == null) return null;

        var opponentList = articlesContainer.GetOpponentArticles(owner);
        if (opponentList.Count == 0) return null;

        var targetPointList = opponentList
            .SelectMany(opponent => new[]
            {
                opponent.TilePosition + Vector2I.Right,
                opponent.TilePosition + Vector2I.Left,
                opponent.TilePosition + Vector2I.Down,
                opponent.TilePosition + Vector2I.Up
            })
            .Where(direction => tileMapLayer.GetUsedRect().HasPoint(direction) && !_aStar2D.IsPointSolid(direction))
            .ToList();

        if (targetPointList.Count == 0) return null;

        targetPointList.Sort((a, b) => (a - owner.TilePosition).LengthSquared().CompareTo((b - owner.TilePosition).LengthSquared()));
        var path = _aStar2D.GetIdPath(owner.TilePosition, targetPointList[0], true);

        owner.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Mobility), out var mobility);
        int mobilityValue = ((mobility as Mobility)?.Value ?? 2) + 1;


        var pathResult = path.Count < 2 ? null : new List<Vector2I>(path.Take(mobilityValue));
        _line2D?.QueueFree();
        
        if (pathResult != null)
        {
            _line2D = new Line2D
            {
                Points = pathResult.Select(elem => tileMapLayer.ToGlobal(tileMapLayer.MapToLocal(elem))).ToArray(),
                DefaultColor = Colors.Red,
                Width = 1.0f
            };

            GlobalUtil.GetBattleField(owner).AddChild(_line2D);
        }

        return pathResult;
    }

    private Constants.BtStatus ActionExecuted()
    {
        _moveTweenQueue.Clear();
        _elapsedTime = 0;
        return Constants.BtStatus.Success;
    }

    protected override Constants.BtStatus PerformAction(double delta, Node owner)
    {
        if (owner is not ArticleBase article) return Constants.BtStatus.Failure;

        if (_moveTweenQueue.Count > 0)
        {
            if (!_moveTweenQueue.Peek().Value.CustomStep(_elapsedTime))
            {
                var moveTween = _moveTweenQueue.Dequeue();
                moveTween.Value.Kill();
                article.TilePosition = moveTween.Key;
                _elapsedTime = 0;

                if (_moveTweenQueue.Count == 0)
                {
                    article.AnimationPlayer.Play("Idle");
                    return ActionExecuted();
                }
            }
            _elapsedTime += delta;
            return Constants.BtStatus.Running;
        }

        var tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(article);
        if (tileMapLayer == null) throw new NullReferenceException("TileMapLayer is null");

        _moveTweenQueue.Clear();

        var path = FindPath(article, tileMapLayer);
        if (path == null) return ActionExecuted();
        foreach (var pathNode in path)
        {
            var tween = article.CreateTween();
            tween.TweenProperty(article, "global_position", tileMapLayer.ToGlobal(tileMapLayer.MapToLocal(pathNode)), 1f);
            tween.Pause();
            _moveTweenQueue.Enqueue(new KeyValuePair<Vector2I, Tween>(pathNode, tween));
        }

        if (_moveTweenQueue.Count == 0) return ActionExecuted();

        article.AnimationPlayer.Play("Walk");
        return Constants.BtStatus.Running;
    }
}