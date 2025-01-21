using System;
using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public partial class Health : StatusElement 
{
    [Signal]
    public delegate void OnHealthChangedEventHandler(int oldHealth, int newHealth);
    [Export] private int MaxHealth { get; set; } = 10;
    private int _currentHealth;
    private ArticleBase _owner;

    public int CurrentHealth
    {
        get => _currentHealth;
        set
        {
            if (_currentHealth == value) return;
            
            int oldHealth = _currentHealth;

            _currentHealth = Math.Clamp(value, 0, MaxHealth);
            EmitSignal("OnHealthChanged", oldHealth, _currentHealth);
            _owner.HealthBar?.Call("_set_health", _currentHealth);
            GD.Print($"name : {_owner.Name} Health Changed : {_currentHealth}");
            
            if (_currentHealth <= 0) _owner.Dead();
        }
    }

    public override void Init(ArticleBase owner)
    {
        _owner = owner;
        _currentHealth = MaxHealth;
        _owner.HealthBar?.Call("init_health", MaxHealth);
    }
}