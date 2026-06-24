---
type: system
system: Turn
status: active
updated: 2026-06-11
---

# Turn System

## Agent Brief

- 주요 파일: `Assets/Script/TurnHelper.cs`, `Assets/Script/Article/CharacterArticle.cs`
- 책임: 턴 대상 등록, 순서 결정, 현재 유닛 실행, 다음 턴 전환
- 주의: 사망 중 리스트 변경, 게임오버 조건, Speed 0 일시정지

## Flow

`TurnHelper._PhysicsProcess()`가 현재 유닛의 `TurnPlay(delta * Speed)`를 호출한다. 결과가 Success 또는 Failure이면 다음 유닛으로 넘어간다.

`CharacterArticle.TurnPlay()`는 지속 상태 효과를 적용하고, 진행 중인 TurnAction이 없으면 BehaviorTree를 실행한다.

## Known Gaps

- 게임오버 후속 처리가 TODO 상태다.
- Priority 정렬은 낮은 값이 먼저 실행되는 현재 구현을 기준으로 한다.
- 상태 효과 적용 시점이 매 frame이 아닌 턴당 한 번인지 지속적으로 검증해야 한다.

## Related

- [[BehaviorTree-System]]
- [[Article-Status-System]]

