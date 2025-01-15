using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article.Status.Element;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public abstract class StatusAffect  
{
    //어디에 적용되는지
    public delegate void OnAffectedEndEventHandler();
    public event OnAffectedEndEventHandler OnAffectedEnd;
    public abstract HashSet<Type> AffectedType { get; }
    public uint UniqId { get; set; } = 0;
    //코스트
    protected virtual int MasterCost => 1;
    private int _usedCost = 0;
    public int Cost => MasterCost - _usedCost;

    public void Apply(ArticleStatus recipient)
    {
        foreach (var statusElement in recipient.StatusElementsDictionary.Values)
        {
            OnApply(statusElement, recipient);
        }
        _usedCost++;
        if (Cost <= 0) OnAffectedEnd?.Invoke();
    }

    protected abstract void OnApply<T>(T type, ArticleStatus recipient) where T : StatusElement;
}