using Godot;
using Godot.Collections;
using AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// 행의 출처(기획 H-8). player=플레이어 편집 행, default=잠긴 기본 system row(default_attack/terminal_wait),
// quirk=특성/장비가 부여하는 행(후속). ADR-024 §11에서 default row는 draft에 직렬화되고 validator가 보호한다.
public enum TacticRowSource
{
    Player,
    Default,
    Quirk
}

// 택틱보드의 한 행 초안(ADR-024 §2, 기획 H-3-1/H-8). 자식 순서가 아니라 명시 필드로 조건/대상/행동/접근 정책을
// 담는다. 런타임 TacticRow와 달리 실행 상태(context/latch)를 갖지 않는다.
//
// 잠긴 기본 system row는 Locked=true, Source=Default로 draft에 존재한다. compiler가 몰래 합성하지 않고
// validator가 손상/누락/중복/순서를 blocking Error로 보고한다(ADR-024 §11). null Target/Action과 빈 Conditions도
// 초안으로 저장 가능하며 Step 2 validator가 보고한다.
[GlobalClass, Tool]
public partial class TacticRowDefinition : Resource
{
    [Export] public StringName RowId { get; set; } = default;
    [Export] public bool Enabled { get; set; } = true;
    [Export] public bool Locked { get; set; } = false;
    [Export] public TacticRowSource Source { get; set; } = TacticRowSource.Player;
    [Export] public Array<TacticConditionDefinition> Conditions { get; set; } = new();
    // NearestEnemy | ContextTarget | null(targetless: wait 등). null도 유효한 초안 상태다.
    [Export] public TacticTargetDefinition Target { get; set; }
    [Export] public TacticActionDefinition Action { get; set; }
    // 컴파일러가 조건·행동 조합으로 결정하는 접근 정책(H-3-2). 런타임에서는 TacticTargetSelector.Policy가 된다.
    [Export] public TacticApproachPolicy ApproachPolicy { get; set; } = TacticApproachPolicy.ApproachAllowed;
}
