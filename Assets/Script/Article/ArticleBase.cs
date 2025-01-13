using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Article.Status;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public abstract partial class ArticleBase : Node2D
{
    [Export] protected ArticleStatus ArticleStatus { get; set; }
    [Export] private AnimatedSprite2D _animatedSprite2D;
    public AnimatedSprite2D AnimatedSprite2D => _animatedSprite2D;

    [Signal]
    public delegate void OnMoveEventHandler(Vector2I from, Vector2I to, ArticleBase article);

    private Vector2I _tilePosition;
    private bool _isUnInitialized = true;

    public Vector2I TilePosition
    {
        get => _tilePosition;
        set
        {
            if (!_isUnInitialized && _tilePosition == value && this is IFixedArticle<ArticleBase>) return;
            
            Vector2I oldPosition = _tilePosition;
            _tilePosition = value;
            _isUnInitialized = false;
            EmitSignal("OnMove", oldPosition, value, this);
        }
    }

    public override sealed void _Ready()
    {
        ArticleStatus.InitStatus(this);
    }

    public bool IsOpponent(ArticleBase article)
    {
        if (article == null) return false;

        if (this is not ITurnAffectedArticle<ArticleBase> || article is not ITurnAffectedArticle<ArticleBase>) return false;
        
        return article.GetParent().Name != "Neutral" && article.GetParent().Name != GetParent().Name;
    }
}