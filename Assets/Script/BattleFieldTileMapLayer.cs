using AutoCrawler.Assets.Script.Article;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script;

public partial class BattleFieldTileMapLayer : TileMapLayer
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Node articleContainer = GetNode("../Articles");
        var articles = (Array<Node>)articleContainer.Call("getAllArticles");
        foreach (var node in articles)
        {
	        if (node is ArticleBase article)
	        {
		        var position = LocalToMap(article.GlobalPosition);
		        article.GlobalPosition = MapToLocal(position);
	        }
        }
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
		
		aStar2D.Update();
	}
}