using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Skill;

public static class SkillUtil
{
    public static HashSet<Vector2I> GetAttackRangePositions(int distance)
    {
        HashSet<Vector2I> positions = [Vector2I.Zero];
        for (int i = 0; i < distance; i++)
        {
            positions.UnionWith(positions.SelectMany(p => new[]
            {
                p + Vector2I.Right,
                p + Vector2I.Left,
                p + Vector2I.Down,
                p + Vector2I.Up
            }).ToList());
        }

        return positions;
    }
}