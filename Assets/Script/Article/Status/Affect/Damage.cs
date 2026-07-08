using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public abstract class Damage : StatusAffect, IAffectedImmediately
{
    protected enum DamageNegate
    {
        None,
        Blocked,
        Absorbed,
        Reflected,
        Resisted,
    }

    protected Dictionary<DamageNegate, string> DamageNegateMessages = new()
    {
        { DamageNegate.Blocked, "Block" },
        { DamageNegate.Absorbed, "Absorb" },
        { DamageNegate.Reflected, "Reflect" },
        { DamageNegate.Resisted, "Resist" }
    };
    
    // 피해를 준 유닛. 주는 피해 배율(giver 측) hook에 쓰인다.
    protected ArticleStatus Giver;

    public static T CreateDamage<T>(ArticleStatus giver, int minDamage, int maxDamage) where T : Damage, new()
    {
        var damage = new T();
        damage.Giver = giver;
        damage.Init(giver, minDamage, maxDamage);
        return damage;
    }
    
    public override HashSet<Type> AffectedType => new() { typeof(Health) };
    
    // 크리티컬 여부, 기본값은 false
    protected virtual bool IsCritical => false; 
    // giver: 공격하는 캐릭터
    protected abstract void Init(ArticleStatus giver, int minDamage, int maxDamage);
    // recipient: 피해를 받는 캐릭터
    protected abstract int CalculatedDamage(ArticleStatus recipient);
    protected virtual DamageNegate GetDamageNegate(ArticleStatus recipient) { return DamageNegate.None; }
    public void ApplyImmediately<TStatus>(TStatus statusElement, ArticleStatus recipient) where TStatus : StatusElement
    {
        var gType = statusElement.GetType();
        if (!AffectedType.Contains(gType)) return;

        if (statusElement is not Health health) return;


        var damageFloater = BattleFieldScene.BattleField.DamageFloater; 
        var damageNegate = GetDamageNegate(recipient);
        if (damageNegate != DamageNegate.None)
        {
            if (DamageNegateMessages.TryGetValue(damageNegate, out var message))
                damageFloater.Call("display", message, recipient.Owner.GlobalPosition, Color.Color8(255, 0, 0));
            else
                GD.PrintErr($"Damage negate {damageNegate} is not allowed");
            return;
        }
        
        recipient.Owner.Hit();

        // 배율 hook: 주는 유닛(DamageDealt)과 받는 유닛(DamageTaken) 배율을 롤 이후에 곱한다(RNG 스트림 무이동).
        // 상태가 없으면 둘 다 1.0이라 기존 피해 baseline이 그대로 유지된다.
        float multiplier = (Giver?.Owner?.DamageDealtMultiplier ?? 1f) * recipient.Owner.DamageTakenMultiplier;
        int damage = (int)(CalculatedDamage(recipient) * (IsCritical ? 2 : 1) * multiplier);
        damageFloater.Call("damage_display", damage, recipient.Owner.GlobalPosition, IsCritical);
        health.CurrentHealth -= damage;
    }
}