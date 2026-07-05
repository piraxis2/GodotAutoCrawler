---
type: system
system: ArticleStatus
status: active
updated: 2026-07-05
---

# Article Status System

## Agent Brief

- 주요 위치: `Assets/Script/Article`, `Assets/Script/Article/Status`
- 책임: 게임 객체, 능력치, 지속 효과, 피해 처리
- 기반 객체: `ArticleBase : Node2D`
- 상태 컨테이너: `ArticleStatus : Resource`

## Model

- StatusElement: Health, Strength, Defense, Intelligence, Luck, Mobility
- StatusAffect: 물리/마법 피해 및 지속 효과
- ArticleBase: 위치, 생존, 이동/사망 signal, 애니메이션

## Flow

TurnAction이 대상의 `ArticleStatus.ApplyAffectStatus()`를 호출한다. 즉시 효과는 바로 적용되고 지속 효과는
목록에 추가돼 이후 적용된다.

지속 효과 목록(`AffectingStatusesList`)은 `CharacterArticle.ApplyTurnStartEffects()`를 통해 해당 유닛의
턴 시작 시 1회 적용된다. Running action frame 반복, 애니메이션 길이, `TurnHelper.Speed`는 지속 효과 적용
횟수를 늘리지 않는다.

## Damage RNG

`PhysicalDamage`는 크리티컬 판정과 데미지 롤에 Godot 전역 RNG를 쓰지 않고,
`BattleFieldScene.BattleField.TurnHelper`의 전투 `CombatRng`를 소비한다. 같은 seed와 같은 판정 순서에서는
같은 크리티컬/데미지 시퀀스가 재현된다.

## Known Risks

- 필수 StatusElement가 없는 리소스의 dictionary 접근
- StatusElements 배열 null 또는 중복 타입
- 사망 signal과 QueueFree의 순서
- Resource 공유로 인한 여러 캐릭터 간 상태 공유 여부

## Related

- [[Turn-System]]
- [[CB-001-Deterministic-Combat-Resolution]]