using System;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Article.Status;

[GlobalClass, Tool]
public partial class ArticleStatus : Resource
{
    [Export] private Array<StatusElement> StatusElements { get; set; } = new Array<StatusElement>();

    public System.Collections.Generic.Dictionary<Type, StatusElement> StatusElementsDictionary { get; } = new();
    private int _affectStatusUniqId = 0;
    private System.Collections.Generic.Dictionary<int, StatusAffect> AffectingStatusesDictionary { get; set; } = new();

    public ArticleBase Owner { get; private set; }
    public void InitStatus(ArticleBase owner)
    {
        Owner = owner;
        foreach (var stat in StatusElements)
        {
            stat.Init(owner);
            StatusElementsDictionary.Add(stat.GetType(), stat);
        }
    }

    public void AddAffectStatus(StatusAffect statusAffect)
    {
        statusAffect.UniqId = _affectStatusUniqId++;
        statusAffect.OnAffectedEnd += () => RemoveAffectStatus(statusAffect);
        AffectingStatusesDictionary.Add(statusAffect.UniqId, statusAffect);
    }

    public void RemoveAffectStatus(StatusAffect statusAffect)
    {
        AffectingStatusesDictionary.Remove(statusAffect.UniqId);
    }

    public void ApplyAffectingStatuses()
    {
        foreach (var affectStatus in AffectingStatusesDictionary.Values)
        {
            affectStatus.Apply(this);
        }
    }
}