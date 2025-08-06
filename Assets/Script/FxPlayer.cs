using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script;

public partial class FxPlayer : Node
{
    [Export] private Godot.Collections.Dictionary<StringName, PackedScene> _spriteFxScenes = new();
    [Export] private Godot.Collections.Dictionary<StringName, PackedScene> _lineFxScenes = new();
    
    public void PlaySpriteFx(StringName fxName, Vector2 position)
    {
        if (_spriteFxScenes.TryGetValue(fxName, out var fxScene))
        {
            var fxInstance = fxScene.Instantiate<Node2D>();
            fxInstance.Position = position;
            AddChild(fxInstance);
            if (fxInstance.HasMethod("playSpriteFx"))
                fxInstance.Call("playSpriteFx",fxName); 
            else
                GD.PrintErr($"FX scene '{fxName}' does not have a 'play' method.");
        }
        else
        {
            GD.PrintErr($"FX scene '{fxName}' not found.");
        }
    }

    public Node2D PlayLineFx(StringName fxName, Array<Vector2> points)
    {
        Node2D fxInstance = null;
        if (_lineFxScenes.TryGetValue(fxName, out var fxScene))
        {
            fxInstance = fxScene.Instantiate<Node2D>();
            
            AddChild(fxInstance);
            if (fxInstance.HasMethod("showLine"))
                fxInstance.Call("showLine", points); 
        }

        return fxInstance;
    }

}