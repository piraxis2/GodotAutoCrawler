using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Skill.Magic;

[GlobalClass, Tool]
public partial class TurnAction_FireBolt : TurnActionBase, ISkill<TurnActionBase>
{
    [Export] private int minDamage = 10;
    [Export] private int maxDamage = 20;
    public int Range => 3;
    public int Scale => 3;

    protected override int MasterCost => 2; 

    private HashSet<Vector2I> _attackRangePositions;
    public HashSet<Vector2I> AttackRangePositions => _attackRangePositions ??= SkillUtil.GetAttackRangePositions(Range);

    private int _chainCount = 3;

    protected override void OnInit(Node owner)
    {
        _chainCount = 3;
    }

    private ActionState Shot(double delta, ArticleBase owner)
    {
        List<Vector2I> calculatedAttackRange = AttackRangePositions.Select(p => p + owner.TilePosition).ToList();
        BattleFieldTileMapLayer tileMapLayer = GlobalUtil.GetBattleFieldCoreNode<BattleFieldTileMapLayer>(owner);
        ArticleBase target = tileMapLayer?.GetArticles(calculatedAttackRange)?.FirstOrDefault(t => t is { IsAlive: true } && t.IsOpponent(owner));
        owner.AnimationPlayer.Play("Idle");
        if (target is { IsAlive: true })
        {
            target.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<PhysicalDamage>(owner.ArticleStatus, minDamage, maxDamage));
        }

        return ActionState.Executed;
    }

    protected override ActionState ActionExecute(double delta, ArticleBase owner)
    {
        if (owner.AnimationPlayer.CurrentAnimation == "Cast")
        {
            if (owner.AnimationPlayer.CurrentAnimationPosition < owner.AnimationPlayer.CurrentAnimationLength)
            {
                owner.AnimationPlayer.Seek(owner.AnimationPlayer.CurrentAnimationPosition + delta);
                
                // If the animation is still playing, return Running state
                if (owner.AnimationPlayer.CurrentAnimationPosition + delta < owner.AnimationPlayer.CurrentAnimationLength) return ActionState.Running;
            }

            if (Cost > 1) return ActionState.Executed;

            return Shot(delta, owner);
        }


        owner.AnimationPlayer.Play("Cast", -1, 0);
        return ActionState.Running;
    }
}