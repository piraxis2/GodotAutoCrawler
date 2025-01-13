using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public class PhysicalDamage : Damage
{
    private int _damage;
    private int _strength = 1;
    private bool _isCritical;

    public PhysicalDamage() {}

    protected override bool IsCritical => _isCritical;

    protected override void Init(ArticleStatus giver, int skillValue)
    {
        _damage = skillValue;
        _strength = (giver.StatusElementsDictionary[typeof(Strength)] as Strength)?.Value ?? _strength;
        int luckValue = (giver.StatusElementsDictionary[typeof(Luck)] as Luck)?.Value ?? 0;
        // 5% + 0.1% per luck
        _isCritical = GD.RandRange(0, (double)100) < ((double)luckValue / 10) + 5;
    }

    protected override int CalculatedDamage(ArticleStatus recipient)
    {
        int damage = _damage - ((recipient.StatusElementsDictionary[typeof(Defense)] as Defense)?.Value ?? 0);
        return damage / (2 + _strength + 25);
    }
}