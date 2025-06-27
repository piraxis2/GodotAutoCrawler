using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article.Status.Element;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public abstract class StatusAffect  
{
    public delegate void OnAffectedEndEventHandler();
    public event OnAffectedEndEventHandler OnAffectedEnd;
    
    //어디에 적용되는지
    public abstract HashSet<Type> AffectedType { get; }
    //코스트
    protected virtual int MasterCost => 1;
    private int _usedCost = 0;
    public int Cost => MasterCost - _usedCost;

    public void Apply(ArticleStatus recipient)
    {
        foreach (var statusElement in recipient.StatusElementsDictionary.Values.Where(statusElement => AffectedType.Contains(statusElement.GetType())))
        {
            if (this is IAffectedImmediately immediately) immediately.ApplyImmediately(statusElement, recipient);
            
            if (this is IAffectedOnlyMyTurn onlyMyTurn) onlyMyTurn.ApplyOnlyMyTurn(statusElement, recipient);
            
            if (this is IAffectedUntilTheEnd applyUntilTheEnd && _usedCost == 0) applyUntilTheEnd.ApplyAffect(statusElement, recipient);
        }
        
        _usedCost++;
        if (Cost > 0) return;
        
        AffectedEnd(recipient);
    }

    private void AffectedEnd(ArticleStatus recipient)
    {
        foreach (var statusElement in recipient.StatusElementsDictionary.Values.Where(statusElement => AffectedType.Contains(statusElement.GetType())))
        {
            if (this is IAffectedUntilTheEnd applyUntilTheEnd) applyUntilTheEnd.UnapplyAffect(statusElement, recipient);
        }
        OnAffectedEnd?.Invoke();
    }

}