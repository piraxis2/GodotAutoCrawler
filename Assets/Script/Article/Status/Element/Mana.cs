using System;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public partial class Mana : StatusElement
{
    [Signal]
    public delegate void OnManaChangedEventHandler(int oldMana, int newMana);
    [Export] public int MaxMana { get; set; } = 10;
    private int _currentMana;
    public int CurrentMana
    {
        get => _currentMana;
        set
        {
            int clamped = Math.Clamp(value, 0, MaxMana);
            if (_currentMana == clamped) return;

            int oldMana = _currentMana;
            _currentMana = clamped;
            EmitSignal("OnManaChanged", oldMana, _currentMana);
        }
    }

    public bool CanAfford(int cost) => _currentMana >= cost;

    // 지불 가능하면 마나를 소모하고 true, 부족하면 상태를 바꾸지 않고 false.
    public bool TrySpend(int cost)
    {
        if (_currentMana < cost) return false;
        CurrentMana = _currentMana - cost;
        return true;
    }

    protected override void OnInit(ArticleBase owner)
    {
        _currentMana = MaxMana;
    }
}
