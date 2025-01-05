using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class BattleFieldTileMapLayer : TileMapLayer
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        var userRect = GetUsedRect();
        var tilemapSize = userRect.End - userRect.Position;
        var tileSize = GetTileSet().TileSize;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		
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