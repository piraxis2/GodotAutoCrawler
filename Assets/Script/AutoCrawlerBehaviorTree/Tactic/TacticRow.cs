using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 택틱보드의 한 행(BT-002 Step 1~2, 기획 H-3-1/H-8). 자식은 [pre-action 게이트0..N-1, 행동] 순서이며
// 마지막 자식이 행동, 그 앞이 pre-action 게이트(조건과 타깃 셀렉터)다.
//
//  - pre-action 게이트는 행을 "선택할 때" 순서대로 한 번만 평가한다. 하나라도 실패하면 행은 Ineligible이고
//    후속 자식(행동)은 실행하지 않는다(R3의 mutation 금지: 게이트가 막으면 행동을 건드리지 않음).
//  - 모든 게이트가 통과하면 행동을 실행하고 그 내부 결과를 그대로 행의 결과로 전파한다.
//  - 행동이 Running이면 _executing을 세워, 재개 tick에는 게이트를 다시 평가하지 않고 행동만 잇는다
//    (H-3-1 "조건·대상은 선택 시 평가", R7 "실행 중 조건 변화로 preempt 금지"의 행 내부 보장).
//
// Step 2: 행은 유닛별 TacticRowContext를 소유하고 ITacticContextBound 자식(조건·셀렉터·행동)에 주입한다.
// 조건이 후보를 모으고 타깃 셀렉터가 하나를 확정하면 행동이 같은 확정 대상을 공유한다(ADR-023 §5). 컨텍스트는
// 새 자기 턴에 폐기된다. 접근 정책·실제 이동은 Step 3 범위다.
[GlobalClass, Tool]
public partial class TacticRow : BehaviorTree_Composite, ITacticNode
{
    // 디버그/발동 통계용 행 식별자 seam(Step 4). 지금은 보관만 한다.
    [Export] public string RowId { get; set; } = "";

    // 게이트를 통과해 행동 실행 단계에 들어갔는지. Running 동안 유지되어 게이트 재평가를 건너뛴다.
    private bool _executing;

    // 유닛별 행 문맥. 인스턴스 필드라 같은 구조의 두 유닛도 후보·확정 대상을 공유하지 않는다(R9).
    private readonly TacticRowContext _context = new();

    public TacticResult LastResult { get; private set; } = TacticResult.Ineligible;

    protected override void OnInit(Node owner)
    {
        ResetForNewTurn();
    }

    public void ResetForNewTurn()
    {
        _executing = false;
        LastResult = TacticResult.Ineligible;
        _context.Reset();
        foreach (var child in TreeChildren)
        {
            if (child is ITacticNode tacticChild) tacticChild.ResetForNewTurn();
        }
    }

    protected override BtStatus OnBehave(double delta, Node owner)
    {
        var children = TreeChildren;
        if (children.Count == 0)
        {
            // 행동이 없는 행은 성립할 수 없다(구조 오류는 Step 4 validation이 잡는다).
            LastResult = TacticResult.Ineligible;
            return BtStatus.Failure;
        }

        // 이 행의 컨텍스트를 자식(조건·셀렉터·행동)에 주입한다. 조건이 후보를 모으고 셀렉터가 확정하면
        // 행동이 같은 확정 대상을 읽는다. 바인딩은 idempotent하며 컨텍스트 인스턴스는 턴 내내 유지된다.
        foreach (var child in children)
        {
            if (child is ITacticContextBound bound) bound.BindContext(_context);
        }

        BehaviorTree_Node actionNode = children[^1];

        // 행동 자리는 택틱 행동(ITacticNode)만 허용한다. 일반 BT action은 commit 구분(Ineligible vs
        // CancelledAfterCommit)을 보고하지 못해, 이동·비용 지불 뒤의 Failure가 다음 행으로 새는 것을 막을 수
        // 없다(R6 위반). 잘못된 구조는 실행하지 않고 fail-closed한다(구조 오류는 Step 4 validation이 표면화).
        if (actionNode is not ITacticNode tacticAction)
        {
            GD.PushError($"TacticRow '{Name}': 마지막 자식 '{actionNode.Name}'이 ITacticNode 행동이 아닙니다. 행을 실행하지 않습니다.");
            LastResult = TacticResult.Ineligible;
            return BtStatus.Failure;
        }

        // pre-action 구조 검증: 타깃 셀렉터는 마지막 pre-action 자식(행동 바로 앞)이어야 하고 최대 하나다.
        // 셀렉터 뒤에 대상 조건이 오면, 조건이 후보를 좁히기 전에 셀렉터가 대상을 확정해 교집합을 우회한다
        // ("두 대상 조건은 같은 유닛이 모두 만족할 때만 성립" 위반). 잘못된 위치의 셀렉터는 fail-closed한다.
        int lastPreActionIndex = children.Count - 2;
        for (int i = 0; i <= lastPreActionIndex; i++)
        {
            if (children[i] is ITacticTargetSelector && i != lastPreActionIndex)
            {
                GD.PushError($"TacticRow '{Name}': 타깃 셀렉터 '{children[i].Name}'는 마지막 pre-action 자식이어야 합니다(행동 바로 앞, 최대 하나). 행을 실행하지 않습니다.");
                LastResult = TacticResult.Ineligible;
                return BtStatus.Failure;
            }
        }

        // 선택 단계: 조건 게이트를 순서대로 평가한다. 실행 단계에 들어간 뒤에는 건너뛴다.
        if (!_executing)
        {
            for (int i = 0; i < children.Count - 1; i++)
            {
                // 조건은 동기 순수 gate다. Success만 통과한다.
                BtStatus condStatus = children[i].Behave(delta, owner);
                if (condStatus == BtStatus.Running)
                {
                    // Running 조건은 gate가 아니라 잘못된 구조다. 행동을 실행하지 않고 fail-closed한다.
                    GD.PushError($"TacticRow '{Name}': 조건 '{children[i].Name}'이 Running을 반환했습니다(gate는 동기여야 함). 행을 실행하지 않습니다.");
                    LastResult = TacticResult.Ineligible;
                    return BtStatus.Failure;
                }
                if (condStatus == BtStatus.Failure)
                {
                    // 정상 불성립: 다음 행으로 fallback.
                    LastResult = TacticResult.Ineligible;
                    return BtStatus.Failure;
                }
            }
        }

        // 실행 단계: 택틱 행동의 내부 결과를 그대로 행의 결과로 전파한다(commit 구분 보존).
        actionNode.Behave(delta, owner);
        LastResult = tacticAction.LastResult;

        // Running 동안만 실행 단계를 latch한다. 종료 결과가 나오면 행이 끝나 다음 평가에서 조건을 다시 본다.
        _executing = LastResult == TacticResult.Running;

        return LastResult.ToBtStatus();
    }
}
