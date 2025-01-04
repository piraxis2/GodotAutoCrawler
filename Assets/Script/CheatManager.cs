using Godot;

namespace AutoCrawler.Assets.Script;

public partial class CheatManager : Node
{
	public override void _Input(InputEvent @event)
	{
		// 현재 씬 가져오기
		Node currentScene = GetTree().CurrentScene;

		// TileMapLayer 노드 찾기
		BattleFieldTileMapLayer tileMapLayer = currentScene.GetNode<BattleFieldTileMapLayer>("TurnHelper/TileMapLayer");
		if (tileMapLayer != null)
		{
			if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
			{
				Vector2 mousePosition = mouseEvent.Position;
				Vector2I tilePosition = tileMapLayer.LocalToMap(mousePosition);
				GD.Print($"Tile clicked at: {tilePosition}");
			}
		}
	}
}