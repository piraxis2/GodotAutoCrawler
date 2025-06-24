using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script.TurnAction.Skill;

public interface ISkill<T> where T : TurnActionBase 
{
    // Skill 사거리
    public int Range { get; }
    // Skill 범위
    public int Scale { get; }
    
    // Skill이 닿는 위치
    public HashSet<Vector2I> AttackRangePositions { get; }
}