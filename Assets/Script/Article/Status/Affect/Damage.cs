using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article.Status.Element;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public abstract class Damage : StatusAffect
{
    public static T CreateDamage<T>(ArticleStatus giver, int skillValue) where T : Damage, new()
    {
        var damage = new T();
        damage.Init(giver, skillValue);
        return damage;
    }
    
    public override HashSet<Type> AffectedType => new() { typeof(Health) };
    protected abstract bool IsCritical { get; }
    protected virtual void Init(ArticleStatus giver, int skillValue){}
    protected abstract int CalculatedDamage(ArticleStatus recipient);
    protected override void OnApply<T>(T type, ArticleStatus recipient)
    {
        var gType = type.GetType();
        if (!AffectedType.Contains((gType))) return;

        if (recipient.StatusElementsDictionary[gType] is not Health health) return;
        
        int damage = CalculatedDamage(recipient) * (IsCritical ? 2 : 1);
        var damageFloater = recipient.Owner.GetNode("/root/DamageFloater");
        damageFloater.Call("display", damage, recipient.Owner.GlobalPosition, IsCritical);
        health.CurrentHealth -= damage;
    }
}