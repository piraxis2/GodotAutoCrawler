using System;
using System.Collections;
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
    private Queue<Vector2I> _path;
    private Vector2I? _targetPosition;
    private Tween _moveTween;
    private double _elapsedTime;

    protected override void OnInit(Node owner)
    {
    }

    private Queue<Vector2I> GetPath(ArticleBase owner, BattleFieldTileMapLayer tileMapLayer)
    {
        tileMapLayer.UpdateAStar(ref _aStar2D);

        if (owner is not CharacterArticle characterArticle) return null;

        var articlesContainer = GlobalUtil.GetBattleField(owner)?.GetBattleFieldCoreNode<ArticlesContainer>();
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
        
        targetPointList.Sort((a, b) => (a - owner.TilePosition).LengthSquared().CompareTo((b - owner.TilePosition).LengthSquared()));
        var path = _aStar2D.GetIdPath(owner.TilePosition, targetPointList[0], true);

        owner.ArticleStatus.StatusElementsDictionary.TryGetValue(typeof(Mobility), out var mobility);
        int mobilityValue = (mobility as Mobility)?.Value ?? 1;


        if (path.Count < 2) return null;
        
        Queue<Vector2I> queue = new Queue<Vector2I>();
        foreach (var pathPosition in path.Slice(0, mobilityValue))
        {
            queue.Enqueue(pathPosition);
        }

        return queue;
    }


    protected override Constants.BtStatus PerformAction(double delta, Node owner)
    {
        if (owner is not ArticleBase article) return Constants.BtStatus.Failure;
        
        // if (_path == null)

        return Constants.BtStatus.Failure;
    }
}