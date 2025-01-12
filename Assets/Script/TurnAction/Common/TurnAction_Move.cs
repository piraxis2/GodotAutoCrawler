using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Common;

public partial class TurnAction_Move : TurnActionBase
{
    private AStarGrid2D _aStar2D;
    private Vector2I? _targetPosition;
    private Tween _tween;
    private double _elapsedTime;


    protected override void OnInit(Node owner)
    {
    }

    private Vector2I? FindTarget(ArticleBase owner, BattleFieldTileMapLayer tileMapLayer)
    {
        tileMapLayer.UpdateAStar(ref _aStar2D);

        if (owner is not CharacterArticle characterArticle) return null;

        var articlesContainer = GlobalUtil.GetBattleField(owner)?.GetBattleFieldCoreNode<ArticlesContainer>();
        if (articlesContainer == null) return null;

        var opponentList = articlesContainer.GetOpponentArticles(owner);
        if (opponentList.Count == 0) return null;

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

        targetPointList.Sort((a, b) => (a - characterArticle.TilePosition).LengthSquared().CompareTo((b - characterArticle.TilePosition).LengthSquared()));
        var path = _aStar2D.GetIdPath(characterArticle.TilePosition, targetPointList[0], true);

        if (path.Count < 2) return null;

        return path.Count > 1 ? path[1] : null;
    }

    private ActionState GetEnd()
    {
        _targetPosition = null;
        _tween?.Kill();
        _tween = null;
        _elapsedTime = 0;
        return ActionState.End;
    }

    protected override ActionState ActionExecute(double delta, ArticleBase owner)
    {
        if (_tween != null)
        {
            if (!_tween.CustomStep(_elapsedTime))
            {
                owner.TilePosition = _targetPosition!.Value;
                return GetEnd();
            }
            
            _elapsedTime += delta;
            return ActionState.Running;
        }
        
        var tileMapLayer = GlobalUtil.GetBattleField(owner)?.GetBattleFieldCoreNode<BattleFieldTileMapLayer>();
        
        if (tileMapLayer == null) return GetEnd();

        _targetPosition ??= FindTarget(owner, tileMapLayer);
        
        if (_targetPosition == null) return GetEnd();
        
        Vector2 to = tileMapLayer.ToGlobal(tileMapLayer.MapToLocal(_targetPosition.Value));

        _tween = owner.CreateTween();
        _tween.TweenProperty(owner, "global_position", to, 1f);
        _tween.Pause();
        
        return ActionState.Running;
    }
}