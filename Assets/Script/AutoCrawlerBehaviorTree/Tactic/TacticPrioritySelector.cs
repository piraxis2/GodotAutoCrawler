using Godot;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 택틱보드의 행 우선순위 selector(BT-002 Step 1, 기획 H-3/H-3-1, Step 0 OD2).
//
// 일반 BehaviorTree_Selector와 두 가지가 다르다.
//  1. Running 행을 latch한다. 다음 tick에 위에서부터 재평가하지 않고 latch된 행만 재개하므로, 상위 행의
//     조건이 실행 중 성립해도 preempt하지 않는다(R7). 일반 Selector는 매 tick 첫 자식부터 재평가한다.
//  2. commit 이후 결과(Success로 접힌 CancelledAfterCommit 포함)는 다음 행으로 fallback하지 않는다(R6).
//     Ineligible(=Failure)만 다음 행을 허용한다(R2).
//
// 턴 reset(R1): OnInit은 직전 _status가 Success/Failure일 때만 호출되므로 곧 "새 자기 턴"의 첫 tick과
// 일치한다(Running 재개 tick에는 호출되지 않는다). 여기서 latch를 풀고 하위 노드에 ResetForNewTurn을
// 전파한다. 인스턴스 필드(_latchedIndex)라 같은 구조의 두 유닛도 latch를 공유하지 않는다(R9).
[GlobalClass, Tool]
public partial class TacticPrioritySelector : BehaviorTree_Composite, ITacticNode
{
    private int _latchedIndex = -1;

    public TacticResult LastResult { get; private set; } = TacticResult.Ineligible;

    // 편의 reset: OnInit은 직전 _status가 terminal(Success/Failure)일 때만 호출되므로 흔한 경우 "새 자기 턴의
    // 첫 tick"과 일치한다. 그러나 이것이 턴 경계의 유일한 근거는 아니다 — 멀티턴 action은 CurrentTurnAction으로
    // BT tick을 우회하다가, BT가 Running인 채 외부에서 종료될 수 있다. 그때는 다음 자기 턴에도 base status가
    // Running이라 OnInit이 호출되지 않는다. 실제 턴 시작 배선은 ResetForNewTurn()을 명시 호출하며(Step 3에서
    // CharacterArticle.TurnPlay 경로에 연결), OnInit은 terminal 경로의 보조 reset일 뿐이다.
    protected override void OnInit(Node owner)
    {
        ResetForNewTurn();
    }

    public void ResetForNewTurn()
    {
        _latchedIndex = -1;
        LastResult = TacticResult.Ineligible;
        foreach (var child in TreeChildren)
        {
            if (child is ITacticNode tacticChild) tacticChild.ResetForNewTurn();
        }
    }

    protected override BtStatus OnBehave(double delta, Node owner)
    {
        var children = TreeChildren;

        // latch된 Running 행을 직접 재개한다. 위에서부터 재평가하지 않으므로 상위 행이 preempt할 수 없다(R7).
        if (_latchedIndex >= 0 && _latchedIndex < children.Count)
        {
            BtStatus resumed = children[_latchedIndex].Behave(delta, owner);
            LastResult = ResultOf(children[_latchedIndex]);
            ReportRow(children[_latchedIndex]);
            if (resumed == BtStatus.Running) return BtStatus.Running;

            // commit된 행이 끝났다(완료/접근 소비/취소). latch를 풀고 그대로 반환해 턴을 종료한다.
            // 재개 분기에서 바로 반환하므로 다른 행을 실행하지 않는다(R6).
            _latchedIndex = -1;
            return resumed;
        }

        // 새 평가: 위에서 아래로 최초 성립 행 하나를 고른다(R2).
        for (int i = 0; i < children.Count; i++)
        {
            // Selector 자식은 택틱 노드(TacticRow 등)만 실행한다. 일반 BT 노드는 commit 규칙을 우회하므로
            // 실행하지 않고 fail-closed로 건너뛴다(구조 오류는 Step 4 validation이 표면화). latch도 택틱 자식만
            // 가리키므로 재개 경로는 raw 노드를 만나지 않는다.
            if (children[i] is not ITacticNode)
            {
                GD.PushError($"TacticPrioritySelector '{Name}': 자식 '{children[i].Name}'이 택틱 노드가 아닙니다. 실행하지 않고 건너뜁니다.");
                continue;
            }

            BtStatus status = children[i].Behave(delta, owner);
            LastResult = ResultOf(children[i]);

            if (status == BtStatus.Failure) continue;          // Ineligible -> 다음 행 fallback

            ReportRow(children[i]);                            // 선택된 행을 디버그 tick에 식별시킨다
            if (status == BtStatus.Running)                    // 성립 후 실행 중 -> 이 행을 latch
            {
                _latchedIndex = i;
                return BtStatus.Running;
            }
            return status;                                      // 성립 후 이번 tick 완료 -> 턴 종료
        }

        // 어떤 행도 성립하지 않음. 완전한 보드에서는 최하단 TacticWait가 이 경로를 막는다(R8, Step 3).
        LastResult = TacticResult.Ineligible;
        return BtStatus.Failure;
    }

    // 자식의 내부 결과를 읽는다. 택틱 노드가 아니면(방어적) Ineligible로 간주한다.
    private static TacticResult ResultOf(BehaviorTree_Node node)
        => node is ITacticNode tacticNode ? tacticNode.LastResult : TacticResult.Ineligible;

    // 이번 tick에 실행 중인 행의 RowId를 디버그 tick에 싣는다(Step 4). DebugEnabled가 꺼져 있으면 호출조차
    // 하지 않아 debug-off 비용 계약을 깨지 않는다.
    private void ReportRow(BehaviorTree_Node row)
    {
        if (Tree is not { DebugEnabled: true }) return;
        if (row is TacticRow tacticRow) Tree.ReportTacticRow(tacticRow.RowId, (int)LastResult);
    }
}
