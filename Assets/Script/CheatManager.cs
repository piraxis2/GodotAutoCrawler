using System.Threading.Tasks;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class CheatManager : Node
{
	private Node _consoleWindow = null;
	public override void _Input(InputEvent @event)
	{
		// 현재 씬 가져오기

		
		if (@event is InputEventMouseButton { Pressed: true } mouseEvent)
		{
			var tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(this);
			if (tileMapLayer != null)
			{
				Vector2 mousePosition = mouseEvent.Position;
				Vector2I tilePosition = tileMapLayer.LocalToMap(tileMapLayer.ToLocal(mousePosition));
				GD.Print($"Tile clicked at: {tilePosition}");
			}
		}

		// if (@event is InputEventKey { Keycode: Key.Quoteleft, Pressed: true })
		// {
		// 	if (_consoleWindow == null)
		// 	{
		// 		_consoleWindow = GD.Load<PackedScene>("res://addons/devconsole/UI/consoleWindow.tscn").Instantiate();
		// 		GetTree().Root.AddChild(_consoleWindow);
		// 	}
		// }
	}
}