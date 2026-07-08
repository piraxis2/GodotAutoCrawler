using System;
using System.Linq;
using AutoCrawler.Assets.Script.Article.Status.Affect;
using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;
using Godot.Collections;

namespace AutoCrawler.Assets.Script.Article.Status;

public partial class ArticleStatus : Resource
{
    [Export] private Array<StatusElement> StatusElements { get; set; } 
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
        // Apply()에서 Cost가 0이 되면 StatusAffect가 OnAffectedEnd -> RemoveAffectStatus()로
        // AffectingStatusesList를 수정한다. 순회 중 수정을 피하려고 snapshot을 돈다.
        foreach (var affectStatus in AffectingStatusesList.ToArray())
        {
            affectStatus.Apply(this);
        }
    }

    // 턴 시작에 반응하는 StatusElement를 TurnStartOrder 오름차순으로 1회씩 실행한다.
    // 무엇을 어떻게 바꾸는지는 각 ITurnStartStatusElement가 소유하므로 새 스탯 추가에 이 클래스는 변하지 않는다.
    public void ApplyTurnStartStatusElements()
    {
        var turnStartElements = StatusElementsDictionary
            .Values
            .OfType<ITurnStartStatusElement>()
            .OrderBy(element => element.TurnStartOrder)
            // Dictionary 열거 순서에 의존하지 않도록 동점자는 타입 이름으로 고정한다.
            .ThenBy(element => element.GetType().FullName, StringComparer.Ordinal)
            .ToList();

        foreach (var element in turnStartElements)
        {
            // 앞선 스탯이 사망을 만들면 같은 hook 안에서 뒤 스탯을 적용하지 않는다.
            if (!HasLivingHealth()) return;
            element.ApplyTurnStart(this);
        }
    }

    // 생존 판정의 단일 진실. Health가 없는 Article은 살아 있는 것으로 본다. ArticleBase.IsAlive가 위임한다.
    public bool HasLivingHealth()
    {
        return !TryGetStatusElement(out Health health) || health.CurrentHealth > 0;
    }

    public bool TryGetStatusElement<TStatus>(out TStatus status) where TStatus : StatusElement
    {
        if (StatusElementsDictionary.TryGetValue(typeof(TStatus), out var element) && element is TStatus typed)
        {
            status = typed;
            return true;
        }

        status = null;
        return false;
    }
}
