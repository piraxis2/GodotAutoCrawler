using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script;

public partial class SpriteFx : Node
{
    [Export]
    private Dictionary<StringName, PackedScene> _fxScenes = new();
    
    public void PlayFx(StringName fxName, Vector2 position)
    {
        if (_fxScenes.TryGetValue(fxName, out var fxScene))
        {
            var fxInstance = fxScene.Instantiate<Node2D>();
            fxInstance.Position = position;
            GetTree().Root.AddChild(fxInstance);
            if (fxInstance.HasMethod("playanimation"))
                fxInstance.Call("playanimation",fxName); 
            else
                GD.PrintErr($"FX scene '{fxName}' does not have a 'play' method.");
        }
        else
        {
            GD.PrintErr($"FX scene '{fxName}' not found.");
        }
    }
}