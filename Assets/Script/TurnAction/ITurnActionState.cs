namespace AutoCrawler.Assets.Script.TurnAction;

public interface ITurnActionState
{
    // windup(영창) 진행 중 여부. HUD/조건 어휘/영창 취소가 SkillState 구체 타입에 의존하지 않고 소비한다.
    bool IsCasting { get; }
}