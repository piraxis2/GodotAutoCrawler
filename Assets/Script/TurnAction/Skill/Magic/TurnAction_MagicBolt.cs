using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Skill.Magic;

[GlobalClass, Tool]
public partial class TurnAction_MagicBolt : TurnActionBase, ISkill<TurnActionBase>
{
    [Export] private int _maxDamage = 20;
    public int Range => 3;
    public int Scale => 3;

    [Export] private int _exportCost = 2;
    protected override int MasterCost => _exportCost; 

    private HashSet<Vector2I> _attackRangePositions;
    public HashSet<Vector2I> AttackRangePositions => _attackRangePositions ??= SkillUtil.GetAttackRangePositions(Range);

    private int _chainCount = 3;

    protected override void OnInit(Node owner)
    {
        _chainCount = 3;
    }

    private void Shot(ArticleBase owner)
    {
        List<Vector2I> calculatedAttackRange = AttackRangePositions.Select(p => p + owner.TilePosition).ToList();
        BattleFieldTileMapLayer tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(owner);
        ArticleBase target = tileMapLayer?.GetArticles(calculatedAttackRange)?.FirstOrDefault(t => t is { IsAlive: true } && t.IsOpponent(owner));
        owner.AnimationPlayer.Play("Idle");
        
        if (target is { IsAlive: true })
        {
            var spriteFx = GlobalUtil.GetBattleFieldCoreNode<SpriteFx>(owner);
            spriteFx.PlayFx("IceBolt", target.GlobalPosition);
            target.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<MagicalDamage>(owner.ArticleStatus, _maxDamage, _maxDamage));
        }
    }

    protected override ActionState ActionExecute(double delta, ArticleBase owner)
    {
        if (owner.AnimationPlayer.CurrentAnimation is "Cast" or "Casting")
        {
            if (owner.AnimationPlayer.CurrentAnimationPosition < owner.AnimationPlayer.CurrentAnimationLength)
            {
                owner.AnimationPlayer.Seek(owner.AnimationPlayer.CurrentAnimationPosition + delta);
                
                // If the animation is still playing, return Running state
                if (owner.AnimationPlayer.CurrentAnimationPosition + delta < owner.AnimationPlayer.CurrentAnimationLength) return ActionState.Running;
            }

            if (Cost <= 1) Shot(owner);
            
            return ActionState.Executed;
        }

        owner.AnimationPlayer.Play(Cost == MasterCost ? "Cast" : "Casting", -1, 0);
        return ActionState.Running;
    }
}