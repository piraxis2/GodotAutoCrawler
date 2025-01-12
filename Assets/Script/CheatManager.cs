using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class CheatManager : Node
{
	public override void _Input(InputEvent @event)
	{
		// 현재 씬 가져오기
		BattleFieldScene battleFieldScene = GlobalUtil.GetBattleField(this);
		var tileMapLayer = battleFieldScene?.GetBattleFieldCoreNode<BattleFieldTileMapLayer>();
		if (tileMapLayer != null)
		{
			if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
			{
				Vector2 mousePosition = mouseEvent.Position;
				Vector2I tilePosition = tileMapLayer.LocalToMap(mousePosition);
				GD.Print($"Tile clicked at: {tilePosition}, {mousePosition}");
			}
		}
	}
}