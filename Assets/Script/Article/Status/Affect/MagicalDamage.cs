using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public partial class MagicalDamage : Damage
{
    protected override bool IsCritical => false; // 마법 데미지는 일반적으로 크리티컬이 없다
    private int _intelligence = 1; // 마법 데미지에 영향을 주는 지능 값

    private int _damage = 0;

    protected override void Init(ArticleStatus giver, int minDamage, int maxDamage)
    {
        _damage = maxDamage;
        _intelligence = (giver.StatusElementsDictionary.GetValueOrDefault(typeof(Intelligence)) as Intelligence)?.Value ?? _intelligence;
    }

    protected override int CalculatedDamage(ArticleStatus recipient)
    {
        //todo : 임시 계산식
        int defenseValue = (recipient.StatusElementsDictionary[typeof(Defense)] as Defense)?.Value ?? 0;
        return (_damage - defenseValue) / 3 + _intelligence + 25; 
    }
}