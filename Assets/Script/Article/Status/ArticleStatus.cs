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
    private System.Collections.Generic.Dictionary<uint, StatusAffect> AffectingStatusesDictionary { get; set; } = new();

    public ArticleBase Owner { get; private set; }
    public void InitStatus(ArticleBase owner)
    {
        Owner = owner;
        foreach (var stat in StatusElements)
        {
            if (stat.Duplicate(true) is not StatusElement duplicatedStat) continue;
            duplicatedStat.Init(owner);
            StatusElementsDictionary.Add(duplicatedStat.GetType(), duplicatedStat);
        }
    }
    
    public void ApplyAffectStatus(StatusAffect statusAffect)
    {
        if (statusAffect.Cost > 1)
        {
            statusAffect.UniqId = _affectStatusUniqId++;
            statusAffect.OnAffectedEnd += () => RemoveAffectStatus(statusAffect);
            AffectingStatusesDictionary.Add(statusAffect.UniqId, statusAffect);
        }
        statusAffect.Apply(this);
    }

    public void RemoveAffectStatus(StatusAffect statusAffect)
    {
        if (AffectingStatusesDictionary.ContainsKey(statusAffect.UniqId))
        {
            AffectingStatusesDictionary.Remove(statusAffect.UniqId);
        }
    }

    public void ApplyAffectingStatuses()
    {
        foreach (var affectStatus in AffectingStatusesDictionary.Values)
        {
            affectStatus.Apply(this);
        }
    }
}