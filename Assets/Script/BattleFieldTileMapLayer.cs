using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Util;
using Godot;
namespace AutoCrawler.Assets.Script;

public partial class BattleFieldTileMapLayer : TileMapLayer
{
	private readonly Dictionary<Vector2I, ArticleBase> _placedArticles = new();


	public ArticleBase GetArticle(Vector2I position)
	{
		return _placedArticles.GetValueOrDefault(position);
	}
	public override void _Ready()
	{
        ArticlesContainer articlesContainer = GlobalUtil.GetBattleField(this)?.GetBattleFieldCoreNode<ArticlesContainer>();
        if (articlesContainer == null) return;
        
        foreach (var (key, value) in articlesContainer.Articles!)
        {
	        foreach (var article in value)
	        {
		        var position = LocalToMap(ToLocal(article.GlobalPosition));
		        article.GlobalPosition = ToGlobal(MapToLocal(position));
		        article.TilePosition = position;
		        article.OnMove += OnArticleMove;
		        _placedArticles[article.TilePosition] = article;
	        }
        }
	}

	private void OnArticleMove(Vector2I from, Vector2I to, ArticleBase article)
	{
		if (from == to) return;

		_placedArticles[from] = null;	
		_placedArticles[to] = article;
	}

	public void UpdateAStar(ref AStarGrid2D aStar2D)
	{
		if (aStar2D == null) 
		{
			aStar2D = new AStarGrid2D();
			aStar2D.Region = GetUsedRect();
			aStar2D.CellSize = GetTileSet().TileSize;
			aStar2D.DiagonalMode = AStarGrid2D.DiagonalModeEnum.Never;
			aStar2D.Update();
		}

		aStar2D.FillSolidRegion(aStar2D.Region, false);

		foreach (var (position, article) in _placedArticles)
		{
			aStar2D.SetPointSolid(position);
		}
		
		aStar2D.Update();
	}
}
