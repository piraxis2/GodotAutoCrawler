using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article;
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
		Node articleContainer = GetNode("../Articles");
        var articles = (Godot.Collections.Array<Node>)articleContainer.Call("getAllArticles");
        foreach (var node in articles)
        {
	        if (node is not ArticleBase article) continue;

	        article.OnMove += OnArticleMove;
	        _placedArticles.Add(article.TilePosition, article);
        }
	}

	private void OnArticleMove(Vector2I from, Vector2I to, ArticleBase article)
	{
		if (from == to) return;

		try
		{
			_placedArticles.Remove(from);
		}
		catch (Exception e)
		{
			GD.PrintErr(e);
		}
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
		}

		aStar2D.FillSolidRegion(aStar2D.Region, false);

		foreach (var (position, article) in _placedArticles)
		{
			aStar2D.SetPointSolid(position);
		}
		
		aStar2D.Update();
	}
	

}
