using System;
using System.Collections.Generic;
using AutoCrawler.Assets.Script.Article.Status.Element;

namespace AutoCrawler.Assets.Script.Article.Status.Affect;

public abstract partial class Damage : StatusAffect
{
    public override HashSet<Type> AffectedType => new() { typeof(Health) }; 
}