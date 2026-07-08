using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.TurnAction;
using AutoCrawler.Assets.Script.TurnAction.Skill;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem.Blocks;

// TurnAction_ChainLightning의 연쇄 대상 선택/피해를 데이터 블록으로 이관한다.
// 첫 대상은 SkillDefinition이 확정한 대상(ConfirmedTargets[0])이며, 이후 각 홉마다
// 현재 대상 위치 기준 사거리 안에서 "미타격 우선 -> ADR-017 정규 순서"로 다음 대상을 고른다.
// 피해는 legacy와 동일하게 고정 MagicalDamage(MaxDamage). 연쇄 피해는 RNG를 소비하지 않는다.
[GlobalClass, Tool]
public partial class ChainBlock : EffectBlock
{
    [Export] public int ChainCount { get; set; } = 3;
    [Export] public int Range { get; set; } = 3;
    [Export] public int MaxDamage { get; set; } = 20;

    public override void EnqueuePhases(SkillContext context, SkillState state)
    {
        var rangePositions = SkillUtil.GetAttackRangePositions(Range);

        state.EnqueuePhase((delta, owner) =>
        {
            var tileMap = context.TileMapLayer;
            var caster = context.Caster;
            if (tileMap != null && caster != null)
            {
                var hit = new HashSet<ArticleBase> { caster };
                ArticleBase current = state.ConfirmedTargets.FirstOrDefault(t => t is { IsAlive: true });

                for (int i = 0; i < ChainCount && current is { IsAlive: true }; i++)
                {
                    hit.Add(current);
                    current.ArticleStatus?.ApplyAffectStatus(
                        Damage.CreateDamage<MagicalDamage>(owner.ArticleStatus, MaxDamage, MaxDamage));
                    state.Reports.Add($"chain:{i}:{current.GetPath()}");
                    current = GetChainTarget(tileMap, caster, current.TilePosition, hit, rangePositions);
                }
            }

            state.PhaseQueue.Dequeue();
            return ActionState.Executed;
        });
    }

    // legacy TurnAction_ChainLightning.GetChainTarget과 동일: 사거리 내 살아있는 적 중
    // 아직 맞지 않은 대상 우선, 동점자는 ADR-017 정규 순서(거리 -> Y -> X).
    private static ArticleBase GetChainTarget(BattleFieldTileMapLayer tileMap, ArticleBase caster,
        Vector2I tilePosition, HashSet<ArticleBase> hit, IReadOnlyList<Vector2I> rangePositions)
    {
        List<Vector2I> calculatedRange = rangePositions.Select(p => p + tilePosition).ToList();
        var potential = tileMap?.GetArticles(calculatedRange)?
            .Where(t => t is { IsAlive: true } && t.TilePosition != tilePosition && t.IsOpponent(caster));

        return potential?
            .OrderBy(t => hit.Contains(t))
            .ThenByCanonical(t => t.TilePosition, tilePosition)
            .FirstOrDefault();
    }
}
