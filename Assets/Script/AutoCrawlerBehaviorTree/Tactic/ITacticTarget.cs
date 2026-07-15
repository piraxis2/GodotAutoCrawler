using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

// 택틱 대상 후보/확정 대상이 노출하는 최소 seam(BT-002 Step 2). ADR-017 정규 동점 순서(거리→Y→X)에
// 필요한 타일 좌표만 요구한다. 실제 전투에서는 ArticleBase가 이를 만족하며(Step 3 배선), Step 2 테스트는
// 경량 Node 대역이 구현한다. 후보는 GodotObject이므로 freed 여부는 IsInstanceValid로 별도 확인한다.
public interface ITacticTarget
{
    Vector2I TilePosition { get; }
}
