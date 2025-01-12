using AutoCrawler.Assets.Script.Article.Interface;
using Godot;

namespace AutoCrawler.Assets.Script.Article;

public partial class ArticleBase : Node2D
{
    [Export] 
    private AnimatedSprite2D _animatedSprite2D;
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
            if (!_isUnInitialized && _tilePosition == value && this is IFixed<ArticleBase>) return;
            
            Vector2I oldPosition = _tilePosition;
            _tilePosition = value;
            _isUnInitialized = false;
            EmitSignal("OnMove", oldPosition, value, this);
        }
    }

    public bool IsOpponent(ArticleBase article)
    {
        if (article == null)
        {
            return false;
        }
        return article.GetParent().Name != "Neutral" && article.GetParent().Name != GetParent().Name;
    }
}