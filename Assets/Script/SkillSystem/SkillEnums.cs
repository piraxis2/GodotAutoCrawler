namespace AutoCrawler.Assets.Script.SkillSystem;

public enum SkillTargetSide
{
    Enemy = 0,
    Ally = 1,
    Self = 2,
}

public enum SkillTargetSelector
{
    Nearest = 0,
    LowestHp = 1,
    HighestHp = 2,
    Self = 3,
}

public enum SkillDamageType
{
    Physical = 0,
    Magical = 1,
}

public enum SelfBuffKind
{
    // 주는 피해 배율(공격 버프)
    DamageDealt = 0,
    // 받는 피해 배율(방어 버프/디버프). 예: 금강체 0.5
    DamageTaken = 1,
}