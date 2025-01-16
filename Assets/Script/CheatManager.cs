using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class CheatManager : Node
{
	public override void _Input(InputEvent @event)
	{
		// 현재 씬 가져오기

		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
		{
			BattleFieldScene battleFieldScene = GlobalUtil.GetBattleField(this);
			var tileMapLayer = battleFieldScene?.GetBattleFieldCoreNode<BattleFieldTileMapLayer>();
			if (tileMapLayer != null)
			{
				Vector2 mousePosition = mouseEvent.Position;
				Vector2I tilePosition = tileMapLayer.LocalToMap(tileMapLayer.ToLocal(mousePosition));
				GD.Print($"Tile clicked at: {tilePosition}");
			}
		}

		if (@event is InputEventKey { Keycode: Key.Quoteleft, Pressed: true }) 
		{
			// 콘솔 열기
			// Console.Open();
			GD.Print("hi");
		}
	}
}