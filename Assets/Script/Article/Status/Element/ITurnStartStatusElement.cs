namespace AutoCrawler.Assets.Script.Article.Status.Element;

// 유닛 턴 시작에 스스로 반응하는 StatusElement 계약.
// ArticleStatus는 이 계약만 알고, 어떤 대상 스탯을 어떻게 바꿀지는 각 StatusElement가 소유한다.
public interface ITurnStartStatusElement
{
    // 오름차순 실행. 같은 hook 안에서 앞선 스탯이 사망을 만들면 뒤 스탯은 실행되지 않는다.
    int TurnStartOrder { get; }

    void ApplyTurnStart(ArticleStatus status);
}
