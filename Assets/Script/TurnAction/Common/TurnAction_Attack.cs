using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Common;
[GlobalClass, Tool]
public partial class TurnAction_Attack : TurnActionBase, ISkill<TurnActionBase>
{
    public int Distance { get; } = 1;
    public int Range { get; } = 1;

    private bool _isAnimationRunning;

    private HashSet<Vector2I> _attackRangePositions;
    public HashSet<Vector2I> AttackRangePositions
    {
        get
        {
            if (_attackRangePositions == null)
            {
                _attackRangePositions = new HashSet<Vector2I> { Vector2I.Zero };
                for (int i = 0; i < Distance; i++)
                {
                    _attackRangePositions.UnionWith(_attackRangePositions.SelectMany(p => new[]
                    {
                        p + Vector2I.Right,
                        p + Vector2I.Left,
                        p + Vector2I.Down,
                        p + Vector2I.Up
                    }));
                }
            }
            return _attackRangePositions;
        }
    }

    protected override void OnInit(Node owner) => _isAnimationRunning = false;

    protected override ActionState ActionExecute(double delta, ArticleBase owner)
    {
        if (owner.AnimationPlayer.CurrentAnimation == "Attack" && owner.AnimationPlayer.CurrentAnimationPosition < owner.AnimationPlayer.CurrentAnimationLength)
        {
            owner.AnimationPlayer.Seek(owner.AnimationPlayer.CurrentAnimationPosition + delta);
            return ActionState.Running;
        }

        if (_isAnimationRunning)
        {
            List<Vector2I> calculatedAttackRange = AttackRangePositions.Select(p => p + owner.TilePosition).ToList();
            BattleFieldTileMapLayer tileMapLayer = GlobalUtil.GetBattleField(owner)?.GetBattleFieldCoreNode<BattleFieldTileMapLayer>();
            ArticleBase target = tileMapLayer?.GetArticles(calculatedAttackRange)?.FirstOrDefault(t => t is { IsAlive: true } && t.IsOpponent(owner));
            owner.AnimationPlayer.Play("Idle");
            if (target is { IsAlive: true })
            {
                target.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<PhysicalDamage>(owner.ArticleStatus, 10));
            }
            return ActionState.Executed;
        }

        owner.AnimationPlayer.Play("Attack", -1, 0);
        _isAnimationRunning = true;
        return ActionState.Running;
    }
}