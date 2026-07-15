namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 타깃 셀렉터 노드의 마커 seam(BT-002 Step 2). TacticRow는 이 인터페이스로 셀렉터를 식별해, 셀렉터가
// "마지막 pre-action 자식"(행동 바로 앞)임을 강제한다. 그래야 모든 조건이 후보를 모은 뒤 셀렉터가 대상을
// 확정한다 — 셀렉터 뒤에 대상 조건이 오면 교집합을 우회하므로 fail-closed한다.
public interface ITacticTargetSelector
{
}
