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
            int oldHealth = _currentHealth;
            _currentHealth = value;
            EmitSignal("OnHealthChanged", oldHealth, value);
        }
    }

    public override void Init(ArticleBase owner)
    {
        _owner = owner;
        CurrentHealth = MaxHealth;
    }
}