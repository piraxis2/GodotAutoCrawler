using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script;

public partial class FxPlayer : Node2D
{
    [Export] private Godot.Collections.Dictionary<StringName, PackedScene> _spriteFxScenes = new();
    [Export] private Godot.Collections.Dictionary<StringName, PackedScene> _lineFxScenes = new();
    [Export] private PackedScene _soundFxScene;

    private List<AnimationPlayer> _playingSoundFxs = [];


    public void Tick(double delta)
    {
        foreach (var elem in _playingSoundFxs.ToList())
        {
            elem.Seek(elem.CurrentAnimationPosition + delta);
            if (elem.CurrentAnimation == "play" && elem.CurrentAnimationPosition < elem.CurrentAnimationLength)
            {
                continue;
            }
            
            _playingSoundFxs.Remove(elem);
            elem.QueueFree();
        }
    }

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

    public void PlaySoundFx(StringName fxName)
    {
        var soundFx = _soundFxScene.Instantiate<Node>();
        AddChild(soundFx);
        _playingSoundFxs.Add(soundFx.GetNode<AnimationPlayer>("AnimationPlayer"));
        soundFx.Call("playSound", fxName);
    }

}