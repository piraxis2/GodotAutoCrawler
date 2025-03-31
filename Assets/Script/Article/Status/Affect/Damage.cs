using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public abstract class Damage : StatusAffect, IAffectedImmediately
{
    public static T CreateDamage<T>(ArticleStatus giver, int minDamage, int maxDamage) where T : Damage, new()
    {
        var damage = new T();
        damage.Init(giver, minDamage, maxDamage);
        return damage;
    }
    
    public override HashSet<Type> AffectedType => new() { typeof(Health) };
    protected abstract bool IsCritical { get; }
    protected abstract void Init(ArticleStatus giver, int minDamage, int maxDamage);
    protected abstract int CalculatedDamage(ArticleStatus recipient);
    public void ApplyImmediately<TStatus>(TStatus statusElement, ArticleStatus recipient) where TStatus : StatusElement
    {
        var gType = statusElement.GetType();
        if (!AffectedType.Contains((gType))) return;

        if (statusElement is not Health health) return;
        
        int damage = CalculatedDamage(recipient) * (IsCritical ? 2 : 1);
        var damageFloater = recipient.Owner.GetNode("/root/DamageFloater");
        damageFloater.Call("display", damage, recipient.Owner.GlobalPosition, IsCritical);
        health.CurrentHealth -= damage;
    }
}