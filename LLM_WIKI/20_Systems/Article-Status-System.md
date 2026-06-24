---
type: system
system: ArticleStatus
status: active
updated: 2026-06-11
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

TurnAction이 대상의 `ArticleStatus.ApplyAffectStatus()`를 호출한다. 즉시 효과는 바로 적용되고 지속 효과는 목록에 추가돼 이후 적용된다.

## Known Risks

- 필수 StatusElement가 없는 리소스의 dictionary 접근
- StatusElements 배열 null 또는 중복 타입
- 사망 signal과 QueueFree의 순서
- Resource 공유로 인한 여러 캐릭터 간 상태 공유 여부

