using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 택틱 조건의 공통 seam(BT-002 Step 2). 조건은 두 종류다(기획 H-3-2, R3).
//  - targetless gate: 참/거짓만 판정하고 후보를 만들지 않는다. `Success`(참)/`Failure`(거짓)를 반환한다.
//  - target-bearing: 매칭 유닛을 문맥 후보로 기여한다. 매칭이 없으면 조건 거짓(`Failure`), 있으면
//    `Context.ContributeCandidates(...)` 후 `Success`. 여러 target-bearing 조건은 컨텍스트에서 교집합된다.
//
// 조건은 동기 순수 gate다: 성립 검사 중 자원 지불·이동·상태 효과를 일으키지 않고 `Running`을 반환하지 않는다.
// 조건 자리는 행이 pre-action gate로 평가하므로 BehaviorTree_Action(leaf) 위에 세운다.
[GlobalClass, Tool]
public abstract partial class TacticCondition : BehaviorTree_Action, ITacticContextBound
{
    protected TacticRowContext Context { get; private set; }

    public void BindContext(TacticRowContext context) => Context = context;
}
