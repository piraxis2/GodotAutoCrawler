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
	

}