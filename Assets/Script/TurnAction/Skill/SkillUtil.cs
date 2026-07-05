using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Skill;

public static class SkillUtil
{
    public static int ManhattanDistance(Vector2I a, Vector2I b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    // ADR-017 정규 순서: 기준점과의 Manhattan 거리 -> 타일 Y -> 타일 X
    public static IOrderedEnumerable<T> OrderByCanonical<T>(this IEnumerable<T> source, Func<T, Vector2I> position, Vector2I origin)
    {
        return source
            .OrderBy(item => ManhattanDistance(position(item), origin))
            .ThenBy(item => position(item).Y)
            .ThenBy(item => position(item).X);
    }

    public static IOrderedEnumerable<T> ThenByCanonical<T>(this IOrderedEnumerable<T> source, Func<T, Vector2I> position, Vector2I origin)
    {
        return source
            .ThenBy(item => ManhattanDistance(position(item), origin))
            .ThenBy(item => position(item).Y)
            .ThenBy(item => position(item).X);
    }

    public static IReadOnlyList<Vector2I> GetAttackRangePositions(int distance)
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

        return positions.OrderByCanonical(p => p, Vector2I.Zero).ToList();
    }

    // 경로 후보 선택: 경로 길이 -> 목적지 거리 제곱 -> 목적지 Y -> 목적지 X (ADR-017)
    public static Godot.Collections.Array<Vector2I> SelectCanonicalPath(IEnumerable<Godot.Collections.Array<Vector2I>> candidatePaths, Vector2I origin)
    {
        return candidatePaths
            .Where(candidatePath => candidatePath.Count >= 2)
            .OrderBy(candidatePath => candidatePath.Count)
            .ThenBy(candidatePath => (candidatePath[candidatePath.Count - 1] - origin).LengthSquared())
            .ThenBy(candidatePath => candidatePath[candidatePath.Count - 1].Y)
            .ThenBy(candidatePath => candidatePath[candidatePath.Count - 1].X)
            .FirstOrDefault();
    }
}
