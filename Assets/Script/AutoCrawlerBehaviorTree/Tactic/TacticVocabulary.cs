using System.Linq;
using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// BT-002 Step 3.5 최소 제품 어휘(H-4 v0 일부). 전체 어휘와 compiler는 후속이다.
//
// 대상 셀렉터 의미는 "어떤 조건이 후보를 공급했는가"로 갈린다:
//  - `가장 가까운 적`  = TacticConditionAnyEnemy(모든 살아 있는 상대) + TacticTargetSelector(정규 순서 = 최근접)
//  - `그 적`(문맥 대상) = TacticConditionEnemyCasting 등 target-bearing 조건의 후보 + 같은 셀렉터

// `항상` — 후보를 만들지 않는 targetless gate.
[GlobalClass, Tool]
public partial class TacticConditionAlways : TacticCondition
{
    protected override BtStatus PerformAction(double delta, Node owner) => BtStatus.Success;
}

// `적이 있음` — 살아 있는 상대 전체를 문맥 후보로 공급한다(최근접 적 행의 후보 소스).
[GlobalClass, Tool]
public partial class TacticConditionAnyEnemy : TacticCondition
{
    protected override BtStatus PerformAction(double delta, Node owner)
    {
        ITacticActionAdapter adapter = (owner as ITacticActionAdapterProvider)?.TacticAdapter;
        if (adapter == null || Context == null) return BtStatus.Failure;

        var opponents = adapter.GetLivingOpponents(owner);
        if (opponents.Count == 0) return BtStatus.Failure;

        Context.ContributeCandidates(opponents);
        return BtStatus.Success;
    }
}

// `적이 영창 중` — 영창 중인 살아 있는 상대만 문맥 후보로 공급한다(영창 방해 행의 후보 소스, H-3-2).
// 후보가 없으면 조건 거짓이므로 행이 불성립하고 다음 행으로 넘어간다.
[GlobalClass, Tool]
public partial class TacticConditionEnemyCasting : TacticCondition
{
    protected override BtStatus PerformAction(double delta, Node owner)
    {
        ITacticActionAdapter adapter = (owner as ITacticActionAdapterProvider)?.TacticAdapter;
        if (adapter == null || Context == null) return BtStatus.Failure;

        var casting = adapter.GetLivingOpponents(owner)
            .Where(opponent => opponent is CharacterArticle { IsCasting: true })
            .ToList();

        if (casting.Count == 0) return BtStatus.Failure;

        Context.ContributeCandidates(casting);
        return BtStatus.Success;
    }
}
