using System;

namespace AutoCrawler.Assets.Script.Article.Status;

// 턴 단위 제어/버프 상태를 유닛별로 보유한다. 지속시간은 턴 카운터이며 frame 반복에 종속되지 않는다.
// 기존 StatusAffect(Damage 지향)와 분리한 이유: 1턴 제어는 apply/expire가 같은 tick에 붕괴한다.
public sealed class StatusController
{
    public int StunTurns { get; private set; }
    public int BindTurns { get; private set; }
    // 이번 턴에 이동이 묶였는지. OnTurnStart에서 확정하고, 이동 노드가 소비한다.
    public bool BoundThisTurn { get; private set; }

    public float DamageDealtMultiplier { get; private set; } = 1f;
    private int _dealtBuffTurns;
    public float DamageTakenMultiplier { get; private set; } = 1f;
    private int _takenBuffTurns;

    // 재진입은 더 긴 지속시간으로 갱신한다(가산 스택 아님).
    public void ApplyStun(int turns) => StunTurns = Math.Max(StunTurns, Math.Max(turns, 0));
    public void ApplyBind(int turns) => BindTurns = Math.Max(BindTurns, Math.Max(turns, 0));

    public void ApplyDamageDealtBuff(float multiplier, int turns)
    {
        // 지속시간 0/음수는 OnTurnStart 만료 분기를 못 타 영구 배율이 되므로 무시한다(잘못된 .tres 방어).
        if (turns <= 0) return;
        DamageDealtMultiplier = multiplier;
        _dealtBuffTurns = turns;
    }

    public void ApplyDamageTakenDebuff(float multiplier, int turns)
    {
        if (turns <= 0) return;
        DamageTakenMultiplier = multiplier;
        _takenBuffTurns = turns;
    }

    // 스턴 소비: 스턴 중이면 한 턴 소모하고 true(=이번 턴 스킵). TurnPlay가 턴당 1회 호출한다.
    public bool ConsumeStunForTurn()
    {
        if (StunTurns <= 0) return false;
        StunTurns--;
        return true;
    }

    // 유닛 턴 시작 틱: bind/buff 지속시간을 갱신한다. stun은 TurnPlay에서 소비한다.
    public void OnTurnStart()
    {
        BoundThisTurn = BindTurns > 0;
        if (BindTurns > 0) BindTurns--;

        if (_dealtBuffTurns > 0)
        {
            _dealtBuffTurns--;
            if (_dealtBuffTurns == 0) DamageDealtMultiplier = 1f;
        }

        if (_takenBuffTurns > 0)
        {
            _takenBuffTurns--;
            if (_takenBuffTurns == 0) DamageTakenMultiplier = 1f;
        }
    }
}
