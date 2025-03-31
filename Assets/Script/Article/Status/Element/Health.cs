using System;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public partial class Health : StatusElement 
{
    [Signal]
    public delegate void OnHealthChangedEventHandler(int oldHealth, int newHealth);
    [Export] public int MaxHealth { get; set; } = 10;
    private int _currentHealth;
    public int CurrentHealth
    {
        get => _currentHealth;
        set
        {
            if (_currentHealth == value) return;
            
            int oldHealth = _currentHealth;

            if (value <= 0) Owner.Dead();
            _currentHealth = Math.Clamp(value, 0, MaxHealth);
            EmitSignal("OnHealthChanged", oldHealth, _currentHealth);
            Owner.HealthBar?.Call("_set_health", _currentHealth);
        }
    }

    protected override void OnInit(ArticleBase owner)
    {
        _currentHealth = MaxHealth;
        Owner.HealthBar?.Call("init_health", MaxHealth);
    }
}