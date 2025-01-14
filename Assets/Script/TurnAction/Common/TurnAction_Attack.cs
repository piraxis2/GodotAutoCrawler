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
    
    
    private HashSet<Vector2I> _attackRangePositions;
    public HashSet<Vector2I> AttackRangePositions
    {
        get
        {
            if (_attackRangePositions == null)
            {
                HashSet<Vector2I> GetAdjacentTiles(Vector2I position)
                {
                    return new HashSet<Vector2I>
                    {
                        position + Vector2I.Right, // 동
                        position + Vector2I.Left, // 서
                        position + Vector2I.Down, // 남
                        position + Vector2I.Up, // 북
                    };
                }

                HashSet<Vector2I> strikingArea = new(GetAdjacentTiles(Vector2I.Zero));
                HashSet<Vector2I> completedArea = new() { Vector2I.Zero };

                for (int i = 1; i < Distance; i++)
                {
                    foreach (var area in strikingArea.ToList().Where(area => completedArea.Add(area)))
                    {
                        strikingArea.UnionWith(GetAdjacentTiles(area));
                    }
                }
                _attackRangePositions = strikingArea;
            }
            return _attackRangePositions;
        }
    }
    protected override void OnInit(Node owner)
    {
    }

    protected override ActionState ActionExecute(double delta, ArticleBase owner)
    {
        List<Vector2I> calculatedAttackRange = AttackRangePositions.Select(p => p + owner.TilePosition).ToList();
        var tileMapLayer = GlobalUtil.GetBattleField(owner)?.GetBattleFieldCoreNode<BattleFieldTileMapLayer>();
        List<ArticleBase> targetList = tileMapLayer?.GetArticles(calculatedAttackRange);
        targetList?.FirstOrDefault(target => target.IsOpponent(owner))?.ArticleStatus?.ApplyAffectStatus(Damage.CreateDamage<PhysicalDamage>(owner.ArticleStatus, 10));
        return ActionState.Executed;
    }
}