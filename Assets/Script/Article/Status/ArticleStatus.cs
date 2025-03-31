using System;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Article.Status;

public partial class ArticleStatus : Resource
{
    [Export] private Array<StatusElement> StatusElements { get; set; } = new();
    public System.Collections.Generic.Dictionary<Type, StatusElement> StatusElementsDictionary { get; } = new();
    private uint _affectStatusUniqId;
    private System.Collections.Generic.List<StatusAffect> AffectingStatusesList { get; set; } = new();

    public ArticleBase Owner { get; private set; }
    public void InitStatus(ArticleBase owner)
    {
        Owner = owner;
        foreach (var stat in StatusElements)
        {
            StatusElementsDictionary.Add(stat.GetType(), stat);
            stat.Init(owner);
        }
    }
    
    public void ApplyAffectStatus(StatusAffect statusAffect)
    {
        if (statusAffect is not IAffectedImmediately)
        {
            statusAffect.OnAffectedEnd += () => RemoveAffectStatus(statusAffect);
            AffectingStatusesList.Add(statusAffect);
        }

        if (statusAffect is IAffectedImmediately)
        {
            statusAffect.Apply(this);
        }
    }

    private void RemoveAffectStatus(StatusAffect statusAffect)
    {
        AffectingStatusesList.Remove(statusAffect);
    }

    public void ApplyAffectingStatuses()
    {
        foreach (var affectStatus in AffectingStatusesList)
        {
            affectStatus.Apply(this);
        }
    }
}