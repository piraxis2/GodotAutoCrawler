using System.Threading.Tasks;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class CheatManager : Node
{
	public override void _Input(InputEvent @event)
	{
		// 현재 씬 가져오기

		
		if (@event is InputEventMouseButton { Pressed: true } mouseEvent)
		{
			var battleField = BattleFieldScene.BattleField;
			if (battleField == null)
				return;

			var tileMapLayer = battleField.BattleFieldTileMap;
			if (tileMapLayer != null)
			{
				Vector2 mousePosition = mouseEvent.Position;
				Vector2I tilePosition = tileMapLayer.LocalToMap(tileMapLayer.ToLocal(mousePosition));
				GD.Print($"Tile clicked at: {tilePosition}");
			}
		}
	}
}