using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public partial class HealthRegen : StatusElement, ITurnStartStatusElement
{
    [Export] public int Value { get; set; } = 0;

    public int TurnStartOrder => 100;

    // Health가 없거나 Value == 0이면 no-op. 회복은 Health.CurrentHealth setter를 통해 적용해
    // clamp, signal, OverheadUi 갱신, 사망 처리를 재사용한다.
    public void ApplyTurnStart(ArticleStatus status)
    {
        if (Value == 0) return;
        if (!status.TryGetStatusElement(out Health health)) return;

        health.CurrentHealth += Value;
    }
}
