using System;
using System.Collections.Generic;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public abstract class StatusAffect  
{
    public Action OnAffectedEnd { get; set; }
    public abstract HashSet<Type> AffectedType { get; }
    public int UniqId { get; set; } = 0;
    protected int MasterCost => 0;
    private int _usedCost = 0;
    private int Cost => MasterCost - _usedCost;

    public void Apply(ArticleStatus articleStatus)
    {
        foreach (var statusElement in articleStatus.StatusElementsDictionary.Values)
        {
            if (AffectedType.Contains(statusElement.GetType()))
            {
                OnApply(statusElement.GetType(), articleStatus);
            }
        }
        _usedCost++;
        if (Cost <= 0) OnAffectedEnd.Invoke();
    }

    protected abstract void OnApply(Type type, ArticleStatus articleStatus);
}