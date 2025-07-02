using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Skill.Magic;

public abstract partial class TurnAction_Cast : TurnActionBase
{
    
    private bool _isAnimationRunning = false;

    protected abstract ActionState Shot(double delta, ArticleBase owner);
    
    private ActionState Cast(double delta, ArticleBase owner)
    {
        if (Cost <= 1) return Shot(delta, owner);
        
        if (owner.AnimationPlayer.CurrentAnimation is "Cast" or "Casting")
        {
            if (owner.AnimationPlayer.CurrentAnimationPosition < owner.AnimationPlayer.CurrentAnimationLength)
            {
                owner.AnimationPlayer.Seek(owner.AnimationPlayer.CurrentAnimationPosition + delta);
                
                // If the animation is still playing, return Running state
                if (owner.AnimationPlayer.CurrentAnimationPosition + delta < owner.AnimationPlayer.CurrentAnimationLength) return ActionState.Running;
            }

            return ActionState.Executed;
        }

        _isAnimationRunning = false;
        owner.AnimationPlayer.Play(Cost == MasterCost ? "Cast" : "Casting", -1, 0);
        return ActionState.Running;
    }



    protected override ActionState ActionExecute(double delta, ArticleBase owner)
    {
        return Cast(delta, owner);
    }
}
