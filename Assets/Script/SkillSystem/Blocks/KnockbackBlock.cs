using System;
using AutoCrawler.Assets.Script;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.TurnAction;
using Godot;

namespace AutoCrawler.Assets.Script.SkillSystem.Blocks;

// 대상을 시전자 반대 방향으로 최대 Distance 칸 밀어낸다. 맵 경계/점유 칸에서 멈춘다.
// 최종 위치를 TilePosition으로 한 번만 설정해 OnMove -> _placedArticles -> (다음)AStar 갱신을 태운다.
[GlobalClass, Tool]
public partial class KnockbackBlock : EffectBlock
{
    [Export] public int Distance { get; set; } = 1;

    public override void EnqueuePhases(SkillContext context, SkillState state)
    {
        state.EnqueuePhase((delta, owner) =>
        {
            var tileMap = context.TileMapLayer;
            var caster = context.Caster;
            if (tileMap != null && caster != null)
            {
                foreach (var target in state.ConfirmedTargets)
                {
                    if (target is { IsAlive: true }) KnockOne(tileMap, caster, target, state);
                }
            }

            state.PhaseQueue.Dequeue();
            return ActionState.Executed;
        });
    }

    private void KnockOne(BattleFieldTileMapLayer tileMap, ArticleBase caster, ArticleBase target, SkillState state)
    {
        Vector2I dir = KnockbackDirection(caster.TilePosition, target.TilePosition);
        if (dir == Vector2I.Zero)
        {
            state.Reports.Add($"knockback:0:{target.GetPath()}");
            return;
        }

        Rect2I bounds = tileMap.GetUsedRect();
        Vector2I pos = target.TilePosition;
        int moved = 0;
        for (int i = 0; i < Distance; i++)
        {
            Vector2I next = pos + dir;
            if (!bounds.HasPoint(next)) break;            // 맵 경계에서 중단
            if (tileMap.GetArticle(next) != null) break;  // 점유 칸 충돌로 중단
            pos = next;
            moved++;
        }

        if (moved == 0)
        {
            state.Reports.Add($"knockback:0:{target.GetPath()}");
            return;
        }

        // 시각 위치를 맞추고, TilePosition 설정이 OnMove를 발행해 tilemap 점유를 갱신한다.
        target.GlobalPosition = tileMap.ToGlobal(tileMap.MapToLocal(pos));
        target.TilePosition = pos;
        state.Reports.Add($"knockback:{moved}:{target.GetPath()}");
    }

    // 시전자로부터 멀어지는 4방향 단위 벡터. 대각은 더 큰 축(동률이면 X)을 택한다.
    private static Vector2I KnockbackDirection(Vector2I from, Vector2I to)
    {
        Vector2I d = to - from;
        if (d == Vector2I.Zero) return Vector2I.Zero;
        return Mathf.Abs(d.X) >= Mathf.Abs(d.Y)
            ? new Vector2I(Math.Sign(d.X), 0)
            : new Vector2I(0, Math.Sign(d.Y));
    }
}
