using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public class PhysicalDamage : Damage
{
    private int _minDamage;
    private int _maxDamage;
    private int _strength = 1;
    private bool _isCritical;

    public PhysicalDamage() {}

    protected override bool IsCritical => _isCritical;

    protected override void Init(ArticleStatus giver, int minDamage, int maxDamage)
    {
        _minDamage = minDamage;
        _maxDamage = maxDamage;
        _strength = (giver.StatusElementsDictionary[typeof(Strength)] as Strength)?.Value ?? _strength;
        int luckValue = (giver.StatusElementsDictionary[typeof(Luck)] as Luck)?.Value ?? 0;
        _isCritical = GD.RandRange(0, (double)100) < (double)luckValue / 10 + 5; 
    }

    protected override int CalculatedDamage(ArticleStatus recipient)
    {
        int defenseValue = (recipient.StatusElementsDictionary[typeof(Defense)] as Defense)?.Value ?? 0;
        int calculatedMinDamage = (_minDamage - defenseValue) / 2 + _strength + 25;
        int calculatedMaxDamage = (_maxDamage - defenseValue) / 2 + _strength + 25;
        return GD.RandRange(calculatedMinDamage, calculatedMaxDamage); 
    }
}