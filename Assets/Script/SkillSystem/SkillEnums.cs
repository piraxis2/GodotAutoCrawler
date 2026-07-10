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

// 스킬 ammo(고정 보장 사용 횟수)의 충전 수명주기. ADR-021.
public enum SkillAmmoResetScope
{
    // ammo counter를 만들지 않는다(무제한). 기본기용이자 기존 .tres 기본값.
    Unlimited = 0,
    // 전투 시작마다 Ammo로 충전한다. 전투 사이 이월 없음.
    Battle = 1,
    // 예약값: 등반/출전 단위 충전 + 이월. lifecycle 도입 전까지 배선하지 않으며 런타임은 Battle로 취급한다.
    Expedition = 2,
}