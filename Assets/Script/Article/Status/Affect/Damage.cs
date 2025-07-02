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
    
    public static T CreateDamage<T>(ArticleStatus giver, int minDamage, int maxDamage) where T : Damage, new()
    {
        var damage = new T();
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

        var damageFloater = recipient.Owner.GetNode("/root/DamageFloater");
        var damageNegate = GetDamageNegate(recipient);
        if (damageNegate != DamageNegate.None)
        {
            if (DamageNegateMessages.TryGetValue(damageNegate, out var message))
                damageFloater.Call("display", message, recipient.Owner.GlobalPosition, Color.Color8(255, 0, 0));
            else
                GD.PrintErr($"Damage negate {damageNegate} is not allowed");
            return;
        }
        int damage = CalculatedDamage(recipient) * (IsCritical ? 2 : 1);
        damageFloater.Call("damage_display", damage, recipient.Owner.GlobalPosition, IsCritical);
        health.CurrentHealth -= damage;
    }
}