using Godot;

namespace AutoCrawler.Assets.Script.Article;

public partial class ArticleBase : Node2D
{

    [Export] public Vector2I TempTilePosition;
    [Signal]
    public delegate void OnMoveEventHandler(Vector2I from, Vector2I to, ArticleBase article);

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
    
    public override void _Ready()
    {
        base._Ready();
        TilePosition = TempTilePosition;
    }
}