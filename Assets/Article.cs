using Godot;
using System;

public partial class Article : Node
{
    private ArticleAction _articleAction = null;
    public ArticleAction GetArticleAction()
    {
        return _articleAction??= new ArticleAction(this);
    }
}
