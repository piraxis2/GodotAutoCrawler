using System.Collections.Generic;
using Godot;

namespace AutoCrawler;

public partial class TurnManager : Node
{
    private readonly Queue<Article> _turnQueue = new Queue<Article>();
    public void AddToQueue(Article article)
    {
        _turnQueue.Enqueue(article);
    }
    
    
}