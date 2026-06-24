using Godot;
using System;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Util;

public partial class InputHelper : Node
{
    
    public override void _Input(InputEvent @event)
    {
        // 현재 씬 가져오기
        if (@event is InputEventMouseButton { Pressed: true } mouseEvent)
        {
            var tileMapLayer = BattleFieldScene.BattleField.BattleFieldTileMap; 
            if (tileMapLayer != null)
            {
                Vector2 mousePosition = mouseEvent.GlobalPosition;
                // tileMapLayer.
                Vector2I tilePosition = tileMapLayer.LocalToMap(tileMapLayer.ToLocal(tileMapLayer.ToGlobal(mousePosition)));

                if (tileMapLayer.GetUsedRect().HasPoint(tilePosition))
                {
                    MouseTileClicked(tilePosition);
                }
            }
        }
    }

    private void MouseTileClicked(Vector2I tilePosition)
    {
        
    }
    
    
    
}
