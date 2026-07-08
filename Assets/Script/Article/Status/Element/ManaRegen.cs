using Godot;

namespace AutoCrawler.Assets.Script.Article.Status.Element;

[GlobalClass, Tool]
public partial class ManaRegen : StatusElement, ITurnStartStatusElement
{
    [Export] public int Value { get; set; } = 0;

    // HealthRegen(100) 뒤에 실행한다. 음수 HealthRegen이 사망을 만들면 이 스탯은 실행되지 않는다.
    public int TurnStartOrder => 110;

    // Mana가 없거나 Value == 0이면 no-op. 회복은 Mana.CurrentMana setter를 통해 적용해 clamp와 signal을 재사용한다.
    public void ApplyTurnStart(ArticleStatus status)
    {
        if (Value == 0) return;
        if (!status.TryGetStatusElement(out Mana mana)) return;

        mana.CurrentMana += Value;
    }
}
