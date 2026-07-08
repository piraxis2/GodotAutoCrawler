using System.Threading.Tasks;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Article.Status;
using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public abstract partial class ArticleBase : Node2D
{
    [Export] public ArticleStatus ArticleStatus = new();

    // 씬 트리 순회 순서 기반 스폰 순번. ArticlesContainer가 등록 시 부여하며 턴 순서 동점자 키로 쓰인다(ADR-017).
    public int SpawnIndex { get; set; }

    // 피해 배율 hook. 기본 1.0이라 상태가 없으면 기존 피해 baseline을 보존한다.
    // 주는 피해(giver 측)와 받는 피해(recipient 측) 배율을 각각 CharacterArticle이 StatusController로 오버라이드한다.
    public virtual float DamageDealtMultiplier => 1f;
    public virtual float DamageTakenMultiplier => 1f;

    public bool IsAlive
    {
        get
        {
            if (ArticleStatus.StatusElementsDictionary[typeof(Health)] is Health health)
            {
                return health.CurrentHealth > 0;
            }

            return true;
        }
    }

    private AnimationPlayer _animationPlayer;
    private AnimatedSprite2D _animatedSprite2D;
    public AnimationPlayer AnimationPlayer => _animationPlayer;
    public ProgressBar HealthBar;

    [Signal]
    public delegate void OnMoveEventHandler(Vector2I from, Vector2I to, ArticleBase article);

    [Signal]
    public delegate void OnDeadEventHandler(ArticleBase deadArticle);


    private Vector2I _tilePosition;
    private bool _isUnInitialized = true;

    public Vector2I TilePosition
    {
        get => _tilePosition;
        set
        {
            if (!_isUnInitialized && _tilePosition == value) return;

            Vector2I oldPosition = _tilePosition;
            _tilePosition = value;
            _isUnInitialized = false;
            EmitSignal("OnMove", oldPosition, value, this);
        }
    }

    public sealed override void _Ready()
    {
        _animatedSprite2D = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
        _animationPlayer = GetNode<AnimationPlayer>("AnimatedSprite2D/AnimationPlayer");
        HealthBar = GetNode<ProgressBar>("HealthBar");
        ArticleStatus.InitStatus(this);
        AnimationPlayer.Connect("animation_finished", new Callable(this, nameof(OnAnimationFinished)));
        AnimationPlayer.Play("Idle");
    }

    public bool IsOpponent(ArticleBase article)
    {
        if (article is not { IsAlive: true }) return false;

        // if (this is not ITurnAffectedArticle<ArticleBase> || article is not ITurnAffectedArticle<ArticleBase>) return false;

        return article.GetParent().Name != "Neutral" && article.GetParent().Name != GetParent().Name;
    }


    private void OnAnimationFinished(string animName)
    {
        if (animName == "Dead")
        {
            QueueFree();
        }
    }

    public void Hit()
    {
        _animatedSprite2D.Call("hit");
    }

    public void Dead()
    {
        if (AnimationPlayer.HasAnimation("Dead"))
        {
            AnimationPlayer.Play("Dead");
        }
        else
        {
            QueueFree();
        }

        EmitSignal("OnDead", this);
    }

    public void DecisionFlipH(Vector2I target)
    {
        // Flip based on X-coordinate difference, if X is same, then Y-coordinate difference
        _animatedSprite2D.FlipH = target.X < TilePosition.X || (target.X == TilePosition.X && target.Y > TilePosition.Y);
    }
}